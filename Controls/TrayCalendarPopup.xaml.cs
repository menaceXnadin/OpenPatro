using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenPatro.ViewModels;

namespace OpenPatro.Controls;

public sealed partial class TrayCalendarPopup : UserControl
{
    public TrayCalendarPopup()
    {
        InitializeComponent();
    }

    public void Attach(CalendarViewModel viewModel)
    {
        DataContext = viewModel;
    }

    private void DaysGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (DataContext is CalendarViewModel viewModel)
        {
            viewModel.SelectDayCommand.Execute(e.ClickedItem);
        }
    }
}