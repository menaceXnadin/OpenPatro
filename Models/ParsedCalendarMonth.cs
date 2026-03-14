using System.Collections.Generic;

namespace OpenPatro.Models;

public sealed class ParsedCalendarMonth
{
    public required int RequestedYear { get; init; }

    public required int RequestedMonth { get; init; }

    public required string TitleNepali { get; init; }

    public required string TitleEnglish { get; init; }

    public required List<CalendarDayRecord> Days { get; init; }
}