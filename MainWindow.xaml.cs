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
using OpenPatro.ViewModels;
using Windows.System;

namespace OpenPatro
{
    public sealed partial class MainWindow : Window
    {
        private const int GwlpWndProc = -4;
        private const uint WmGetMinMaxInfo = 0x0024;
        private const double MinWindowWidthDip = 1160;
        private const double MinWindowHeightDip = 1050;
        private const double AbsoluteMinWindowWidthDip = 800;
        private const double AbsoluteMinWindowHeightDip = 600;
        private const double MinWindowWidthWorkAreaRatio = 0.72;
        private const double MinWindowHeightWorkAreaRatio = 0.88;
        private const int WindowWorkAreaPaddingPx = 24;
        private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmaUseImmersiveDarkMode = 20;

        private IntPtr _previousWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate;
        private bool _showDetailPanelForNextSelectionChange;
        private bool _initialWindowSizeApplied;
        private OverlappedPresenterState _lastPresenterState = OverlappedPresenterState.Restored;
        private bool _suppressSaitSelectionEvents;
        private bool _suppressDateConverterInputSync;
        private bool _suppressNavCheckedEvents;
        private bool _suppressCalendarNavigationSelectionEvents;

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

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded;

            ConfigureWindowResizeBehavior();
            ApplyInitialWindowSize();
            CenterWindowOnCurrentDisplay();

            CalendarButton.Checked += CalendarButton_Checked;
            CalendarButton.Unchecked += NavButton_Unchecked;
            SettingsButton.Checked += SettingsButton_Checked;
            SettingsButton.Unchecked += NavButton_Unchecked;
            RashifalButton.Checked += RashifalButton_Checked;
            RashifalButton.Unchecked += NavButton_Unchecked;
            ShubhaSaitButton.Checked += ShubhaSaitButton_Checked;
            ShubhaSaitButton.Unchecked += NavButton_Unchecked;
            DateConverterButton.Checked += DateConverterButton_Checked;
            DateConverterButton.Unchecked += NavButton_Unchecked;

            CalendarButton.IsChecked = ViewModel.SelectedSection == ShellSection.Calendar;
            SettingsButton.IsChecked = ViewModel.SelectedSection == ShellSection.Settings;
            RashifalButton.IsChecked = ViewModel.SelectedSection == ShellSection.Rashifal;
            ShubhaSaitButton.IsChecked = ViewModel.SelectedSection == ShellSection.ShubhaSait;
            DateConverterButton.IsChecked = ViewModel.SelectedSection == ShellSection.DateConverter;
            UpdateSectionVisibility();
            UpdateCalendarLayout();
            SyncCalendarNavigationSelections();
            UpdateDateConverterMonthLabels();
            SyncDateConverterInputParts();

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                _lastPresenterState = presenter.State;
            }

