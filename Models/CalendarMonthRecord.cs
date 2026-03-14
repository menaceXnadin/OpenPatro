namespace OpenPatro.Models;

public sealed class CalendarMonthRecord
{
    public required int BsYear { get; init; }

    public required int BsMonth { get; init; }

    public required string TitleNepali { get; init; }

    public required string TitleEnglish { get; init; }

    public required string FirstAdDateIso { get; init; }

    public required string LastAdDateIso { get; init; }
}