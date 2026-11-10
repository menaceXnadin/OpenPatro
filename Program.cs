using Microsoft.Windows.AppLifecycle;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OpenPatro;

/// <summary>
/// Custom entry point that enforces single-instance behavior.
/// Replaces the auto-generated Main (disabled via DISABLE_XAML_GENERATED_MAIN).
/// </summary>
public static class Program
{
    [global::System.STAThread]
    static void Main(string[] args)
    {
        global::WinRT.ComWrappersSupport.InitializeComWrappers();

        // Register this process under a well-known key. If a previous instance is
        // already registered, IsCurrent == false and we redirect activation to it.
        var instance = AppInstance.FindOrRegisterForKey("OpenPatro-SingleInstance");

        if (!instance.IsCurrent)
        {
            // A previous instance is running — redirect activation so it can show
            // its main window, then exit this new process immediately.
            try
            {
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                RedirectActivationTo(activationArgs, instance);
            }
            catch
            {
                // Redirection failure is non-fatal; the user will still see only one
                // tray icon because this process exits right after.
            }

            return;
        }

        // This is the primary (or only) instance.
        // When a subsequent launch tries to redirect here, show the main window.
        instance.Activated += OnInstanceActivated;

        global::Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    /// <summary>
    /// Microsoft's documented single-instancing pattern: redirect on a worker thread and
    /// pump COM on this STA thread while waiting. Blocking the STA thread with
    /// GetAwaiter().GetResult() can deadlock if the redirect calls back into it.
    /// </summary>
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        var redirectEvent = CreateEvent(IntPtr.Zero, bManualReset: true, bInitialState: false, lpName: null);
        if (redirectEvent == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Task.Run(() =>
            {
                try
                {
                    keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
                }
                finally
                {
                    SetEvent(redirectEvent);
                }
            });

            const uint cwmoDefault = 0;
            const uint timeoutMs = 10_000; // never hang the second process forever
            _ = CoWaitForMultipleObjects(cwmoDefault, timeoutMs, 1, new[] { redirectEvent }, out _);
        }
        finally
        {
            CloseHandle(redirectEvent);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint lpdwIndex);

    private static void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        // Another launch was redirected to us — bring the main window into view.
        var app = global::Microsoft.UI.Xaml.Application.Current as App;
        app?.ShowMainWindow();
    }
}