            AppWindow.Changed -= AppWindow_Changed;
            AppWindow.Changed += AppWindow_Changed;
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            UpdateCalendarLayout();
        }

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

        private void UpdateSectionVisibility()
        {
            if (CalendarSection is null || SettingsSection is null)
            {
                return;
            }

            CalendarSection.Visibility = ViewModel.IsCalendarVisible ? Visibility.Visible : Visibility.Collapsed;
            SettingsSection.Visibility = ViewModel.IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;
            RashifalSection.Visibility = ViewModel.IsRashifalVisible ? Visibility.Visible : Visibility.Collapsed;
            ShubhaSaitSection.Visibility = ViewModel.IsShubhaSaitVisible ? Visibility.Visible : Visibility.Collapsed;
            DateConverterSection.Visibility = ViewModel.IsDateConverterVisible ? Visibility.Visible : Visibility.Collapsed;

            var activeButton = ViewModel.SelectedSection switch
            {
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
            RashifalButton.IsChecked = ReferenceEquals(activeButton, RashifalButton);
            ShubhaSaitButton.IsChecked = ReferenceEquals(activeButton, ShubhaSaitButton);
            DateConverterButton.IsChecked = ReferenceEquals(activeButton, DateConverterButton);
            SettingsButton.IsChecked = ReferenceEquals(activeButton, SettingsButton);
            _suppressNavCheckedEvents = false;
        }

        private void UpdateCalendarLayout()
        {
            var calendarDaysView = RootGrid?.FindName("CalendarDaysView") as GridView;
            var layoutHost = RootGrid?.FindName("CalendarLayoutHost") as Grid;
            var weekdayHeader = RootGrid?.FindName("WeekdayHeaderGrid") as Grid;

            if (calendarDaysView?.ItemsPanelRoot is not ItemsWrapGrid panel || layoutHost is null || weekdayHeader is null)
            {
                return;
            }

            var availableWidth = layoutHost.ActualWidth;
            if (availableWidth <= 0)
            {
                return;
            }

            const int columns = 7;
            const int rows = 6;
            const double minCellSize = 72;
            const double maxCellSize = 260;
            const double cellAspectRatio = 0.92; // height = width * ratio

            // The weekday header strip Border wraps the inner grid.
            var weekdayHeaderParent = weekdayHeader.Parent as Microsoft.UI.Xaml.Controls.Border;
            var headerHeight = weekdayHeaderParent?.ActualHeight ?? weekdayHeader.ActualHeight;
            // 10 = margin below the weekday header border
            var availableHeight = Math.Max(0, layoutHost.ActualHeight - headerHeight - 10);

            // Compute the cell width that would fill width vs. height independently.
            var widthFromAvailableWidth = Math.Floor(availableWidth / columns);
            var widthFromAvailableHeight = Math.Floor(availableHeight / rows / cellAspectRatio);

            // Use the smaller so the 7×6 grid fits in BOTH dimensions.
            var itemWidth = Math.Min(widthFromAvailableWidth, widthFromAvailableHeight);
            itemWidth = Math.Clamp(itemWidth, minCellSize, maxCellSize);

            var itemHeight = Math.Floor(itemWidth * cellAspectRatio);
            itemHeight = Math.Clamp(itemHeight, minCellSize * cellAspectRatio, maxCellSize * cellAspectRatio);

            // Small buffer (+4px) accounts for the GridView's internal ScrollViewer/Border
            // overhead that would otherwise eat into the exact 7*itemWidth, causing
            // the ItemsWrapGrid to wrap at 6 columns instead of 7.
            var gridWidth = (itemWidth * columns) + 4;
            calendarDaysView.Width = gridWidth;

            // Always center the grid so there is no dead space on the right.
            if (weekdayHeaderParent is not null)
            {
                weekdayHeaderParent.Width = gridWidth;
                weekdayHeaderParent.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
            }
            else
            {
                weekdayHeader.Width = gridWidth;
                weekdayHeader.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
            }

            calendarDaysView.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;

            panel.ItemWidth = itemWidth;
            panel.ItemHeight = itemHeight;
        }

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

        private static uint GetWindowDpiOrDefault(IntPtr hwnd)
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 96u : dpi;
        }

        private static int DipToPx(double dip, uint dpi)
        {
            return (int)Math.Ceiling(dip * dpi / 96.0);
        }

        private (int minWidthPx, int minHeightPx) GetMinimumWindowSizePx(IntPtr hwnd)
        {
            var dpi = GetWindowDpiOrDefault(hwnd);
            return (DipToPx(AbsoluteMinWindowWidthDip, dpi), DipToPx(AbsoluteMinWindowHeightDip, dpi));
        }

        private (int WidthPx, int HeightPx) GetTargetWindowSizePx(IntPtr hwnd)
        {
            var dpi = GetWindowDpiOrDefault(hwnd);

            var preferredWidthPx = DipToPx(MinWindowWidthDip, dpi);
            var preferredHeightPx = DipToPx(MinWindowHeightDip, dpi);

            var (absoluteMinWidthPx, absoluteMinHeightPx) = GetMinimumWindowSizePx(hwnd);

            var maxWidthPx = int.MaxValue;
            var maxHeightPx = int.MaxValue;
            var targetWidthPx = preferredWidthPx;
            var targetHeightPx = preferredHeightPx;

            try
            {
                var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;
                maxWidthPx = Math.Max(480, workArea.Width - WindowWorkAreaPaddingPx);
                maxHeightPx = Math.Max(480, workArea.Height - WindowWorkAreaPaddingPx);

                targetWidthPx = (int)Math.Floor(maxWidthPx * MinWindowWidthWorkAreaRatio);
                targetHeightPx = (int)Math.Floor(maxHeightPx * MinWindowHeightWorkAreaRatio);
            }
            catch
            {
                // Fall back to DPI-scaled preferred sizing when monitor work area can't be read.
            }

            var effectiveMinWidthPx = Math.Min(absoluteMinWidthPx, maxWidthPx);
            var effectiveMinHeightPx = Math.Min(absoluteMinHeightPx, maxHeightPx);

            var widthPx = Math.Clamp(targetWidthPx, effectiveMinWidthPx, maxWidthPx);
            var heightPx = Math.Clamp(targetHeightPx, effectiveMinHeightPx, maxHeightPx);

            return (widthPx, heightPx);
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmGetMinMaxInfo)
            {
                var (minWidthPx, minHeightPx) = GetMinimumWindowSizePx(hWnd);

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

            var (widthPx, heightPx) = GetTargetWindowSizePx(hwnd);

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
            var monthPicker = RootGrid?.FindName("DateConverterMonthPicker") as ComboBox;
            if (monthPicker is null)
            {
                return;
            }

            var selectedItem = monthPicker.SelectedItem;

            var monthNames = ViewModel.DateConverter.ConversionDirection == "AD"
                ? new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }
                : new[] { "बैशाख", "जेठ", "असार", "साउन", "भदौ", "असोज", "कात्तिक", "मंसिर", "पुष", "माघ", "फाल्गुन", "चैत" };

            foreach (var entry in monthPicker.Items)
            {
                if (entry is not ComboBoxItem item
                    || item.Tag is not string monthTag
                    || !int.TryParse(monthTag, out var monthNumber)
                    || monthNumber is < 1 or > 12)
                {
                    continue;
                }

                item.Content = monthNames[monthNumber - 1];
            }

            if (selectedItem is not null)
            {
                _suppressDateConverterInputSync = true;
                monthPicker.SelectedItem = null;
                monthPicker.SelectedItem = selectedItem;
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
            var yearBox = RootGrid?.FindName("DateConverterYearBox") as TextBox;
            var monthPicker = RootGrid?.FindName("DateConverterMonthPicker") as ComboBox;
            var dayBox = RootGrid?.FindName("DateConverterDayBox") as TextBox;
            if (yearBox is null || monthPicker is null || dayBox is null)
            {
                return;
            }

            var yearText = yearBox.Text.Trim();
            var dayText = dayBox.Text.Trim();
            var month = 0;
            if (monthPicker.SelectedItem is ComboBoxItem monthItem
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

            var yearBox = RootGrid?.FindName("DateConverterYearBox") as TextBox;
            var monthPicker = RootGrid?.FindName("DateConverterMonthPicker") as ComboBox;
            var dayBox = RootGrid?.FindName("DateConverterDayBox") as TextBox;
            var convertButton = RootGrid?.FindName("DateConverterConvertButton") as Button;

            if (ReferenceEquals(sender, yearBox))
            {
                monthPicker?.Focus(FocusState.Programmatic);
            }
            else if (ReferenceEquals(sender, monthPicker))
            {
                dayBox?.Focus(FocusState.Programmatic);
            }
            else if (ReferenceEquals(sender, dayBox))
            {
                convertButton?.Focus(FocusState.Programmatic);
                if (ViewModel.DateConverter.ConvertCommand.CanExecute(null))
                {
                    ViewModel.DateConverter.ConvertCommand.Execute(null);
                }
            }

            e.Handled = true;
        }

        private void DateConverterClearButton_Click(object sender, RoutedEventArgs e)
        {
            var yearBox = RootGrid?.FindName("DateConverterYearBox") as TextBox;
            var monthPicker = RootGrid?.FindName("DateConverterMonthPicker") as ComboBox;
            var dayBox = RootGrid?.FindName("DateConverterDayBox") as TextBox;
            if (yearBox is null || monthPicker is null || dayBox is null)
            {
                return;
            }

            _suppressDateConverterInputSync = true;
            yearBox.Text = string.Empty;
            monthPicker.SelectedIndex = -1;
            dayBox.Text = string.Empty;
            _suppressDateConverterInputSync = false;

            ViewModel.DateConverter.InputDate = string.Empty;
            yearBox.Focus(FocusState.Programmatic);
        }

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

        private void SyncDateConverterInputParts()
        {
            var yearBox = RootGrid?.FindName("DateConverterYearBox") as TextBox;
            var monthPicker = RootGrid?.FindName("DateConverterMonthPicker") as ComboBox;
            var dayBox = RootGrid?.FindName("DateConverterDayBox") as TextBox;
            if (yearBox is null || monthPicker is null || dayBox is null)
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
            yearBox.Text = year.ToString("0000");
            ComboBoxItem? monthSelection = null;
            foreach (var item in monthPicker.Items)
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

            monthPicker.SelectedItem = monthSelection;
            dayBox.Text = day.ToString("00");
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
