using System.Runtime.InteropServices;
using Tilde.Core;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform.Audio;

/// <summary>
/// Volumen maestro del dispositivo de salida predeterminado (Core Audio).
///
/// Core Audio es COM «clásico». En lugar de depender de un generador de interop, llamamos a
/// los métodos por su posición en la tabla virtual (vtable), y para recibir avisos creamos
/// nosotros dos objetos COM mínimos: uno avisa cuando cambia el volumen (desde cualquier app,
/// teclas multimedia incluidas) y otro cuando cambia el dispositivo predeterminado (p. ej. al
/// conectar unos auriculares). Así el deslizador siempre está al día sin sondear.
/// </summary>
public sealed unsafe class SystemVolume : IDisposable
{
    private static readonly Guid ClsidDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid IidVolumeCallback = new("657804FA-D6AD-4496-8A60-352752AF4F89");
    private static readonly Guid IidNotificationClient = new("7991EEC9-7E89-4D85-8390-6C703CEC60C0");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");

    private static SystemVolume? s_instance;

    private readonly SynchronizationContext _ui;
    private readonly nint _enumerator;
    private nint _endpoint;
    private readonly nint _volumeCallback;
    private readonly nint _deviceCallback;

    public SystemVolume()
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Crear en el hilo de interfaz");
        s_instance = this;
        _volumeCallback = CreateComObject(VolumeCallbackVtbl());
        _deviceCallback = CreateComObject(DeviceCallbackVtbl());

