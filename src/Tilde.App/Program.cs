using Tilde.Core;
using Tilde.Platform;

namespace Tilde.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Una sola instancia por usuario: si ya está abierta, salimos sin hacer nada.
        using var mutex = new Mutex(true, @"Local\Tilde.SingleInstance.6f3c2a", out bool isFirst);
        if (!isFirst)
        {
            return 0;
        }

        AppPaths.EnsureCreated();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Excepción no controlada", e.ExceptionObject as Exception);
        Strings.Spanish = Language.IsSpanishUi();
        // OLE es necesario para arrastrar y soltar (estante) en este hilo de interfaz.
        Tilde.Platform.Interop.Native.OleInitialize(0);
        Log.Info($"Tilde {TildeApp.Version} iniciado");

        try
        {
            using var app = new TildeApp();
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Error("Error fatal al iniciar", ex);
            return 1;
        }
        finally
        {
            Log.Info("Tilde cerrado");
        }
    }
}
