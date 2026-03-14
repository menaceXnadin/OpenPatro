using System;
using System.IO;
using System.Runtime.InteropServices;
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
        private const double MinWindowWidthDip = 920;
        private const double MinWindowHeightDip = 640;
        private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmaUseImmersiveDarkMode = 20;

        private IntPtr _previousWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate;

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
            Closed += MainWindow_Closed;

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

            CalendarButton.Checked += CalendarButton_Checked;
            SearchButton.Checked += SearchButton_Checked;
            SettingsButton.Checked += SettingsButton_Checked;

            CalendarButton.IsChecked = ViewModel.SelectedSection == ShellSection.Calendar;
            SearchButton.IsChecked = ViewModel.SelectedSection == ShellSection.Search;
            SettingsButton.IsChecked = ViewModel.SelectedSection == ShellSection.Settings;
            UpdateSectionVisibility();
            UpdateCalendarLayout();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            UpdateCalendarLayout();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero || _previousWndProc == IntPtr.Zero)
            {
                return;
            }

            SetWindowLongPtr(hwnd, GwlpWndProc, _previousWndProc);
            _previousWndProc = IntPtr.Zero;
            _wndProcDelegate = null;
        }

        private void CalendarButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Calendar;
            UpdateSectionVisibility();
        }

        private void SearchButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Search;
            UpdateSectionVisibility();
            SearchQueryBox?.Focus(FocusState.Programmatic);
        }

        private void SettingsButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Settings;
            UpdateSectionVisibility();
        }

        private void CloseDetailPanel_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Calendar.ClearSelection();
            HideDetailPanel();
        }

        private void CalendarDaysView_ItemClick(object sender, ItemClickEventArgs e)
        {
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
            if (CalendarSection is null || SearchSection is null || SettingsSection is null)
            {
                return;
            }

            CalendarSection.Visibility = ViewModel.IsCalendarVisible ? Visibility.Visible : Visibility.Collapsed;
            SearchSection.Visibility = ViewModel.IsSearchVisible ? Visibility.Visible : Visibility.Collapsed;
            SettingsSection.Visibility = ViewModel.IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;

            var calendarNav = RootGrid?.FindName("CalendarButton");
            if (calendarNav is ToggleButton calendarToggle)
            {
                calendarToggle.IsChecked = ViewModel.IsCalendarVisible;
            }
            else if (calendarNav is RadioButton calendarRadio)
            {
                calendarRadio.IsChecked = ViewModel.IsCalendarVisible;
            }

            var searchNav = RootGrid?.FindName("SearchButton");
            if (searchNav is ToggleButton searchToggle)
            {
                searchToggle.IsChecked = ViewModel.IsSearchVisible;
            }
            else if (searchNav is RadioButton searchRadio)
            {
                searchRadio.IsChecked = ViewModel.IsSearchVisible;
            }

            var settingsNav = RootGrid?.FindName("SettingsButton");
            if (settingsNav is ToggleButton settingsToggle)
            {
                settingsToggle.IsChecked = ViewModel.IsSettingsVisible;
            }
            else if (settingsNav is RadioButton settingsRadio)
            {
                settingsRadio.IsChecked = ViewModel.IsSettingsVisible;
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

            var itemWidth = Math.Floor(availableWidth / 7d);
            itemWidth = Math.Clamp(itemWidth, 92, 144);

            var gridWidth = itemWidth * 7;
            weekdayHeader.Width = gridWidth;
            calendarDaysView.Width = gridWidth;

            panel.ItemWidth = itemWidth;
            panel.ItemHeight = Math.Floor(itemWidth * 0.98);
        }

        private void Calendar_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CalendarViewModel.SelectedDay))
            {
                if (ViewModel.Calendar.SelectedDay is null)
                {
                    HideDetailPanel();
                }
                else
                {
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
            animation.Completed += (_, _) => DetailPanel.Visibility = Visibility.Collapsed;
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
    }
}
