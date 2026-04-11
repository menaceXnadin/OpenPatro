using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using OpenPatro.Infrastructure;
using OpenPatro.Services;

namespace OpenPatro.ViewModels;

public sealed class SearchViewModel : BindableBase
{
    private readonly AppServices _services;
    private string _query = string.Empty;
    private bool _isBusy;
    private string _statusMessage = "Type to search by event, tithi, date, or note.";
    private CancellationTokenSource? _queryDebounceCts;

    public SearchViewModel(AppServices services)
    {
        _services = services;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(Query) && !IsBusy);
        OpenResultCommand = new AsyncRelayCommand(OpenResultAsync);
        Results.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(HasResults));
            RaisePropertyChanged(nameof(ShowStatusMessage));
            RaisePropertyChanged(nameof(StatusVisibility));
        };
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
                ScheduleLiveSearch();
            }
        }
    }

    public bool HasResults => Results.Count > 0;

    public bool ShowStatusMessage => IsBusy || !HasResults;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Visibility StatusVisibility => ShowStatusMessage ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(ShowStatusMessage));
                RaisePropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public async Task SearchAsync()
    {
        var trimmedQuery = Query.Trim();
        Query = trimmedQuery;

        if (trimmedQuery.Length == 0)
        {
            Results.Clear();
            StatusMessage = "Type to search by event, tithi, date, or note.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Searching...";
        try
        {
            Results.Clear();
            var days = await _services.CalendarRepository.SearchAsync(trimmedQuery);
            var notes = await _services.UserRepository.SearchNotesAsync(trimmedQuery);

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

            if (Results.Count == 0)
            {
                StatusMessage = "No results found.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ScheduleLiveSearch()
    {
        _queryDebounceCts?.Cancel();

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            StatusMessage = "Type to search by event, tithi, date, or note.";
            return;
        }

        var cts = new CancellationTokenSource();
        _queryDebounceCts = cts;
        _ = RunDebouncedSearchAsync(cts);
    }

    private async Task RunDebouncedSearchAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(300, cts.Token);
            if (!cts.Token.IsCancellationRequested && SearchCommand.CanExecute(null))
            {
                SearchCommand.Execute(null);
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (ReferenceEquals(_queryDebounceCts, cts))
            {
                _queryDebounceCts = null;
            }

            cts.Dispose();
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