        if (CoCreateInstance(ClsidDeviceEnumerator, 0, CLSCTX_ALL, IidDeviceEnumerator, out _enumerator) < 0)
        {
            Log.Warn("Core Audio no disponible: sin control de volumen");
            return;
        }
        // IMMDeviceEnumerator::RegisterEndpointNotificationCallback (vtable 6)
        Call<nint, int>(_enumerator, 6, _deviceCallback);
        BindDefaultDevice();
    }

    /// <summary>Volumen de 0 a 1.</summary>
    public float Level { get; private set; }

    public bool IsMuted { get; private set; }

    public bool IsAvailable => _endpoint != 0;

    /// <summary>Cambió el volumen o el silencio (se lanza en el hilo de interfaz).</summary>
    public event Action? Changed;

    public void SetLevel(float level)
    {
        if (_endpoint == 0) return;
        level = Math.Clamp(level, 0f, 1f);
        Level = level;
        // IAudioEndpointVolume::SetMasterVolumeLevelScalar (vtable 7)
        ((delegate* unmanaged<nint, float, Guid*, int>)Vtbl(_endpoint)[7])(_endpoint, level, null);
        if (IsMuted && level > 0)
        {
            SetMuted(false);
        }
    }

    public void SetMuted(bool muted)
    {
        if (_endpoint == 0) return;
        IsMuted = muted;
        // IAudioEndpointVolume::SetMute (vtable 14)
        ((delegate* unmanaged<nint, int, Guid*, int>)Vtbl(_endpoint)[14])(_endpoint, muted ? 1 : 0, null);
    }

    public void Dispose()
    {
        ReleaseEndpoint();
        if (_enumerator != 0)
        {
            Call<nint, int>(_enumerator, 7, _deviceCallback); // UnregisterEndpointNotificationCallback
            Release(_enumerator);
        }
        if (s_instance == this)
        {
            s_instance = null;
        }
        // Los objetos de aviso se quedan reservados: Windows podría llamarlos un instante
        // después de darlos de baja. Son unos pocos bytes y solo existen una vez.
    }

    private void BindDefaultDevice()
    {
        ReleaseEndpoint();
        nint device;
        // IMMDeviceEnumerator::GetDefaultAudioEndpoint(eRender = 0, eMultimedia = 1) (vtable 4)
        if (((delegate* unmanaged<nint, int, int, nint*, int>)Vtbl(_enumerator)[4])(_enumerator, 0, 1, &device) < 0)
        {
            Level = 0;
            Changed?.Invoke();
            return; // No hay altavoces ni auriculares.
        }
        nint endpoint;
        Guid iid = IidEndpointVolume;
        // IMMDevice::Activate (vtable 3)
        int hr = ((delegate* unmanaged<nint, Guid*, uint, nint, nint*, int>)Vtbl(device)[3])(device, &iid, CLSCTX_ALL, 0, &endpoint);
        Release(device);
        if (hr < 0)
        {
            return;
        }
        _endpoint = endpoint;
        Call<nint, int>(_endpoint, 3, _volumeCallback); // RegisterControlChangeNotify
        ReadState();
    }

    private void ReleaseEndpoint()
    {
        if (_endpoint != 0)
        {
            Call<nint, int>(_endpoint, 4, _volumeCallback); // UnregisterControlChangeNotify
            Release(_endpoint);
            _endpoint = 0;
        }
    }

    private void ReadState()
    {
        float level;
        int muted;
        ((delegate* unmanaged<nint, float*, int>)Vtbl(_endpoint)[9])(_endpoint, &level);   // GetMasterVolumeLevelScalar
        ((delegate* unmanaged<nint, int*, int>)Vtbl(_endpoint)[15])(_endpoint, &muted);    // GetMute
        Level = level;
        IsMuted = muted != 0;
        Changed?.Invoke();
    }

    // ── Ayudas COM ──

    private static nint* Vtbl(nint obj) => *(nint**)obj;

    private static TResult Call<TArg, TResult>(nint obj, int slot, TArg arg) where TArg : unmanaged where TResult : unmanaged =>
        ((delegate* unmanaged<nint, TArg, TResult>)Vtbl(obj)[slot])(obj, arg);

    private static void Release(nint obj) => ((delegate* unmanaged<nint, uint>)Vtbl(obj)[2])(obj);

    /// <summary>Crea un objeto COM nativo: un puntero a su tabla de métodos.</summary>
    private static nint CreateComObject(nint* vtbl)
    {
        var obj = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
        *obj = (nint)vtbl;
        return (nint)obj;
    }

    private static nint* VolumeCallbackVtbl()
    {
        var vtbl = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 4));
        vtbl[0] = (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryInterfaceVolume;
        vtbl[1] = (nint)(delegate* unmanaged<nint, uint>)&AddRef;
        vtbl[2] = (nint)(delegate* unmanaged<nint, uint>)&ReleaseSelf;
        vtbl[3] = (nint)(delegate* unmanaged<nint, nint, int>)&OnNotify;
        return vtbl;
    }

    private static nint* DeviceCallbackVtbl()
    {
        var vtbl = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 8));
        vtbl[0] = (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryInterfaceDevice;
        vtbl[1] = (nint)(delegate* unmanaged<nint, uint>)&AddRef;
        vtbl[2] = (nint)(delegate* unmanaged<nint, uint>)&ReleaseSelf;
        vtbl[3] = (nint)(delegate* unmanaged<nint, nint, uint, int>)&OnDeviceStateChanged;
        vtbl[4] = (nint)(delegate* unmanaged<nint, nint, int>)&OnDeviceAddedOrRemoved;
        vtbl[5] = (nint)(delegate* unmanaged<nint, nint, int>)&OnDeviceAddedOrRemoved;
        vtbl[6] = (nint)(delegate* unmanaged<nint, int, int, nint, int>)&OnDefaultDeviceChanged;
        vtbl[7] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&OnPropertyValueChanged;
        return vtbl;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterfaceVolume(nint self, Guid* iid, nint* result) => QueryInterface(self, *iid, IidVolumeCallback, result);

    [UnmanagedCallersOnly]
    private static int QueryInterfaceDevice(nint self, Guid* iid, nint* result) => QueryInterface(self, *iid, IidNotificationClient, result);

    private static int QueryInterface(nint self, Guid iid, Guid supported, nint* result)
    {
        if (iid == IidUnknown || iid == supported)
        {
            *result = self;
            return 0;
        }
        *result = 0;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    // Objetos estáticos de vida ilimitada: el recuento de referencias no importa.
    [UnmanagedCallersOnly]
    private static uint AddRef(nint self) => 1;

    [UnmanagedCallersOnly]
    private static uint ReleaseSelf(nint self) => 1;

    /// <summary>IAudioEndpointVolumeCallback::OnNotify: llega en un hilo del sistema.</summary>
    [UnmanagedCallersOnly]
    private static int OnNotify(nint self, nint data)
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: GUID (16 bytes), BOOL bMuted, float fMasterVolume…
        bool muted = *(int*)(data + 16) != 0;
        float level = *(float*)(data + 20);
        s_instance?._ui.Post(_ =>
        {
            if (s_instance is { } volume)
            {
                volume.Level = level;
                volume.IsMuted = muted;
                volume.Changed?.Invoke();
            }
        }, null);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDefaultDeviceChanged(nint self, int flow, int role, nint deviceId)
    {
        if (flow == 0 && role == 1) // eRender, eMultimedia
        {
            s_instance?._ui.Post(_ => s_instance?.BindDefaultDevice(), null);
        }
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDeviceStateChanged(nint self, nint deviceId, uint state) => 0;

    [UnmanagedCallersOnly]
    private static int OnDeviceAddedOrRemoved(nint self, nint deviceId) => 0;

    [UnmanagedCallersOnly]
    private static int OnPropertyValueChanged(nint self, nint deviceId, nint key) => 0;
}
