using System;
using System.IO;
using System.Reflection;
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
                _trayIcon.Icon = TrayIconGlyphFactory.CreateIcon(today.BsDayText);

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
            _todayMenuItem = new MenuFlyoutItem
            {
                Text = "आज",
                IsEnabled = false
            };

            _startupMenuItem = new ToggleMenuFlyoutItem
            {
                Text = "Start with Windows"
            };
            _startupMenuItem.Click += StartupMenuItem_Click;

            var contextMenu = new MenuFlyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft
            };
            var openItem = new MenuFlyoutItem { Text = "Open Calendar" };
            openItem.Click += OpenCalendarMenuItem_Click;
            var exitItem = new MenuFlyoutItem { Text = "Exit" };
            exitItem.Click += ExitMenuItem_Click;
            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new MenuFlyoutSeparator());
            contextMenu.Items.Add(_todayMenuItem);
            contextMenu.Items.Add(_startupMenuItem);
            contextMenu.Items.Add(new MenuFlyoutSeparator());
            contextMenu.Items.Add(exitItem);

            var popupContent = new TrayCalendarPopup();
            popupContent.Attach(MainViewModel.Calendar);

            var popupRoot = TryGetWindowXamlRoot();
            UIElement trayPopup = popupContent;
            if (popupRoot is not null)
            {
                trayPopup = new Popup
                {
                    Child = popupContent,
                    XamlRoot = popupRoot,
                    ShouldConstrainToRootBounds = false
                };
            }

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "OpenPatro",
                ContextFlyout = contextMenu,
                TrayPopup = trayPopup,
                LeftClickCommand = MainViewModel.Calendar.OpenMainWindowCommand,
                DoubleClickCommand = null,
                NoLeftClickDelay = true
            };

            SetEnumProperty(_trayIcon, "MenuActivation", "RightClick");
            // When XamlRoot is unavailable, disable tray popup activation to avoid runtime COM crash.
            SetEnumProperty(_trayIcon, "PopupActivation", popupRoot is null ? "None" : "DoubleClick");
            SetEnumProperty(_trayIcon, "ContextMenuMode", "SecondWindow");
            _trayIcon.ForceCreate();
        }

        private XamlRoot? TryGetWindowXamlRoot()
        {
            if (_mainWindow?.Content is FrameworkElement root)
            {
                return root.XamlRoot;
            }

            return null;
        }

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
