using System.Runtime.CompilerServices;

namespace OpenPatro.Tests;

internal static class TestLogRedirect
{
    /// <summary>Keeps the app's DiagnosticLog out of the real %LocalAppData%\OpenPatro\logs during tests.</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(
            "OPENPATRO_LOG_DIR",
            Path.Combine(Path.GetTempPath(), "openpatro-tests-logs"));
    }
}
