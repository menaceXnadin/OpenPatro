using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Services;

namespace OpenPatro.ViewModels;

public sealed class SearchViewModel : BindableBase
{
    private readonly AppServices _services;
    private string _query = string.Empty;
    private bool _isBusy;

    public SearchViewModel(AppServices services)
    {
        _services = services;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(Query) && !IsBusy);
        OpenResultCommand = new AsyncRelayCommand(OpenResultAsync);
    }

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    public ICommand SearchCommand { get; }

    public ICommand OpenResultCommand { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            Results.Clear();
            var days = await _services.CalendarRepository.SearchAsync(Query);
            var notes = await _services.UserRepository.SearchNotesAsync(Query);

            foreach (var day in days)
            {
                notes.TryGetValue($"{day.BsYear}-{day.BsMonth}-{day.BsDay}", out var noteText);
                Results.Add(new SearchResultViewModel
                {
                    Title = day.EventSummary == "--" ? day.BsFullDate : day.EventSummary,
                    Subtitle = $"{day.BsFullDate}  |  {day.AdDateText}",
                    NotePreview = string.IsNullOrWhiteSpace(noteText) ? string.Empty : noteText,
                    BsYear = day.BsYear,
                    BsMonth = day.BsMonth,
                    BsDay = day.BsDay
                });
            }

            foreach (var note in notes.Where(item => Results.All(result => $"{result.BsYear}-{result.BsMonth}-{result.BsDay}" != item.Key)))
            {
                var parts = note.Key.Split('-');
                Results.Add(new SearchResultViewModel
                {
                    Title = "Personal note",
                    Subtitle = note.Key,
                    NotePreview = note.Value,
                    BsYear = int.Parse(parts[0]),
                    BsMonth = int.Parse(parts[1]),
                    BsDay = int.Parse(parts[2])
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task OpenResultAsync(object? parameter)
    {
        if (parameter is not SearchResultViewModel result)
        {
            return;
        }

        await ((App)Microsoft.UI.Xaml.Application.Current).OpenMainWindowForDateAsync(result.BsYear, result.BsMonth, result.BsDay);
    }
}