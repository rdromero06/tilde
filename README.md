# Tilde

Tilde convierte el borde superior de la pantalla en un «notch» vivo para Windows 10 (1809+) y
Windows 11 de 64 bits. Cerrado es una píldora negra con la hora; al pasar el ratón se abre con
una animación suave y muestra varias páginas (música, Pomodoro, agua, tareas, estante de
archivos y Claude).

Su mascota es la propia virgulilla «~»: cuando se duerme se aplana hasta convertirse en una
línea, que es también el aspecto del modo «notch mínimo».

> Estado: **fase 1 de 7** (ventana del notch y bandeja). Las páginas son marcadores que indican
> en qué fase llegan.

## Objetivos de diseño

| Objetivo | Cómo se consigue |
|---|---|
| ~30 MB de RAM, 0 % de CPU en reposo | Native AOT (sin JIT), dibujo con WARP y animaciones ejecutadas por el compositor de Windows (DirectComposition) |
| Fluido a 60/120/144/240 Hz | Las animaciones son curvas cúbicas que reproduce DWM a la frecuencia del monitor |
| Nítido a cualquier escala | DPI por monitor v2; todo se dibuja en vectores al tamaño exacto en píxeles |
| Sin sondeos | Mensajes de Windows, temporizador alineado al minuto, notificaciones de AppBar y de primer plano, FileSystemWatcher |
| Privacidad | Todo en `%LOCALAPPDATA%\Tilde`. Sin cuentas ni analíticas. Solo se conectará a LRCLIB y a la API de Anthropic |

Medido en la fase 1 (publicación AOT, Full HD, en reposo): **11,5 MB de memoria privada**,
28 MB de working set (la mayor parte son DLL del sistema compartidas) y **0 ms de CPU en 60 s**.

## Requisitos para compilar

- [SDK de .NET 10](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)
- Solo para publicar con Native AOT: Build Tools de Visual Studio con la carga de trabajo
  «Desarrollo de escritorio con C++»:

  ```powershell
  winget install Microsoft.VisualStudio.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
  ```

## Compilar, probar y ejecutar

```powershell
# Compilación de depuración + pruebas
powershell -ExecutionPolicy Bypass -File scripts\build.ps1

# Ejecutar la versión de depuración
src\Tilde.App\bin\Debug\net10.0-windows10.0.22621.0\win-x64\Tilde.exe

# Publicación final (Native AOT) en publish\Tilde.exe
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

El instalador por usuario (sin permisos de administrador) llega en la fase 7. Hasta entonces,
`publish\Tilde.exe` funciona tal cual: se puede copiar a cualquier carpeta.

## Uso (fase 1)

- **Pasa el ratón** por la píldora para abrir el notch; al salir se cierra.
- **Cambia de página** con la rueda del ratón (o desplazamiento horizontal del touchpad).
  Tras hacer clic en el notch también funcionan **← / →**, y **Esc** lo cierra.
- **Pasa el ratón por los puntos** de abajo: se convierten en iconos clicables.
- El botón de **deslizadores** (abajo a la derecha) y **Ajustes…** de la bandeja abren
  `settings.json` en el Bloc de notas. Al guardar, los cambios se aplican al instante.
- **Bandeja del sistema**: Iniciar con Windows, Mascota (tendrá efecto en la fase 5),
  Notch mínimo, Ajustes, Acerca de y Salir.
- Con una app **a pantalla completa** en el monitor principal, el notch se oculta.

### settings.json

```jsonc
{
  "version": 1,
  "notchSize": "Auto",       // Auto | Small | Medium | Large | ExtraLarge
  "minimalNotch": false,      // cerrado = línea fina
  "hideInFullscreen": true,   // ocultar con apps a pantalla completa
  "mascotEnabled": true,
  "lastPage": 0
}
```

«Auto» usa un tamaño mayor (×1,2) en monitores de al menos 2560 DIP de ancho o
ultrapanorámicos.

## Estructura

```
src/
  Tilde.Core/       Lógica pura y testeable: ajustes, métricas, curvas de animación, log
  Tilde.Platform/   Interop con Windows (LibraryImport): ventanas, bandeja, pantalla
                    completa, formato regional, inicio con Windows
  Tilde.Render/     Mini motor: Direct3D 11 (WARP) + Direct2D + DirectWrite + DirectComposition
  Tilde.App/        Tilde.exe: ventana del notch, capas, páginas, bandeja
tests/
  Tilde.Core.Tests/ Pruebas xUnit del núcleo
assets/             Icono (generado por scripts\make-icon.ps1)
scripts/            build.ps1, publish.ps1, make-icon.ps1
```

## Notas técnicas

- **Por qué WARP y no la GPU.** Crear un dispositivo Direct3D en la GPU hace que el driver
  reserve unos 45 MB dentro del proceso. Tilde dibuja muy poco (el reloj, una vez por minuto),
  así que rasteriza en la CPU con WARP (~12 MB). Las animaciones siguen en la GPU, porque las
  ejecuta DWM. Para forzar la GPU: variable de entorno `TILDE_RENDERER=gpu`. En la fase 7 será
  una opción de Ajustes.
- **Zonas transparentes.** La ventana mide lo que el notch abierto, pero su «región» se ajusta a
  la píldora cuando está cerrada, así que el resto no bloquea los clics.
- **Foco.** Pasar el ratón nunca roba el foco. Solo al hacer clic en el notch se toma el teclado
  (para las flechas), y se devuelve a la ventana anterior al cerrarse.
- **Logs.** `%LOCALAPPDATA%\Tilde\logs\tilde.log`. Nunca contienen claves ni texto del usuario.

### Variables solo para depuración (compilación Debug)

| Variable | Efecto |
|---|---|
| `TILDE_DEBUG_OFFSET_X=560` | Desplaza el notch en horizontal, para probarlo junto a otra app de notch |
| `TILDE_DEBUG_DEMO=1` | Abre el notch, cambia de página y lo cierra solo (un paso por segundo) |
| `TILDE_DATA_DIR=C:\ruta` | Usa otra carpeta de datos (también en Release) |

## Plan de fases

1. ✅ Ventana del notch y bandeja
2. Música (controles multimedia de Windows) y letras sincronizadas (LRCLIB)
3. Pomodoro, agua y tareas
4. Estante de archivos
5. HUD (volumen, brillo, Bloq Mayús, batería, Wi-Fi, Bluetooth) y mascota
6. Claude (streaming, contador de tokens, claves en el Administrador de credenciales)
7. Ventana de Ajustes, instalador y pulido
