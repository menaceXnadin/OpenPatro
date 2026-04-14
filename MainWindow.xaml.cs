using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenPatro.Services;
using OpenPatro.ViewModels;
using Windows.System;

namespace OpenPatro
{
    public sealed partial class MainWindow : Window
    {
        // ── Win32 / DWM interop constants ──
        private const int GwlpWndProc = -4;
        private const uint WmGetMinMaxInfo = 0x0024;
        private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmaUseImmersiveDarkMode = 20;

        // ── Interop delegates & state ──
        private IntPtr _previousWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate;

        // ── UI state flags ──
        private bool _showDetailPanelForNextSelectionChange;
        private bool _initialWindowSizeApplied;
        private OverlappedPresenterState _lastPresenterState = OverlappedPresenterState.Restored;
        private bool _suppressSaitSelectionEvents;
        private bool _suppressDateConverterInputSync;
        private bool _suppressNavCheckedEvents;
        private bool _suppressCalendarNavigationSelectionEvents;

        // ── Cached UI element references ──
        // Resolved once during RootGrid_Loaded instead of calling FindName() repeatedly.
        private GridView? _calendarDaysView;
        private Grid? _calendarLayoutHost;
        private Grid? _weekdayHeaderGrid;
        private Border? _weekdayHeaderBorder;
        private TextBox? _dateConverterYearBox;
        private ComboBox? _dateConverterMonthPicker;
        private TextBox? _dateConverterDayBox;
        private Button? _dateConverterConvertButton;

        // ── Services ──
        private ResizePipeline? _resizePipeline;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point ptReserved;
            public Point ptMaxSize;
            public Point ptMaxPosition;
            public Point ptMinTrackSize;
            public Point ptMaxTrackSize;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        public MainWindow()
        {
            ViewModel = ((App)Application.Current).MainViewModel;
            InitializeComponent();
            ViewModel.Calendar.PropertyChanged += Calendar_PropertyChanged;

            ExtendsContentIntoTitleBar = false;

            // Debounced resize pipeline — all size-change events funnel through here.
            _resizePipeline = new ResizePipeline(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
                OnDebouncedResize);

            SizeChanged += MainWindow_SizeChanged;

            ApplyWindowIcon();
            ApplyDarkTitleBar();
            ConfigureMinimumWindowSize();

            UpdateSectionVisibility();
        }

        public MainViewModel ViewModel { get; }

        public void BringToFront()
        {
            Activate();
        }

        // ── Lifecycle ──

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded;

            // Cache UI element references once — eliminates repeated FindName() calls.
            CacheUiReferences();
            ApplySidebarLogo();

            ConfigureWindowResizeBehavior();

            // Attempt to restore saved window bounds; fall back to computed initial size.
            var restored = await TryRestoreWindowBoundsAsync();
            if (!restored)
            {
                ApplyInitialWindowSize();
                CenterWindowOnCurrentDisplay();
            }

            CalendarButton.Checked += CalendarButton_Checked;
            CalendarButton.Unchecked += NavButton_Unchecked;
            StockMarketButton.Checked += StockMarketButton_Checked;
            StockMarketButton.Unchecked += NavButton_Unchecked;
            SettingsButton.Checked += SettingsButton_Checked;
            SettingsButton.Unchecked += NavButton_Unchecked;
            RashifalButton.Checked += RashifalButton_Checked;
            RashifalButton.Unchecked += NavButton_Unchecked;
            ShubhaSaitButton.Checked += ShubhaSaitButton_Checked;
            ShubhaSaitButton.Unchecked += NavButton_Unchecked;
            DateConverterButton.Checked += DateConverterButton_Checked;
            DateConverterButton.Unchecked += NavButton_Unchecked;

            CalendarButton.IsChecked = ViewModel.SelectedSection == ShellSection.Calendar;
            StockMarketButton.IsChecked = ViewModel.SelectedSection == ShellSection.StockMarket;
            SettingsButton.IsChecked = ViewModel.SelectedSection == ShellSection.Settings;
            RashifalButton.IsChecked = ViewModel.SelectedSection == ShellSection.Rashifal;
            ShubhaSaitButton.IsChecked = ViewModel.SelectedSection == ShellSection.ShubhaSait;
            DateConverterButton.IsChecked = ViewModel.SelectedSection == ShellSection.DateConverter;
            UpdateSectionVisibility();
            UpdateCalendarLayout();
            SyncCalendarNavigationSelections();
            UpdateDateConverterMonthLabels();
            SyncDateConverterInputParts();
            UpdateSortIcons();

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                _lastPresenterState = presenter.State;
            }

