using Microsoft.Windows.AppLifecycle;
using System;

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
                instance.RedirectActivationToAsync(activationArgs).GetAwaiter().GetResult();
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

    private static void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        // Another launch was redirected to us — bring the main window into view.
        var app = global::Microsoft.UI.Xaml.Application.Current as App;
        app?.ShowMainWindow();
    }
}
