namespace OpenPatro.ViewModels;

public sealed class SearchResultViewModel
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string NotePreview { get; init; }

    public required int BsYear { get; init; }

    public required int BsMonth { get; init; }

    public required int BsDay { get; init; }
}