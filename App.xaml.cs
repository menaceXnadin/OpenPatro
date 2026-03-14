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

        public AppServices Services { get; private set; } = null!;
        public MainViewModel MainViewModel { get; private set; } = null!;
        public CalendarViewModel SharedCalendarViewModel => MainViewModel.Calendar;

        public App()
        {
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

                MainViewModel = new MainViewModel(Services);
                Log("MainViewModel created");

                if (await TryRunBundledSeedModeAsync())
                {
                    Exit();
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

            var openTrayCalendarCommand = new AsyncRelayCommand(ToggleTrayPopupWindowAsync);
            var openMainWindowCommand = new AsyncRelayCommand(async () =>
            {
                HideTrayPopupWindow();
                await ShowMainWindowAsync();
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
                LeftClickCommand = openTrayCalendarCommand,
                DoubleClickCommand = openMainWindowCommand,
                NoLeftClickDelay = true
            };

            SetEnumProperty(_trayIcon, "MenuActivation", "RightClick");
            SetEnumProperty(_trayIcon, "PopupActivation", "None");
            SetEnumProperty(_trayIcon, "ContextMenuMode", "SecondWindow");
            _trayIcon.ForceCreate();

            // Pre-bootstrap the popup window immediately so its HWND is alive from
            // the start. Without this the WinUI dispatcher shuts down when the main
            // window is hidden and H.NotifyIcon left-click commands stop firing.
            GetOrCreateTrayPopupWindow().Bootstrap();
        }

        private async Task ToggleTrayPopupWindowAsync()
        {
            if (_uiDispatcherQueue?.HasThreadAccess == true)
            {
                await ToggleTrayPopupWindowCoreAsync();
                return;
            }

            var dispatcher = _uiDispatcherQueue;
            if (dispatcher is null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            // IMPORTANT: TryEnqueue takes a DispatcherQueueHandler (void delegate).
            // Using async lambda here creates async void, which means:
            //   - Exceptions crash the process instead of propagating to the caller
            //   - The AsyncRelayCommand._isRunning flag gets stuck as true permanently
            // Use a synchronous lambda that fires-and-forgets the async work safely.
            if (!dispatcher.TryEnqueue(() =>
                {
                    _ = SafeToggleAndComplete(tcs);
                }))
            {
                tcs.TrySetException(new InvalidOperationException("Unable to enqueue tray popup activation."));
            }

            await tcs.Task;
        }

        private async Task SafeToggleAndComplete(TaskCompletionSource<object?> tcs)
        {
            try
            {
                await ToggleTrayPopupWindowCoreAsync();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private async Task ToggleTrayPopupWindowCoreAsync()
        {
            Log("TrayPopup: toggle requested");
            await MainViewModel.Calendar.EnsureLoadedAsync();

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

            _trayPopupWindow = new TrayPopupWindow(MainViewModel.Calendar);
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
            _mainWindow?.Hide();
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