            AppWindow.Changed -= AppWindow_Changed;
            AppWindow.Changed += AppWindow_Changed;
        }

        private void SidebarLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ApplySidebarLogo();
        }

        private void ApplySidebarLogo()
        {
            if (SidebarLogo is null)
            {
                return;
            }

            // Resolve the logo from the filesystem using the executable's base directory.
            // The .csproj CopyToOutputDirectory ensures the asset is present in publish output.
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.scale-200.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Square44x44Logo.scale-200.png"),
                Path.Combine(Environment.CurrentDirectory, "Assets", "Square44x44Logo.scale-200.png")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    SidebarLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
                    return;
                }
                catch
                {
                    // Try the next candidate path.
                }
            }
        }

        /// <summary>
        /// Resolves named elements once and caches them, avoiding repeated FindName() calls.
        /// </summary>
        private void CacheUiReferences()
        {
            _calendarDaysView = RootGrid?.FindName("CalendarDaysView") as GridView;
            _calendarLayoutHost = RootGrid?.FindName("CalendarLayoutHost") as Grid;
            _weekdayHeaderGrid = RootGrid?.FindName("WeekdayHeaderGrid") as Grid;
            _weekdayHeaderBorder = _weekdayHeaderGrid?.Parent as Border;
            _dateConverterYearBox = RootGrid?.FindName("DateConverterYearBox") as TextBox;
            _dateConverterMonthPicker = RootGrid?.FindName("DateConverterMonthPicker") as ComboBox;
            _dateConverterDayBox = RootGrid?.FindName("DateConverterDayBox") as TextBox;
            _dateConverterConvertButton = RootGrid?.FindName("DateConverterConvertButton") as Button;
        }

        // ── Resize pipeline ──

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            // Funnel all resize events through the debounced pipeline.
            _resizePipeline?.RequestLayout();
        }

        /// <summary>
        /// Single authoritative callback that runs after resize events have settled.
        /// </summary>
        private void OnDebouncedResize()
        {
            UpdateCalendarLayout();
            _ = PersistWindowBoundsAsync();
        }

        // ── Navigation ──

        private void CalendarButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            ViewModel.SelectedSection = ShellSection.Calendar;
            UpdateSectionVisibility();
        }

        private void SettingsButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            ViewModel.SelectedSection = ShellSection.Settings;
            UpdateSectionVisibility();
        }

        private async void StockMarketButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            ViewModel.SelectedSection = ShellSection.StockMarket;
            UpdateSectionVisibility();
            await ViewModel.StockMarket.InitializeAsync();
            UpdateSortIcons();
            
            // Initialize the first tab as selected
            UpdateTopStocksTabStates("TopGainers");
        }

        private async void RashifalButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            ViewModel.SelectedSection = ShellSection.Rashifal;
            UpdateSectionVisibility();
            await ViewModel.Rashifal.InitializeAsync();
        }

        private async void ShubhaSaitButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            ViewModel.SelectedSection = ShellSection.ShubhaSait;
            UpdateSectionVisibility();
            _suppressSaitSelectionEvents = true;
            await ViewModel.ShubhaSait.InitializeAsync();
            SyncShubhaSaitSelections();
            _suppressSaitSelectionEvents = false;
        }

        private void DateConverterButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            ViewModel.SelectedSection = ShellSection.DateConverter;
            UpdateSectionVisibility();
            SyncDateConverterInputParts();
        }

        // ── Top Stocks Tab Handlers ──

        private void TopGainersTab_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StockMarket.SelectedTopStocksTab = "TopGainers";
            UpdateTopStocksTabStates("TopGainers");
        }

        private void TopLosersTab_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StockMarket.SelectedTopStocksTab = "TopLosers";
            UpdateTopStocksTabStates("TopLosers");
        }

        private void TopTurnoverTab_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StockMarket.SelectedTopStocksTab = "TopTurnover";
            UpdateTopStocksTabStates("TopTurnover");
        }

        private void TopVolumeTab_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StockMarket.SelectedTopStocksTab = "TopVolume";
            UpdateTopStocksTabStates("TopVolume");
        }

        private void TopTransactionsTab_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StockMarket.SelectedTopStocksTab = "TopTransactions";
            UpdateTopStocksTabStates("TopTransactions");
        }

        private void SectorHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string column)
            {
                ViewModel.StockMarket.SortSectorBy(column);
                UpdateSectorSortIcons();
            }
        }

        private void TopStocksHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string column)
            {
                ViewModel.StockMarket.SortTopStocksBy(column);
                UpdateTopStocksSortIcons();
            }
        }

        private void LiveMarketHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string column)
            {
                ViewModel.StockMarket.SortLiveCompaniesBy(column);
                UpdateLiveSortIcons();
            }
        }

        private void UpdateSortIcons()
        {
            UpdateSectorSortIcons();
            UpdateTopStocksSortIcons();
            UpdateLiveSortIcons();
        }

        private void UpdateSectorSortIcons()
        {
            var sortName = FindSortIcon("SectorSortNameIcon");
            var sortValue = FindSortIcon("SectorSortValueIcon");
            var sortPercent = FindSortIcon("SectorSortPercentIcon");

            ResetSortIcons(sortName, sortValue, sortPercent);

            var active = ViewModel.StockMarket.SectorSortColumn switch
            {
                "Value" => sortValue,
                "ChangePercent" => sortPercent,
                _ => sortName
            };

            ApplyActiveSortIcon(active, ViewModel.StockMarket.IsSectorSortAscending);
        }

        private void UpdateTopStocksSortIcons()
        {
            var sortSymbol = FindSortIcon("TopSortSymbolIcon");
            var sortLtp = FindSortIcon("TopSortLtpIcon");
            var sortChange = FindSortIcon("TopSortChangeIcon");
            var sortMetric = FindSortIcon("TopSortMetricIcon");

            ResetSortIcons(sortSymbol, sortLtp, sortChange, sortMetric);

            var active = ViewModel.StockMarket.TopStocksSortColumn switch
            {
                "Ltp" => sortLtp,
                "Change" => sortChange,
                "Metric" => sortMetric,
                _ => sortSymbol
            };

            ApplyActiveSortIcon(active, ViewModel.StockMarket.IsTopStocksSortAscending);
        }

        private void UpdateLiveSortIcons()
        {
            var sortLogo = FindSortIcon("LiveSortLogoIcon");
            var sortSymbol = FindSortIcon("LiveSortSymbolIcon");
            var sortName = FindSortIcon("LiveSortNameIcon");
            var sortSector = FindSortIcon("LiveSortSectorIcon");
            var sortOpen = FindSortIcon("LiveSortOpenIcon");
            var sortHigh = FindSortIcon("LiveSortHighIcon");
            var sortLow = FindSortIcon("LiveSortLowIcon");
            var sortLtp = FindSortIcon("LiveSortLtpIcon");
            var sortPrev = FindSortIcon("LiveSortPrevIcon");
            var sortChangePercent = FindSortIcon("LiveSortChangePercentIcon");
            var sortVolume = FindSortIcon("LiveSortVolumeIcon");
            var sortTurnover = FindSortIcon("LiveSortTurnoverIcon");
            var sortTrades = FindSortIcon("LiveSortTradesIcon");

            ResetSortIcons(
                sortLogo, sortSymbol, sortName, sortSector,
                sortOpen, sortHigh, sortLow, sortLtp,
                sortPrev, sortChangePercent, sortVolume,
                sortTurnover, sortTrades);

            var active = ViewModel.StockMarket.LiveSortColumn switch
            {
                "Logo" => sortLogo,
                "Name" => sortName,
                "Sector" => sortSector,
                "Open" => sortOpen,
                "High" => sortHigh,
                "Low" => sortLow,
                "Ltp" => sortLtp,
                "Prev" => sortPrev,
                "ChangePercent" => sortChangePercent,
                "Volume" => sortVolume,
                "Turnover" => sortTurnover,
                "Trades" => sortTrades,
                _ => sortSymbol
            };

            ApplyActiveSortIcon(active, ViewModel.StockMarket.IsLiveSortAscending);
        }

        private void LiveMarketSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.StockMarket.FilterLiveMarket(sender.Text);
            }
        }

        private FontIcon? FindSortIcon(string name)
        {
            return RootGrid?.FindName(name) as FontIcon;
        }

        private static void ResetSortIcons(params FontIcon?[] icons)
        {
            foreach (var icon in icons)
            {
                if (icon is null)
                {
                    continue;
                }

                icon.FontFamily = new FontFamily("Segoe UI Symbol");
                icon.Glyph = "↕";
                icon.Opacity = 0.6;
            }
        }

        private static void ApplyActiveSortIcon(FontIcon? icon, bool isAscending)
        {
            if (icon is null)
            {
                return;
            }

            icon.Glyph = isAscending ? "↑" : "↓";
            icon.Opacity = 1;
        }

        private void UpdateTopStocksTabStates(string selectedTab)
        {
            TopGainersTab.IsChecked = (selectedTab == "TopGainers");
            TopLosersTab.IsChecked = (selectedTab == "TopLosers");
            TopTurnoverTab.IsChecked = (selectedTab == "TopTurnover");
            TopVolumeTab.IsChecked = (selectedTab == "TopVolume");
            TopTransactionsTab.IsChecked = (selectedTab == "TopTransactions");
        }

        // Prevent the user from unchecking the currently active nav button
        // by clicking it a second time (ToggleButton default behavior).
        private void NavButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressNavCheckedEvents) return;
            if (sender is ToggleButton btn)
            {
                _suppressNavCheckedEvents = true;
                btn.IsChecked = true;
                _suppressNavCheckedEvents = false;
            }
        }

        private void CloseDetailPanel_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Calendar.ClearSelection();
            HideDetailPanel();
        }

        private void CalendarDaysView_ItemClick(object sender, ItemClickEventArgs e)
        {
            _showDetailPanelForNextSelectionChange = true;
            ViewModel.Calendar.SelectDayCommand.Execute(e.ClickedItem);
        }

        private void SearchResultsView_ItemClick(object sender, ItemClickEventArgs e)
        {
            ViewModel.Search.OpenResultCommand.Execute(e.ClickedItem);
        }

        private void SearchQueryBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            if (ViewModel.Search.SearchCommand.CanExecute(null))
            {
                ViewModel.Search.SearchCommand.Execute(null);
                e.Handled = true;
            }
        }

        // ── Section visibility ──

        private void UpdateSectionVisibility()
        {
            if (CalendarSection is null || SettingsSection is null)
            {
                return;
            }

            CalendarSection.Visibility = ViewModel.IsCalendarVisible ? Visibility.Visible : Visibility.Collapsed;
            SettingsSection.Visibility = ViewModel.IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;
            StockMarketSection.Visibility = ViewModel.IsStockMarketVisible ? Visibility.Visible : Visibility.Collapsed;
            RashifalSection.Visibility = ViewModel.IsRashifalVisible ? Visibility.Visible : Visibility.Collapsed;
            ShubhaSaitSection.Visibility = ViewModel.IsShubhaSaitVisible ? Visibility.Visible : Visibility.Collapsed;
            DateConverterSection.Visibility = ViewModel.IsDateConverterVisible ? Visibility.Visible : Visibility.Collapsed;

            var activeButton = ViewModel.SelectedSection switch
            {
                ShellSection.StockMarket => StockMarketButton,
                ShellSection.Settings => SettingsButton,
                ShellSection.Rashifal => RashifalButton,
                ShellSection.ShubhaSait => ShubhaSaitButton,
                ShellSection.DateConverter => DateConverterButton,
                _ => CalendarButton
            };

            // Guard against Checked/Unchecked event re-entry when we set
            // IsChecked programmatically below.
            _suppressNavCheckedEvents = true;
            CalendarButton.IsChecked = ReferenceEquals(activeButton, CalendarButton);
            StockMarketButton.IsChecked = ReferenceEquals(activeButton, StockMarketButton);
            RashifalButton.IsChecked = ReferenceEquals(activeButton, RashifalButton);
            ShubhaSaitButton.IsChecked = ReferenceEquals(activeButton, ShubhaSaitButton);
            DateConverterButton.IsChecked = ReferenceEquals(activeButton, DateConverterButton);
            SettingsButton.IsChecked = ReferenceEquals(activeButton, SettingsButton);
            _suppressNavCheckedEvents = false;
        }

        // ── Calendar layout (uses WindowLayoutService) ──

        private void UpdateCalendarLayout()
        {
            if (_calendarDaysView?.ItemsPanelRoot is not ItemsWrapGrid panel
                || _calendarLayoutHost is null
                || _weekdayHeaderGrid is null)
            {
                return;
            }

            var headerHeight = _weekdayHeaderBorder?.ActualHeight
                               ?? _weekdayHeaderGrid.ActualHeight;

            var metrics = WindowLayoutService.ComputeCalendarGridMetrics(
                _calendarLayoutHost.ActualWidth, _calendarLayoutHost.ActualHeight, headerHeight);

            if (metrics is null)
            {
                return;
            }

            _calendarDaysView.Width = metrics.GridWidth;
            _calendarDaysView.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;

            // Keep the weekday header strip aligned with the calendar grid.
            if (_weekdayHeaderBorder is not null)
            {
                _weekdayHeaderBorder.Width = metrics.GridWidth;
                _weekdayHeaderBorder.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
            }
            else
            {
                _weekdayHeaderGrid.Width = metrics.GridWidth;
                _weekdayHeaderGrid.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
            }

            panel.ItemWidth = metrics.ItemWidth;
            panel.ItemHeight = metrics.ItemHeight;
        }

        // ── Window positioning & bounds ──

        private void CenterWindowOnCurrentDisplay()
        {
            try
            {
                var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;
                var size = AppWindow.Size;

                var centeredX = workArea.X + ((workArea.Width - size.Width) / 2);
                var centeredY = workArea.Y + ((workArea.Height - size.Height) / 2);

                AppWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
            catch
            {
                // Best-effort centering only.
            }
        }

        /// <summary>
        /// Attempts to restore window bounds and presenter state from the user's saved settings.
        /// Returns true if bounds were successfully restored, false otherwise.
        /// </summary>
        private async Task<bool> TryRestoreWindowBoundsAsync()
        {
            try
            {
                var services = ((App)Application.Current).Services;
                var saved = await services.WindowBounds.LoadAsync();

                if (saved is null)
                {
                    return false;
                }

                // Validate that the saved bounds are on a visible display area.
                var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;

                // Ensure the window is at least partially visible on the current monitor.
                var onScreenX = Math.Max(workArea.X, Math.Min(saved.X, workArea.X + workArea.Width - 100));
                var onScreenY = Math.Max(workArea.Y, Math.Min(saved.Y, workArea.Y + workArea.Height - 100));

                // Clamp dimensions to the current work area.
                var width = Math.Clamp(saved.Width, (int)WindowLayoutService.AbsoluteMinWidthDip, workArea.Width);
                var height = Math.Clamp(saved.Height, (int)WindowLayoutService.AbsoluteMinHeightDip, workArea.Height);

                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
                AppWindow.Move(new Windows.Graphics.PointInt32(onScreenX, onScreenY));

                if (saved.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }

                _initialWindowSizeApplied = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Persists current window position, size, and state to the settings store.
        /// Called by the debounced resize pipeline so we don't write on every pixel drag.
        /// </summary>
        private async Task PersistWindowBoundsAsync()
        {
            try
            {
                var services = ((App)Application.Current).Services;
                var isMaximized = AppWindow.Presenter is OverlappedPresenter p
                                  && p.State == OverlappedPresenterState.Maximized;

                // When maximized, save the restored (pre-maximize) size from the last known
                // good position so restore works correctly next launch.
                if (!isMaximized)
                {
                    var pos = AppWindow.Position;
                    var size = AppWindow.Size;
                    await services.WindowBounds.SaveAsync(pos.X, pos.Y, size.Width, size.Height, false);
                }
                else
                {
                    // Save state as maximized; keep the last-saved position/size.
                    var existing = await services.WindowBounds.LoadAsync();
                    if (existing is not null)
                    {
                        await services.WindowBounds.SaveAsync(
                            existing.X, existing.Y, existing.Width, existing.Height, true);
                    }
                }
            }
            catch
            {
                // Best-effort persistence.
            }
        }

        // ── Calendar property changes ──

        private void Calendar_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CalendarViewModel.SelectedNavigationYear)
                || e.PropertyName == nameof(CalendarViewModel.SelectedNavigationMonth)
                || e.PropertyName == nameof(CalendarViewModel.MonthTitleNepali))
            {
                SyncCalendarNavigationSelections();
            }

            if (e.PropertyName == nameof(CalendarViewModel.SelectedDay))
            {
                if (ViewModel.Calendar.SelectedDay is null)
                {
                    HideDetailPanel();
                    _showDetailPanelForNextSelectionChange = false;
                }

                if (!_showDetailPanelForNextSelectionChange)
                {
                    return;
                }

                if (ViewModel.Calendar.SelectedDay is not null)
                {
                    _showDetailPanelForNextSelectionChange = false;
                    ShowDetailPanel();
                }
            }
        }

        private async void CalendarYearPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCalendarNavigationSelectionEvents)
            {
                return;
            }

            if (CalendarYearPicker.SelectedItem is not int year)
            {
                return;
            }

            _suppressCalendarNavigationSelectionEvents = true;
            await ViewModel.Calendar.NavigateToYearAsync(year);
            SyncCalendarNavigationSelections();
            _suppressCalendarNavigationSelectionEvents = false;
        }

        private async void CalendarMonthPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCalendarNavigationSelectionEvents)
            {
                return;
            }

            if (CalendarMonthPicker.SelectedItem is not CalendarViewModel.MonthNavigationOption month)
            {
                return;
            }

            _suppressCalendarNavigationSelectionEvents = true;
            await ViewModel.Calendar.NavigateToMonthAsync(month.MonthNumber);
            SyncCalendarNavigationSelections();
            _suppressCalendarNavigationSelectionEvents = false;
        }

        private void SyncCalendarNavigationSelections()
        {
            if (CalendarYearPicker is null || CalendarMonthPicker is null)
            {
                return;
            }

            _suppressCalendarNavigationSelectionEvents = true;
            CalendarYearPicker.SelectedItem = ViewModel.Calendar.SelectedNavigationYear;
            CalendarMonthPicker.SelectedItem = ViewModel.Calendar.SelectedNavigationMonth;
            _suppressCalendarNavigationSelectionEvents = false;
        }

        // ── Detail panel animations ──

        private void ShowDetailPanel()
        {
            DetailPanel.Visibility = Visibility.Visible;
            var animation = new Storyboard();

            var slide = new DoubleAnimation
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                To = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(slide, DetailTransform);
            Storyboard.SetTargetProperty(slide, "X");
            animation.Children.Add(slide);

            var fade = new DoubleAnimation
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                From = 0,
                To = 1,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(fade, DetailPanel);
            Storyboard.SetTargetProperty(fade, "Opacity");
            animation.Children.Add(fade);
            animation.Begin();
        }

        private void HideDetailPanel()
        {
            if (DetailPanel.Visibility == Visibility.Collapsed)
            {
                return;
            }

            var animation = new Storyboard();
            var slide = new DoubleAnimation
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                To = 32,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slide, DetailTransform);
            Storyboard.SetTargetProperty(slide, "X");
            animation.Children.Add(slide);

            var fade = new DoubleAnimation
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                To = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, DetailPanel);
            Storyboard.SetTargetProperty(fade, "Opacity");
            animation.Children.Add(fade);
            animation.Completed += (_, _) =>
            {
                DetailPanel.Visibility = Visibility.Collapsed;
            };
            animation.Begin();
        }

        // ── Window chrome helpers ──

        private void ApplyWindowIcon()
        {
            var iconCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "favicon.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico"),
                Path.Combine(Environment.CurrentDirectory, "favicon.ico")
            };

            foreach (var iconPath in iconCandidates)
            {
                if (!File.Exists(iconPath))
                {
                    continue;
                }

                try
                {
                    AppWindow.SetIcon(iconPath);
                    return;
                }
                catch
                {
                    // Try the next candidate path.
                }
            }
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                AppWindow.TitleBar.PreferredTheme = Microsoft.UI.Windowing.TitleBarTheme.Dark;
            }
            catch
            {
                // Fall back to the DWM attribute for older title bar implementations.
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var useDarkMode = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeBefore20H1, ref useDarkMode, sizeof(int));
        }

        // ── Minimum window size via WndProc hook ──

        private void ConfigureMinimumWindowSize()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _wndProcDelegate = WindowProc;
            var newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _previousWndProc = SetWindowLongPtr(hwnd, GwlpWndProc, newWndProc);
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmGetMinMaxInfo)
            {
                var dpi = WindowLayoutService.SafeDpi(GetDpiForWindow(hWnd));
                var (minWidthPx, minHeightPx) = WindowLayoutService.GetMinimumSizePx(dpi);

                var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                minMaxInfo.ptMinTrackSize.X = Math.Max(minMaxInfo.ptMinTrackSize.X, minWidthPx);
                minMaxInfo.ptMinTrackSize.Y = Math.Max(minMaxInfo.ptMinTrackSize.Y, minHeightPx);
                Marshal.StructureToPtr(minMaxInfo, lParam, false);

                return IntPtr.Zero;
            }

            if (_previousWndProc != IntPtr.Zero)
            {
                return CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam);
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // ── Window resize behavior ──

        private void ConfigureWindowResizeBehavior()
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (sender.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            var currentState = presenter.State;
            if (_lastPresenterState == OverlappedPresenterState.Maximized && currentState == OverlappedPresenterState.Restored)
            {
                _ = TryResizeToInitialWindowSize();
            }

            // Persist state on maximize/minimize transitions too.
            if (_lastPresenterState != currentState)
            {
                _resizePipeline?.RequestLayout();
            }

            _lastPresenterState = currentState;
        }

        private void ApplyInitialWindowSize()
        {
            if (_initialWindowSizeApplied)
            {
                return;
            }

            if (TryResizeToInitialWindowSize())
            {
                _initialWindowSizeApplied = true;
            }
        }

        private bool TryResizeToInitialWindowSize()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var dpi = WindowLayoutService.SafeDpi(GetDpiForWindow(hwnd));
            var (widthPx, heightPx) = WindowLayoutService.GetTargetSizePx(dpi, AppWindow);

            try
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(widthPx, heightPx));
                return true;
            }
            catch
            {
                // Best-effort sizing; min-size hook still enforces lower bound.
                return false;
            }
        }

        // ── Rashifal picker handlers ──

        private void RashifalTypePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (((ComboBox)sender).SelectedItem is ComboBoxItem item && item.Tag is string type)
            {
                ViewModel.Rashifal.SelectedType = type;
            }
        }

        private void RashifalZodiacPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (((ComboBox)sender).SelectedItem is ComboBoxItem item && item.Tag is string key)
            {
                ViewModel.Rashifal.SelectedZodiacKey = key;
            }
        }

        // ── Shubha Sait picker handlers ──

        private void SaitYearPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSaitSelectionEvents) return;
            if (SaitYearPicker.SelectedItem is not string year) return;

            _suppressSaitSelectionEvents = true;
            ViewModel.ShubhaSait.SelectedYear = year;
            SyncShubhaSaitSelections();
            _suppressSaitSelectionEvents = false;
        }

        private void SaitCategoryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSaitSelectionEvents) return;
            if (SaitCategoryPicker.SelectedItem is not SaitCategoryInfo cat) return;

            _suppressSaitSelectionEvents = true;
            ViewModel.ShubhaSait.SelectedCategoryKey = cat.FullKey;
            SyncShubhaSaitSelections();
            _suppressSaitSelectionEvents = false;
        }

        private async void SaitRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ShubhaSait.IsBusy) return;

            _suppressSaitSelectionEvents = true;
            await ViewModel.ShubhaSait.RefreshAsync();
            SyncShubhaSaitSelections();
            _suppressSaitSelectionEvents = false;
        }

        // ── Date Converter handlers (using cached references) ──

        private void DateConverterDirection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (((ComboBox)sender).SelectedItem is ComboBoxItem item && item.Tag is string direction)
            {
                ViewModel.DateConverter.ConversionDirection = direction;
            }

            UpdateDateConverterMonthLabels();
            SyncDateConverterInputParts();
        }

        private void UpdateDateConverterMonthLabels()
        {
            if (_dateConverterMonthPicker is null)
            {
                return;
            }

            var selectedItem = _dateConverterMonthPicker.SelectedItem;

            var monthNames = ViewModel.DateConverter.ConversionDirection == "AD"
                ? new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }
                : new[] { "बैशाख", "जेठ", "असार", "साउन", "भदौ", "असोज", "कात्तिक", "मंसिर", "पुष", "माघ", "फाल्गुन", "चैत" };

            foreach (var entry in _dateConverterMonthPicker.Items)
            {
                if (entry is not ComboBoxItem monthItem
                    || monthItem.Tag is not string monthTag
                    || !int.TryParse(monthTag, out var monthNumber)
                    || monthNumber is < 1 or > 12)
                {
                    continue;
                }

                monthItem.Content = monthNames[monthNumber - 1];
            }

            if (selectedItem is not null)
            {
                _suppressDateConverterInputSync = true;
                _dateConverterMonthPicker.SelectedItem = null;
                _dateConverterMonthPicker.SelectedItem = selectedItem;
                _suppressDateConverterInputSync = false;
            }
        }

        private void RootGrid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IsInputElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            RootGrid.Focus(FocusState.Programmatic);
        }

        private static bool IsInputElement(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is TextBox
                    || source is ComboBox
                    || source is PasswordBox
                    || source is DatePicker
                    || source is TimePicker)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void DateConverterInputPart_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDateConverterInputSync)
            {
                return;
            }

            UpdateDateConverterInputDateFromControls();
        }

        private void DateConverterNumericOnly_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            if (string.IsNullOrEmpty(args.NewText))
            {
                return;
            }

            foreach (var ch in args.NewText)
            {
                if (!char.IsDigit(ch))
                {
                    args.Cancel = true;
                    return;
                }
            }
        }

        private void DateConverterMonthPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDateConverterInputSync)
            {
                return;
            }

            UpdateDateConverterInputDateFromControls();
        }

        private void UpdateDateConverterInputDateFromControls()
        {
            if (_dateConverterYearBox is null || _dateConverterMonthPicker is null || _dateConverterDayBox is null)
            {
                return;
            }

            var yearText = _dateConverterYearBox.Text.Trim();
            var dayText = _dateConverterDayBox.Text.Trim();
            var month = 0;
            if (_dateConverterMonthPicker.SelectedItem is ComboBoxItem monthItem
                && monthItem.Tag is string monthTag
                && int.TryParse(monthTag, out var parsedMonth))
            {
                month = parsedMonth;
            }

            if (string.IsNullOrEmpty(yearText) && month == 0 && string.IsNullOrEmpty(dayText))
            {
                ViewModel.DateConverter.InputDate = string.Empty;
                return;
            }

            if (!int.TryParse(yearText, out var year)
                || !int.TryParse(dayText, out var day)
                || year <= 0
                || month is < 1 or > 12
                || day is < 1 or > 32)
            {
                ViewModel.DateConverter.InputDate = string.Empty;
                return;
            }

            ViewModel.DateConverter.InputDate = $"{year:0000}-{month:00}-{day:00}";
        }

        private void DateConverterInputPart_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            if (ReferenceEquals(sender, _dateConverterYearBox))
            {
                _dateConverterMonthPicker?.Focus(FocusState.Programmatic);
            }
            else if (ReferenceEquals(sender, _dateConverterMonthPicker))
            {
                _dateConverterDayBox?.Focus(FocusState.Programmatic);
            }
            else if (ReferenceEquals(sender, _dateConverterDayBox))
            {
                _dateConverterConvertButton?.Focus(FocusState.Programmatic);
                if (ViewModel.DateConverter.ConvertCommand.CanExecute(null))
                {
                    ViewModel.DateConverter.ConvertCommand.Execute(null);
                }
            }

            e.Handled = true;
        }

        private void DateConverterClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dateConverterYearBox is null || _dateConverterMonthPicker is null || _dateConverterDayBox is null)
            {
                return;
            }

            _suppressDateConverterInputSync = true;
            _dateConverterYearBox.Text = string.Empty;
            _dateConverterMonthPicker.SelectedIndex = -1;
            _dateConverterDayBox.Text = string.Empty;
            _suppressDateConverterInputSync = false;

            ViewModel.DateConverter.InputDate = string.Empty;
            _dateConverterYearBox.Focus(FocusState.Programmatic);
        }

        // ── Support links ──

        private async void GitHubSupportButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenSupportLinkAsync("https://github.com/menaceXnadin");
        }

        private async void LinkedInSupportButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenSupportLinkAsync("https://www.linkedin.com/in/nadintamang/");
        }

        private async void EsewaSupportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "eSewa",
                Content = "Nothing to pay, but your kindness is noted and appreciated.",
                CloseButtonText = "OK",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async Task OpenSupportLinkAsync(string link)
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            {
                return;
            }

            var launched = await Launcher.LaunchUriAsync(uri);
            if (launched)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Unable to open link",
                Content = link,
                CloseButtonText = "OK",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        // ── Sync helpers (using cached references) ──

        private void SyncDateConverterInputParts()
        {
            if (_dateConverterYearBox is null || _dateConverterMonthPicker is null || _dateConverterDayBox is null)
            {
                return;
            }

            var source = ViewModel.DateConverter.InputDate;
            var year = DateTime.Now.Year;
            var month = DateTime.Now.Month;
            var day = DateTime.Now.Day;

            if (!string.IsNullOrWhiteSpace(source))
            {
                var parts = source.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length == 3
                    && int.TryParse(parts[0], out var parsedYear)
                    && int.TryParse(parts[1], out var parsedMonth)
                    && int.TryParse(parts[2], out var parsedDay)
                    && parsedYear > 0
                    && parsedMonth is >= 1 and <= 12
                    && parsedDay is >= 1 and <= 32)
                {
                    year = parsedYear;
                    month = parsedMonth;
                    day = parsedDay;
                }
            }

            _suppressDateConverterInputSync = true;
            _dateConverterYearBox.Text = year.ToString("0000");
            ComboBoxItem? monthSelection = null;
            foreach (var item in _dateConverterMonthPicker.Items)
            {
                if (item is ComboBoxItem monthItem
                    && monthItem.Tag is string monthTag
                    && int.TryParse(monthTag, out var monthValue)
                    && monthValue == month)
                {
                    monthSelection = monthItem;
                    break;
                }
            }

            _dateConverterMonthPicker.SelectedItem = monthSelection;
            _dateConverterDayBox.Text = day.ToString("00");
            _suppressDateConverterInputSync = false;

            ViewModel.DateConverter.InputDate = $"{year:0000}-{month:00}-{day:00}";
        }

        private void SyncShubhaSaitSelections()
        {
            if (ViewModel.ShubhaSait.SelectedYear is not null)
            {
                foreach (var item in SaitYearPicker.Items)
                {
                    if (item is string y && y == ViewModel.ShubhaSait.SelectedYear)
                    {
                        SaitYearPicker.SelectedItem = item;
                        break;
                    }
                }
            }

            if (ViewModel.ShubhaSait.SelectedCategoryKey is not null)
            {
                foreach (var item in SaitCategoryPicker.Items)
                {
                    if (item is SaitCategoryInfo cat && cat.FullKey == ViewModel.ShubhaSait.SelectedCategoryKey)
                    {
                        SaitCategoryPicker.SelectedItem = item;
                        break;
                    }
                }
            }

        }

        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public Visibility StringToVisibility(string value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
