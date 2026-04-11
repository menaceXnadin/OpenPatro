using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
        private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmaUseImmersiveDarkMode = 20;

        private IntPtr _previousWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate;
        private bool _showDetailPanelForNextSelectionChange;
        private bool _initialWindowSizeApplied;
        private OverlappedPresenterState _lastPresenterState = OverlappedPresenterState.Restored;
        private bool _suppressSaitSelectionEvents;

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

            CalendarButton.Checked += CalendarButton_Checked;
            SettingsButton.Checked += SettingsButton_Checked;
            RashifalButton.Checked += RashifalButton_Checked;
            ShubhaSaitButton.Checked += ShubhaSaitButton_Checked;
            DateConverterButton.Checked += DateConverterButton_Checked;

            CalendarButton.IsChecked = ViewModel.SelectedSection == ShellSection.Calendar;
            SettingsButton.IsChecked = ViewModel.SelectedSection == ShellSection.Settings;
            RashifalButton.IsChecked = ViewModel.SelectedSection == ShellSection.Rashifal;
            ShubhaSaitButton.IsChecked = ViewModel.SelectedSection == ShellSection.ShubhaSait;
            DateConverterButton.IsChecked = ViewModel.SelectedSection == ShellSection.DateConverter;
            UpdateSectionVisibility();
            UpdateCalendarLayout();

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
            ViewModel.SelectedSection = ShellSection.Calendar;
            UpdateSectionVisibility();
        }

        private void SettingsButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Settings;
            UpdateSectionVisibility();
        }

        private async void RashifalButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Rashifal;
            UpdateSectionVisibility();
            await ViewModel.Rashifal.InitializeAsync();
        }

        private async void ShubhaSaitButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.ShubhaSait;
            UpdateSectionVisibility();
            _suppressSaitSelectionEvents = true;
            await ViewModel.ShubhaSait.InitializeAsync();
            SyncShubhaSaitSelections();
            _suppressSaitSelectionEvents = false;
        }

        private void DateConverterButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.DateConverter;
            UpdateSectionVisibility();
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

            SyncToggleButton("CalendarButton", ViewModel.IsCalendarVisible);
            SyncToggleButton("SettingsButton", ViewModel.IsSettingsVisible);
            SyncToggleButton("RashifalButton", ViewModel.IsRashifalVisible);
            SyncToggleButton("ShubhaSaitButton", ViewModel.IsShubhaSaitVisible);
            SyncToggleButton("DateConverterButton", ViewModel.IsDateConverterVisible);
        }

        private void SyncToggleButton(string name, bool isChecked)
        {
            var element = RootGrid?.FindName(name);
            if (element is ToggleButton toggle)
            {
                toggle.IsChecked = isChecked;
            }
            else if (element is RadioButton radio)
            {
                radio.IsChecked = isChecked;
            }
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

            const double minItemWidth = 92;
            const double restoredMaxItemWidth = 144;
            const double maximizedMaxItemWidth = 260;
            const double itemHeightRatio = 0.98;

            var isMaximized = AppWindow.Presenter is OverlappedPresenter presenter
                              && presenter.State == OverlappedPresenterState.Maximized;

            var itemWidth = Math.Floor(availableWidth / 7d);
            if (isMaximized)
            {
                itemWidth = Math.Clamp(itemWidth, minItemWidth, maximizedMaxItemWidth);
            }
            else
            {
                itemWidth = Math.Clamp(itemWidth, minItemWidth, restoredMaxItemWidth);
            }

            var availableHeight = Math.Max(0, layoutHost.ActualHeight - weekdayHeader.ActualHeight - 16);
            var itemHeight = isMaximized
                ? Math.Max(88, Math.Floor(availableHeight / 6d))
                : Math.Floor(itemWidth * itemHeightRatio);

            var gridWidth = itemWidth * 7;
            weekdayHeader.Width = gridWidth;
            calendarDaysView.Width = gridWidth;

            weekdayHeader.HorizontalAlignment = isMaximized
                ? Microsoft.UI.Xaml.HorizontalAlignment.Center
                : Microsoft.UI.Xaml.HorizontalAlignment.Left;
            calendarDaysView.HorizontalAlignment = isMaximized
                ? Microsoft.UI.Xaml.HorizontalAlignment.Center
                : Microsoft.UI.Xaml.HorizontalAlignment.Left;

            panel.ItemWidth = itemWidth;
            panel.ItemHeight = itemHeight;
        }

        private void Calendar_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
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

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmGetMinMaxInfo)
            {
                var dpi = GetDpiForWindow(hWnd);
                if (dpi == 0)
                {
                    dpi = 96;
                }

                var minWidthPx = (int)Math.Ceiling(MinWindowWidthDip * dpi / 96.0);
                var minHeightPx = (int)Math.Ceiling(MinWindowHeightDip * dpi / 96.0);

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

            presenter.IsResizable = false;
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
                _ = TryResizeToMinimumWindowSize();
            }

            _lastPresenterState = currentState;
        }

        private void ApplyInitialWindowSize()
        {
            if (_initialWindowSizeApplied)
            {
                return;
            }

            if (TryResizeToMinimumWindowSize())
            {
                _initialWindowSizeApplied = true;
            }
        }

        private bool TryResizeToMinimumWindowSize()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            var widthPx = (int)Math.Ceiling(MinWindowWidthDip * dpi / 96.0);
            var heightPx = (int)Math.Ceiling(MinWindowHeightDip * dpi / 96.0);

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

        private void SaitMonthPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSaitSelectionEvents) return;
            if (SaitMonthPicker.SelectedItem is not SaitMonthInfo month) return;

            _suppressSaitSelectionEvents = true;
            ViewModel.ShubhaSait.SelectedMonth = month.MonthKey;
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

            if (ViewModel.ShubhaSait.SelectedMonth is not null)
            {
                foreach (var item in SaitMonthPicker.Items)
                {
                    if (item is SaitMonthInfo m && m.MonthKey == ViewModel.ShubhaSait.SelectedMonth)
                    {
                        SaitMonthPicker.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public Visibility StringToVisibility(string value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
