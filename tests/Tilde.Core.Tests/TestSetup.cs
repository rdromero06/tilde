using System.Runtime.CompilerServices;

namespace Tilde.Core.Tests;

internal static class TestSetup
{
    /// <summary>Las pruebas escriben sus logs en una carpeta temporal, nunca en los datos reales del usuario.</summary>
    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable("TILDE_DATA_DIR", Path.Combine(Path.GetTempPath(), "tilde-tests-data"));
}
