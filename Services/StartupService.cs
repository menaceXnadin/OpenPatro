using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace OpenPatro.Services;

/// <summary>
/// "Start with Windows". Unpackaged builds use the HKCU Run key; MSIX builds use the
/// manifest's <c>windows.startupTask</c>, because a packaged app's HKCU writes are
/// virtualized and never seen by Explorer.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenPatro";
    private const string StartupArgument = "--startup";
    private const string PackagedTaskId = "OpenPatroStartup"; // must match Package.appxmanifest

    private readonly bool _isPackaged = ApplicationPaths.IsRunningAsPackagedApp();

    public async Task<bool> IsEnabledAsync()
    {
        if (_isPackaged)
        {
            var task = await StartupTask.GetAsync(PackagedTaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Applies the requested state and returns the resulting state. For MSIX the user
    /// (or policy) can block enabling, in which case this returns false.
    /// </summary>
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (_isPackaged)
        {
            var task = await StartupTask.GetAsync(PackagedTaskId);
            if (enabled)
            {
                var state = await task.RequestEnableAsync();
                return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            task.Disable();
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {StartupArgument}");
            return true;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return false;
    }
}
