using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenPatro.ViewModels;

namespace OpenPatro
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            ViewModel = ((App)Application.Current).MainViewModel;
            InitializeComponent();
            ViewModel.Calendar.PropertyChanged += Calendar_PropertyChanged;

            ExtendsContentIntoTitleBar = false;
            SizeChanged += MainWindow_SizeChanged;

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

        private void CalendarButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Calendar;
            UpdateSectionVisibility();
        }

        private void SearchButton_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedSection = ShellSection.Search;
            UpdateSectionVisibility();
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
            panel.ItemHeight = Math.Floor(itemWidth * 0.92);
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
    }
}
