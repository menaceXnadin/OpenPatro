using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Globalization;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenPatro.Controls;
using OpenPatro.Infrastructure;
using OpenPatro.Services;
using OpenPatro.ViewModels;

namespace OpenPatro
{
    public partial class App : Application
    {
        private static readonly string DiagLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "openpatro-diag.log");

        private MainWindow? _mainWindow;
        private TrayPopupWindow? _trayPopupWindow;
        private TaskbarIcon? _trayIcon;
        private MenuFlyoutItem? _todayMenuItem;
        private ToggleMenuFlyoutItem? _startupMenuItem;
        private bool _allowWindowClose;
        private IDisposable? _midnightSubscription;
        private Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcherQueue;
        private CalendarViewModel? _trayCalendarViewModel;
        private DispatcherKeepaliveWindow? _keepaliveWindow;
        private volatile bool _trayToggleRunning;
        private DateTimeOffset _trayToggleStartedAt;
        private DateTimeOffset _lastTrayDateNotificationAt;

        private static readonly TimeSpan TrayDateNotificationCooldown = TimeSpan.FromSeconds(2);

        public AppServices Services { get; private set; } = null!;
        public MainViewModel MainViewModel { get; private set; } = null!;

        public App()
        {
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Log($"UnhandledException: {e.Exception}");
            e.Handled = true;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            Log("OnLaunched: start");
            _uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            try
            {
                Log("Creating AppServices...");
                Services = await AppServices.CreateAsync();
                Log($"AppServices OK. CalendarDB={Services.Paths.CalendarDatabasePath}");

                try
                {
                    if (!Services.Startup.IsEnabled())
                    {
                        Services.Startup.SetEnabled(true);
                        Log("Startup: enabled automatically");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Startup auto-enable FAILED: {ex}");
                }

                MainViewModel = new MainViewModel(Services);
                Log("MainViewModel created");

                if (await TryRunBundledSeedModeAsync())
                {
                    Exit();
                    return;
                }

                var isStartupLaunch = IsStartupLaunch();
                if (isStartupLaunch)
                {
                    Log("Startup launch detected: initializing tray only");
                    _ = InitializeAfterWindowShownAsync();
                    return;
                }

                Log("Showing MainWindow...");
                ShowMainWindow();
                Log("MainWindow shown");

                // Load data and tray icon in the background so the window appears instantly.
                _ = InitializeAfterWindowShownAsync();
            }
            catch (Exception ex)
            {
                Log($"OnLaunched FAILED: {ex}");
            }
        }

        public void ShowMainWindow()
        {
            if (_uiDispatcherQueue?.HasThreadAccess == true)
            {
                ShowMainWindowCore();
                return;
            }

            _uiDispatcherQueue?.TryEnqueue(ShowMainWindowCore);
        }

        public async Task ShowMainWindowAsync()
        {
            if (_uiDispatcherQueue?.HasThreadAccess == true)
            {
                ShowMainWindowCore();
                return;
            }

            var dispatcher = _uiDispatcherQueue;
            if (dispatcher is null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        ShowMainWindowCore();
                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }))
            {
                tcs.TrySetException(new InvalidOperationException("Unable to enqueue main window activation."));
            }

