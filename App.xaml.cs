using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        private const string StartupConfiguredSettingKey = "StartupConfigured";

        private MainWindow? _mainWindow;
        private TrayPopupWindow? _trayPopupWindow;
        private TaskbarIcon? _trayIcon;
        private MenuFlyoutItem? _todayMenuItem;
        private ToggleMenuFlyoutItem? _startupMenuItem;
        private bool _allowWindowClose;
        private DateChangeWatcher? _dateChangeSubscription;
        private Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcherQueue;
        private CalendarViewModel? _trayCalendarViewModel;
        private DispatcherKeepaliveWindow? _keepaliveWindow;
        private volatile bool _trayToggleRunning;
        private DateTimeOffset _trayToggleStartedAt;
        private bool _calendarResetInProgress;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _trayRefreshTimer;
        private string? _trayIconKey;

        // Set when another launch asks to show the window before startup has finished
        // creating the view models; honoured once they exist.
        private bool _showWindowWhenReady;

        public AppServices Services { get; private set; } = null!;
        public MainViewModel MainViewModel { get; private set; } = null!;
        public DateChangeWatcher? DateChangeWatcher => _dateChangeSubscription;

        private bool IsReady => MainViewModel is not null;

        public App()
        {
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"AppDomain.UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log($"UnobservedTaskException: {e.Exception}");
                e.SetObserved();
            };
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // A tray app should keep running; the failure is recorded instead of crashing.
            Log($"UnhandledException: {e.Exception}");
            e.Handled = true;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            Trace("OnLaunched: start");
            _uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            try
            {
                Services = await AppServices.CreateAsync();
                Trace($"AppServices OK. CalendarDB={Services.Paths.CalendarDatabasePath}");

                await EnableStartupOnFirstRunAsync();

                MainViewModel = new MainViewModel(Services);

                if (await TryRunBundledSeedModeAsync())
                {
                    Exit();
                    return;
                }

                if (IsStartupLaunch() && !_showWindowWhenReady)
                {
                    Trace("Startup launch detected: initializing tray only");
                    _ = InitializeAfterWindowShownAsync();
                    return;
                }

                ShowMainWindow(resetCalendarToToday: false);

                // Load data and tray icon in the background so the window appears instantly.
                _ = InitializeAfterWindowShownAsync();
            }
            catch (Exception ex)
            {
                // Without services there is no window and no tray icon. Staying alive would
                // leave an invisible process that swallows every later launch, so tell the
                // user and exit (which also releases the single-instance key).
                Log($"OnLaunched FAILED: {ex}");
                ShowFatalStartupError(ex);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Turns "Start with Windows" on automatically the first time the app runs.
        /// After that the user's choice (Settings or tray menu) is never overridden.
        /// </summary>
        private async Task EnableStartupOnFirstRunAsync()
        {
            try
            {
                if (await Services.UserRepository.GetSettingAsync(StartupConfiguredSettingKey) is not null)
                {
                    return;
                }

                await Services.Startup.SetEnabledAsync(true);
                await Services.UserRepository.SetSettingAsync(StartupConfiguredSettingKey, "1");
                Trace("Startup: enabled on first run");
            }
            catch (Exception ex)
            {
                Log($"Startup first-run enable FAILED: {ex}");
            }
        }

        private static void ShowFatalStartupError(Exception ex)
        {
            const uint mbOk = 0x0, mbIconError = 0x10, mbSetForeground = 0x10000;
            var message =
                "OpenPatro could not start.\n\n" +
                $"{ex.Message}\n\n" +
                $"Details were written to:\n{DiagnosticLog.LogPath}";
            _ = MessageBoxW(IntPtr.Zero, message, "OpenPatro", mbOk | mbIconError | mbSetForeground);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public void ShowMainWindow(bool resetCalendarToToday = true)
        {
            if (_uiDispatcherQueue?.HasThreadAccess == true)
            {
                ShowMainWindowOrDefer(resetCalendarToToday);
                return;
            }

            _uiDispatcherQueue?.TryEnqueue(() => ShowMainWindowOrDefer(resetCalendarToToday));
        }

        private void ShowMainWindowOrDefer(bool resetCalendarToToday)
        {
            if (!IsReady)
            {
                // A second launch arrived while OnLaunched is still creating services.
                _showWindowWhenReady = true;
                return;
            }

            ShowMainWindowCore();
            if (resetCalendarToToday)
            {
                _ = ResetCalendarToTodayAsync();
            }
        }

        public async Task ShowMainWindowAsync(bool resetCalendarToToday = true)
        {
            if (_uiDispatcherQueue?.HasThreadAccess == true)
            {
                ShowMainWindowOrDefer(resetCalendarToToday);
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
                        ShowMainWindowOrDefer(resetCalendarToToday);
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
                Trace("Loading calendar data...");
                await MainViewModel.InitializeAsync();
                Trace("Calendar data loaded");
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
                Trace("Tray CalendarViewModel initialized");
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
                Trace("Keepalive window created");
            }
            catch (Exception ex)
            {
                Log($"Keepalive window FAILED: {ex}");
            }

            // Then set up the tray icon (this can be slow with H.NotifyIcon).
            try
            {
                Trace("Setting up tray icon...");
                InitializeTrayInfrastructure();
                Trace("Tray icon OK");
            }
            catch (Exception ex)
            {
                Log($"InitializeTray FAILED: {ex}");
            }

            // The timer method null-guards everything, so starting it here is safe
            // even if tray setup above failed.
            EnsureTrayAutoRefreshTimerStarted();

            try
            {
                _dateChangeSubscription = Services.Clock.WatchForDateChange(() =>
                {
                    var dispatcher = _uiDispatcherQueue;
                    if (dispatcher is null)
                    {
                        Log("DateChange: UI dispatcher unavailable");
                        return;
                    }

                    if (dispatcher.HasThreadAccess)
                    {
                        _ = HandleDateChangeAsync();
                        return;
                    }

                    if (!dispatcher.TryEnqueue(() => _ = HandleDateChangeAsync()))
                    {
                        Log("DateChange: failed to enqueue on UI dispatcher");
                    }
                });

                _ = Services.CalendarSync.RunSilentRefreshAsync();
                await RefreshTrayPresentationAsync();
            }
            catch (Exception ex)
            {
                Log($"PostLaunch FAILED: {ex}");
            }
        }

        private async Task HandleDateChangeAsync()
        {
            try
            {
                await MainViewModel.Calendar.InitializeAsync();
                if (_trayCalendarViewModel is not null)
                {
                    await _trayCalendarViewModel.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"DateChange: calendar refresh FAILED: {ex}");
            }

            try
            {
                await Services.CalendarSync.RunSilentRefreshAsync();
            }
            catch (Exception ex)
            {
                Log($"DateChange: sync FAILED: {ex}");
            }

            try
            {
                await RefreshTrayPresentationAsync();
            }
            catch (Exception ex)
            {
                Log($"DateChange: tray refresh FAILED: {ex}");
            }
        }

        public async Task OpenMainWindowForDateAsync(int year, int month, int day)
        {
            HideTrayPopupWindow();
            await ShowMainWindowAsync(resetCalendarToToday: false);
            await MainViewModel.SelectCalendarDateAsync(year, month, day);
        }

        private async Task ResetCalendarToTodayAsync()
        {
            if (_calendarResetInProgress)
            {
                return;
            }

            _calendarResetInProgress = true;
            try
            {
                MainViewModel.SelectedSection = ShellSection.Calendar;
                await MainViewModel.Calendar.InitializeAsync();
            }
            catch (Exception ex)
            {
                Log($"ResetCalendarToToday FAILED: {ex}");
            }
            finally
            {
                _calendarResetInProgress = false;
            }
        }

        public async Task RefreshTrayPresentationAsync()
        {
            if (_trayIcon is null)
            {
                return;
            }

            Models.CalendarDayRecord? today;
            try
            {
                today = await Services.CalendarRepository.GetTodayAsync();
            }
            catch (Exception ex)
            {
                Log($"RefreshTray: reading today FAILED: {ex}");
                return;
            }

            if (today is not null)
            {
                // Each part is updated independently: H.NotifyIcon can fail a tooltip
                // update (seen after Explorer restarts), which must not also skip the icon.
                try
                {
                    var iconKey = $"{today.BsDayText}|{today.IsHoliday}";
                    if (!string.Equals(iconKey, _trayIconKey, StringComparison.Ordinal) || _trayIcon.Icon is null)
                    {
                        var newIcon = TrayIconGlyphFactory.CreateIcon(today.BsDayText, today.IsHoliday);
                        var oldIcon = _trayIcon.Icon;
                        _trayIcon.Icon = newIcon;
                        // The factory hands us an owned handle — release the previous one.
                        oldIcon?.Dispose();
                        _trayIconKey = iconKey;
                    }
                }
                catch (Exception ex)
                {
                    Log($"RefreshTray: icon update FAILED: {ex}");
                }

                try
                {
                    _trayIcon.ToolTipText = today.BsFullDate;
                }
                catch (Exception ex)
                {
                    Log($"RefreshTray: tooltip update FAILED: {ex.Message}");
                }

                if (_todayMenuItem is not null)
                {
                    _todayMenuItem.Text = today.BsFullDate;
                }
            }

            if (_startupMenuItem is not null)
            {
                try
                {
                    _startupMenuItem.IsChecked = await Services.Startup.IsEnabledAsync();
                }
                catch (Exception ex)
                {
                    Log($"RefreshTray: startup state FAILED: {ex.Message}");
                }
            }
        }

        public void ExitApplication()
        {
            _allowWindowClose = true;
            _dateChangeSubscription?.Dispose();
            _trayRefreshTimer?.Stop();
            _trayRefreshTimer = null;

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

        private void EnsureTrayAutoRefreshTimerStarted()
        {
            var dispatcher = _uiDispatcherQueue;
            if (dispatcher is null || _trayRefreshTimer is not null)
            {
                return;
            }

            var timer = dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMinutes(1);
            timer.IsRepeating = true;
            timer.Tick += (_, _) => _ = RefreshTrayPresentationAsync();
            timer.Start();
            _trayRefreshTimer = timer;
        }

        private static bool IsStartupLaunch()
        {
            // Unpackaged: the HKCU Run value passes --startup.
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Packaged (MSIX): Windows activates the app through the startup task instead.
            try
            {
                return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                    == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
            }
            catch
            {
                return false;
            }
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

            var contextMenu = new MenuFlyout
            {
                // Tray menus must open upward: the icon sits on the taskbar, so the
                // default downward placement drops the menu behind the taskbar and
                // buries the bottom items (e.g. Exit).
                Placement = FlyoutPlacementMode.Top
            };
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

            // Left-click toggles the mini calendar popup.
            var togglePopupCommand = new RelayCommand(() =>
            {
                Trace("TrayPopup: left-click received");
                _ = SafeToggleTrayPopupAsync();
            });

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
                LeftClickCommand = togglePopupCommand,
                // Double-click behavior is intentionally disabled; users can open
                // the main window from the right-click menu.
                NoLeftClickDelay = true
            };

            SetEnumProperty(_trayIcon, "MenuActivation", "RightClick");
            SetEnumProperty(_trayIcon, "PopupActivation", "None");
            SetEnumProperty(_trayIcon, "ContextMenuMode", "SecondWindow");
            _trayIcon.ForceCreate(false);

            // Pre-create and park the popup now so the first left-click shows it
            // instantly instead of building the HWND + restyling on demand
            // (that on-demand path caused first-open flicker/position jumps).
            try
            {
                GetOrCreateTrayPopupWindow().Bootstrap();
            }
            catch (Exception ex)
            {
                Log($"TrayPopup bootstrap FAILED: {ex}");
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
                    Trace("TrayPopup: toggle already running, ignoring click");
                    return;
                }

                Trace("TrayPopup: previous toggle timed out, forcing reset");
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
            Trace("TrayPopup: toggle requested");

            // If the existing popup window's HWND has been destroyed, recreate it.
            if (_trayPopupWindow is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_trayPopupWindow);
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                {
                    Trace("TrayPopup: existing HWND is invalid, recreating");
                    _trayPopupWindow.Closed -= TrayPopupWindow_Closed;
                    _trayPopupWindow = null;
                }
            }

            var popupWindow = GetOrCreateTrayPopupWindow();
            if (popupWindow.IsPopupVisible)
            {
                Trace("TrayPopup: hiding");
                popupWindow.HidePopup();
                return;
            }

            if (popupWindow.WasRecentlyDismissedByOutsideClick())
            {
                // The dismiss hook already hid the popup for this very click
                // (the tray icon is outside the popup). Showing now would just
                // re-open what the user closed — treat the toggle as handled.
                Trace("TrayPopup: show suppressed, same click just dismissed it");
                return;
            }

            try
            {
                Trace("TrayPopup: showing");
                // Show FIRST, synchronously inside the click dispatch, so the window
                // can take foreground while the tray-click input context is still
                // valid. Awaiting data load before showing expired that context, so
                // Windows refused foreground and the popup instantly auto-hid.
                // Data loads right after and fills in live via bindings.
                popupWindow.ShowPopup();
                Trace("TrayPopup: shown OK");
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
                Trace("TrayPopup: shown OK after recreate");
            }

            if (_trayCalendarViewModel is not null)
            {
                try
                {
                    await _trayCalendarViewModel.EnsureLoadedAsync();
                }
                catch (Exception ex)
                {
                    Log($"TrayPopup data load FAILED: {ex}");
                }
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

            if (_mainWindow is not null)
            {
                // Preferred tray behavior: hide the window completely.
                // Keepalive window keeps the dispatcher active in this mode.
                if (_keepaliveWindow is not null)
                {
                    _mainWindow.Hide();
                    Trace("MainWindow: hidden to tray");
                    return;
                }

                // Fallback for very early-close race before keepalive is ready.
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
                if (hwnd != IntPtr.Zero)
                {
                    var exStyle = GetWindowLongPtrApp(hwnd, GwlExStyleApp).ToInt64();
                    SetWindowLongPtrApp(hwnd, GwlExStyleApp, new IntPtr(exStyle | WsExToolWindowApp));
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoZOrder | SwpFrameChanged);
                    ShowWindow(hwnd, SwMinimize);
                    Trace("MainWindow: minimized to tray (keepalive not ready)");
                }
            }
        }

        private async void OpenCalendarMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HideTrayPopupWindow();
                await ShowMainWindowAsync();
            }
            catch (Exception ex)
            {
                Log($"OpenCalendarMenuItem_Click FAILED: {ex}");
            }
        }

        private async void StartupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleMenuFlyoutItem menuItem)
            {
                return;
            }

            try
            {
                menuItem.IsChecked = await Services.Startup.SetEnabledAsync(menuItem.IsChecked);
                await MainViewModel.Settings.RefreshStartupStateAsync();
            }
            catch (Exception ex)
            {
                Log($"StartupMenuItem_Click FAILED: {ex}");
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        /// <summary>Errors and unexpected failures; written in every build.</summary>
        internal static void Log(string message) => DiagnosticLog.Error(message);

        /// <summary>Verbose tracing; compiled out of Release builds.</summary>
        [Conditional("DEBUG")]
        internal static void Trace(string message) => DiagnosticLog.Trace(message);
    }
}
