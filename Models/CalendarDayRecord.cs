namespace OpenPatro.Models;

public sealed class CalendarDayRecord
{
    public required int BsYear { get; init; }

    public required int BsMonth { get; init; }

    public required int BsDay { get; init; }

    public required string BsDayText { get; init; }

    public required string BsMonthName { get; init; }

    public required string BsFullDate { get; init; }

    public required string NepaliWeekday { get; init; }

    public required string AdDateIso { get; init; }

    public required string AdDateText { get; init; }

    public required string EventSummary { get; init; }

    public required string Tithi { get; init; }

    public required string LunarText { get; init; }

    public required string Panchanga { get; init; }

    public required string DetailsPath { get; init; }

    public required bool IsHoliday { get; init; }
}