            await tcs.Task;
        }

        private void ShowMainWindowCore()
        {
            if (_mainWindow is null)
            {
                try
                {
                    _mainWindow = new MainWindow();
                }
                catch (Exception ex)
                {
                    Log($"MainWindow constructor FAILED: {ex}");
                    throw;
                }

                _mainWindow.Closed += MainWindow_Closed;
            }

            // The main window may be minimized with WS_EX_TOOLWINDOW to keep the
            // WinUI dispatcher alive. Restore it to a normal window state.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
            if (hwnd != IntPtr.Zero)
            {
                // Remove WS_EX_TOOLWINDOW so the window appears in taskbar again.
                var exStyle = GetWindowLongPtrApp(hwnd, GwlExStyleApp).ToInt64();
                if ((exStyle & WsExToolWindowApp) != 0)
                {
                    SetWindowLongPtrApp(hwnd, GwlExStyleApp, new IntPtr(exStyle & ~WsExToolWindowApp));
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
                }

                // Ensure the window is restored and foregrounded when activated from tray.
                if (IsIconic(hwnd))
                {
                    ShowWindow(hwnd, SwRestore);
                }

                ShowWindow(hwnd, SwShow);
                SetForegroundWindow(hwnd);
            }

            _mainWindow.Show();
            _mainWindow.BringToFront();
        }

        private async Task InitializeAfterWindowShownAsync()
        {
            try
            {
                Log("Loading calendar data...");
                await MainViewModel.InitializeAsync();
                Log("Calendar data loaded");
            }
            catch (Exception ex)
            {
                Log($"InitializeCalendar FAILED: {ex}");
            }

            // Create a separate CalendarViewModel for the tray popup so it has
            // independent state (month navigation, day selection) from the main window.
            try
            {
                _trayCalendarViewModel = new CalendarViewModel(Services);
                await _trayCalendarViewModel.InitializeAsync();
                Log("Tray CalendarViewModel initialized");
            }
            catch (Exception ex)
            {
                Log($"Tray CalendarViewModel FAILED: {ex}");
            }

            // Create a keepalive window that stays alive at all times to prevent the
            // WinUI dispatcher from shutting down when all user-visible windows are
            // hidden. This is the reliable replacement for the off-screen parking trick.
            try
            {
                _keepaliveWindow = new DispatcherKeepaliveWindow();
                _keepaliveWindow.Create();
                Log("Keepalive window created");
            }
            catch (Exception ex)
            {
                Log($"Keepalive window FAILED: {ex}");
            }

            // Then set up the tray icon (this can be slow with H.NotifyIcon).
            try
            {
                Log("Setting up tray icon...");
                InitializeTrayInfrastructure();
                Log("Tray icon OK");
            }
            catch (Exception ex)
            {
                Log($"InitializeTray FAILED: {ex}");
            }

            try
            {
                _midnightSubscription = Services.Clock.ScheduleDailyMidnightCallback(() =>
                {
                    _ = MainViewModel.Calendar.InitializeAsync();
                    _ = _trayCalendarViewModel?.InitializeAsync();
                    _ = Services.CalendarSync.RunSilentRefreshAsync();
                    _ = RefreshTrayPresentationAsync();
                });

                _ = Services.CalendarSync.RunSilentRefreshAsync();
                await RefreshTrayPresentationAsync();
            }
            catch (Exception ex)
            {
                Log($"PostLaunch FAILED: {ex}");
            }
        }

        public async Task OpenMainWindowForDateAsync(int year, int month, int day)
        {
            HideTrayPopupWindow();
            await ShowMainWindowAsync();
            await MainViewModel.SelectCalendarDateAsync(year, month, day);
        }

        public async Task RefreshTrayPresentationAsync()
        {
            if (_trayIcon is null)
            {
                return;
            }

            try
            {
                var today = await Services.CalendarRepository.GetTodayAsync();
                if (today is null)
                {
                    return;
                }

                _trayIcon.ToolTipText = today.BsFullDate;
                _trayIcon.Icon = TrayIconGlyphFactory.CreateIcon(today.BsDayText, today.IsHoliday);

                if (_todayMenuItem is not null)
                {
                    _todayMenuItem.Text = today.BsFullDate;
                }

                if (_startupMenuItem is not null)
                {
                    _startupMenuItem.IsChecked = Services.Startup.IsEnabled();
                }
            }
            catch (Exception ex)
            {
                Log($"RefreshTray FAILED: {ex}");
            }
        }

        public void ExitApplication()
        {
            _allowWindowClose = true;
            _midnightSubscription?.Dispose();

            if (_trayPopupWindow is not null)
            {
                _trayPopupWindow.Closed -= TrayPopupWindow_Closed;
                _trayPopupWindow.Close();
                _trayPopupWindow = null;
            }

            _trayIcon?.Dispose();
            _keepaliveWindow?.Close();
            _mainWindow?.Close();
            Exit();
            Environment.Exit(0);
        }

        private async Task<bool> TryRunBundledSeedModeAsync()
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            foreach (var arg in cmdArgs)
            {
                if (!string.Equals(arg, "--seed-bundled-db", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await Services.BundledSeed.RunAsync();
                return true;
            }

            return false;
        }

        private static bool IsStartupLaunch()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void InitializeTrayInfrastructure()
        {
            const double trayMenuMinWidth = 220;

            _todayMenuItem = new MenuFlyoutItem
            {
                Text = "आज",
                IsEnabled = false,
                MinWidth = trayMenuMinWidth
            };

            _startupMenuItem = new ToggleMenuFlyoutItem
            {
                Text = "Start with Windows",
                MinWidth = trayMenuMinWidth
            };
            _startupMenuItem.Click += StartupMenuItem_Click;

            var contextMenu = new MenuFlyout();
            var openItem = new MenuFlyoutItem
            {
                Text = "Open Calendar",
                MinWidth = trayMenuMinWidth
            };
            openItem.Click += OpenCalendarMenuItem_Click;
            var exitItem = new MenuFlyoutItem
            {
                Text = "Exit",
                MinWidth = trayMenuMinWidth
            };
            exitItem.Click += ExitMenuItem_Click;
            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new MenuFlyoutSeparator());
            contextMenu.Items.Add(_todayMenuItem);
            contextMenu.Items.Add(_startupMenuItem);
            contextMenu.Items.Add(new MenuFlyoutSeparator());
            contextMenu.Items.Add(exitItem);

            var showTodayCommand = new RelayCommand(() => _ = ShowTodayFromTrayAsync());

            var faviconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
            var initialIcon = File.Exists(faviconPath)
                ? new System.Drawing.Icon(faviconPath)
                : null;

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "OpenPatro",
                Icon = initialIcon,
                ContextFlyout = contextMenu,
                TrayPopup = null,
                LeftClickCommand = showTodayCommand,
                // Double-click behavior is intentionally disabled; users can open
                // the main window from the right-click menu.
                NoLeftClickDelay = true
            };

            SetEnumProperty(_trayIcon, "MenuActivation", "RightClick");
            SetEnumProperty(_trayIcon, "PopupActivation", "None");
            SetEnumProperty(_trayIcon, "ContextMenuMode", "SecondWindow");
            _trayIcon.ForceCreate(false);
        }

        private async Task ShowTodayFromTrayAsync()
        {
            var dispatcher = _uiDispatcherQueue;
            if (dispatcher is not null && !dispatcher.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dispatcher.TryEnqueue(async () =>
                    {
                        try
                        {
                            await ShowTodayFromTrayAsync();
                            tcs.TrySetResult(null);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }))
                {
                    Log("TrayDate: failed to enqueue notification on UI dispatcher");
                    return;
                }

                await tcs.Task;
                return;
            }

            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _lastTrayDateNotificationAt < TrayDateNotificationCooldown)
                {
                    return;
                }

                HideTrayPopupWindow();

                var today = await Services.CalendarRepository.GetTodayAsync();
                var nepaliDate = today?.BsFullDate;
                var englishDate = today?.AdDateText;
                var message =
                    $"Nepali (BS): {nepaliDate ?? "Unavailable"}\r\n" +
                    $"English (AD): {englishDate ?? "Unavailable"}";

                TryShowTrayNotification("OpenPatro - Today", message);
                _lastTrayDateNotificationAt = now;
            }
            catch (Exception ex)
            {
                Log($"TrayDate FAILED: {ex}");
            }
        }

        private void TryShowTrayNotification(string title, string message)
        {
            if (_trayIcon is null)
            {
                return;
            }

            try
            {
                var method = _trayIcon.GetType().GetMethod("ShowNotification", BindingFlags.Instance | BindingFlags.Public);
                if (method is null)
                {
                    return;
                }

                var parameters = method.GetParameters();
                if (parameters.Length < 2)
                {
                    return;
                }

                var args = new object?[parameters.Length];
                args[0] = title;
                args[1] = message;

                for (var index = 2; index < parameters.Length; index++)
                {
                    args[index] = parameters[index].HasDefaultValue ? parameters[index].DefaultValue : null;
                }

                _ = method.Invoke(_trayIcon, args);
            }
            catch (Exception ex)
            {
                Log($"TrayDate notification FAILED: {ex}");
            }
        }

        /// <summary>
        /// Fire-and-forget wrapper for ToggleTrayPopupWindowCoreAsync with its own
        /// re-entrancy guard that auto-resets after a timeout so it can never get
        /// permanently stuck.
        /// </summary>
        private async Task SafeToggleTrayPopupAsync()
        {
            var dispatcher = _uiDispatcherQueue;
            if (dispatcher is not null && !dispatcher.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dispatcher.TryEnqueue(async () =>
                    {
                        try
                        {
                            await SafeToggleTrayPopupAsync();
                            tcs.TrySetResult(null);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }))
                {
                    Log("TrayPopup: failed to enqueue toggle on UI dispatcher");
                    return;
                }

                await tcs.Task;
                return;
            }

            // Re-entrancy guard with a 5-second timeout failsafe.
            // If a previous toggle somehow never completed (e.g. dispatcher stalled),
            // the timeout ensures we don't stay stuck forever.
            if (_trayToggleRunning)
            {
                if (DateTimeOffset.UtcNow - _trayToggleStartedAt < TimeSpan.FromSeconds(5))
                {
                    Log("TrayPopup: toggle already running, ignoring click");
                    return;
                }

                Log("TrayPopup: previous toggle timed out, forcing reset");
            }

            _trayToggleRunning = true;
            _trayToggleStartedAt = DateTimeOffset.UtcNow;
            try
            {
                await ToggleTrayPopupWindowCoreAsync();
            }
            catch (Exception ex)
            {
                Log($"SafeToggleTrayPopup FAILED: {ex}");
            }
            finally
            {
                _trayToggleRunning = false;
            }
        }

        private async Task ToggleTrayPopupWindowCoreAsync()
        {
            Log("TrayPopup: toggle requested");
            if (_trayCalendarViewModel is not null)
            {
                await _trayCalendarViewModel.EnsureLoadedAsync();
            }

            // If the existing popup window's HWND has been destroyed, recreate it.
            if (_trayPopupWindow is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_trayPopupWindow);
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                {
                    Log("TrayPopup: existing HWND is invalid, recreating");
                    _trayPopupWindow.Closed -= TrayPopupWindow_Closed;
                    _trayPopupWindow = null;
                }
            }

            var popupWindow = GetOrCreateTrayPopupWindow();
            if (popupWindow.IsPopupVisible)
            {
                Log("TrayPopup: hiding");
                popupWindow.HidePopup();
                return;
            }

            try
            {
                Log("TrayPopup: showing");
                popupWindow.ShowPopup();
                Log("TrayPopup: shown OK");
            }
            catch (Exception ex)
            {
                Log($"TrayPopup.Show FAILED. Recreating popup window. {ex}");

                if (_trayPopupWindow is not null)
                {
                    _trayPopupWindow.Closed -= TrayPopupWindow_Closed;
                    _trayPopupWindow = null;
                }

                popupWindow = GetOrCreateTrayPopupWindow();
                popupWindow.ShowPopup();
                Log("TrayPopup: shown OK after recreate");
            }
        }

        private TrayPopupWindow GetOrCreateTrayPopupWindow()
        {
            if (_trayPopupWindow is not null)
            {
                return _trayPopupWindow;
            }

            // Use the dedicated tray ViewModel so the popup has independent state.
            // Fall back to the main Calendar ViewModel if the tray one isn't ready yet.
            var trayVm = _trayCalendarViewModel ?? MainViewModel.Calendar;
            _trayPopupWindow = new TrayPopupWindow(trayVm);
            _trayPopupWindow.Closed += TrayPopupWindow_Closed;
            return _trayPopupWindow;
        }

        private void TrayPopupWindow_Closed(object sender, WindowEventArgs args)
        {
            if (ReferenceEquals(sender, _trayPopupWindow))
            {
                _trayPopupWindow = null;
            }
        }

        private void HideTrayPopupWindow()
        {
            _trayPopupWindow?.HidePopup();
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtrApp(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtrApp(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const int SwRestore = 9;
        private const int SwMinimize = 6;
        private const int SwShow = 5;
        private const int GwlExStyleApp = -20;
        private const int WsExToolWindowApp = 0x00000080;

        private static void SetEnumProperty(object target, string propertyName, string enumValue)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType.IsEnum != true || !property.CanWrite)
            {
                return;
            }

            try
            {
                var value = Enum.Parse(property.PropertyType, enumValue, ignoreCase: true);
                property.SetValue(target, value);
            }
            catch
            {
                // Ignore invalid enum values on different library versions.
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_allowWindowClose)
            {
                return;
            }

            args.Handled = true;

            // IMPORTANT: Do NOT call _mainWindow.Hide() here.
            // Window.Hide() tells the WinUI runtime this window is gone. Once all
            // WinUI-tracked windows are hidden, the runtime shuts down the dispatcher
            // message loop, which kills H.NotifyIcon's ability to deliver click events.
            //
            // Instead, MINIMIZE the window and add WS_EX_TOOLWINDOW so it disappears
            // from the taskbar and Alt+Tab. A minimized window is still considered
            // "visible" by Windows, which keeps the WinUI dispatcher alive.
            if (_mainWindow is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
                if (hwnd != IntPtr.Zero)
                {
                    // Add WS_EX_TOOLWINDOW to hide from taskbar / Alt+Tab.
                    var exStyle = GetWindowLongPtrApp(hwnd, GwlExStyleApp).ToInt64();
                    SetWindowLongPtrApp(hwnd, GwlExStyleApp, new IntPtr(exStyle | WsExToolWindowApp));
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoZOrder | SwpFrameChanged);

                    // Minimize — keeps the window "visible" in Win32/WinUI terms.
                    ShowWindow(hwnd, SwMinimize);
                    Log("MainWindow: minimized + hidden from taskbar");
                }
            }
        }

        private async void OpenCalendarMenuItem_Click(object sender, RoutedEventArgs e)
        {
            HideTrayPopupWindow();
            await ShowMainWindowAsync();
        }

        private void StartupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleMenuFlyoutItem menuItem)
            {
                return;
            }

            Services.Startup.SetEnabled(menuItem.IsChecked);
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private static void Log(string message)
        {
            try
            {
                var entry = $"[{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}] {message}{Environment.NewLine}";
                File.AppendAllText(DiagLogPath, entry);
            }
            catch
            {
                // Cannot log – silently ignore.
            }
        }
    }
}
