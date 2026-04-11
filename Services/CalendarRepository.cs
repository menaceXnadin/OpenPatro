using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OpenPatro.Models;

namespace OpenPatro.Services;

public sealed class CalendarRepository
{
    private readonly ApplicationPaths _paths;
    private readonly NepalTimeService _clock;

    public CalendarRepository(ApplicationPaths paths, NepalTimeService clock)
    {
        _paths = paths;
        _clock = clock;
    }

    public async Task UpsertMonthAsync(ParsedCalendarMonth month)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var actualDays = month.Days.FindAll(day => day.BsYear == month.RequestedYear && day.BsMonth == month.RequestedMonth);
        if (actualDays.Count > 0)
        {
            var monthCommand = connection.CreateCommand();
            monthCommand.Transaction = transaction;
            monthCommand.CommandText = """
                INSERT INTO CalendarMonths (BsYear, BsMonth, TitleNepali, TitleEnglish, FirstAdDateIso, LastAdDateIso, UpdatedAtUtc)
                VALUES ($year, $month, $titleNepali, $titleEnglish, $firstAd, $lastAd, $updatedAtUtc)
                ON CONFLICT(BsYear, BsMonth) DO UPDATE SET
                    TitleNepali = excluded.TitleNepali,
                    TitleEnglish = excluded.TitleEnglish,
                    FirstAdDateIso = excluded.FirstAdDateIso,
                    LastAdDateIso = excluded.LastAdDateIso,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            monthCommand.Parameters.AddWithValue("$year", month.RequestedYear);
            monthCommand.Parameters.AddWithValue("$month", month.RequestedMonth);
            monthCommand.Parameters.AddWithValue("$titleNepali", month.TitleNepali);
            monthCommand.Parameters.AddWithValue("$titleEnglish", month.TitleEnglish);
            monthCommand.Parameters.AddWithValue("$firstAd", actualDays[0].AdDateIso);
            monthCommand.Parameters.AddWithValue("$lastAd", actualDays[^1].AdDateIso);
            monthCommand.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await monthCommand.ExecuteNonQueryAsync();
        }

        foreach (var day in month.Days)
        {
            var dayCommand = connection.CreateCommand();
            dayCommand.Transaction = transaction;
            dayCommand.CommandText = """
                INSERT INTO CalendarDays (
                    BsYear, BsMonth, BsDay, BsDayText, BsMonthName, BsFullDate, NepaliWeekday,
                    AdDateIso, AdDateText, EventSummary, Tithi, LunarText, Panchanga, DetailsPath,
                    IsHoliday, UpdatedAtUtc)
                VALUES (
                    $bsYear, $bsMonth, $bsDay, $bsDayText, $bsMonthName, $bsFullDate, $nepaliWeekday,
                    $adDateIso, $adDateText, $eventSummary, $tithi, $lunarText, $panchanga, $detailsPath,
                    $isHoliday, $updatedAtUtc)
                ON CONFLICT(BsYear, BsMonth, BsDay) DO UPDATE SET
                    BsDayText = excluded.BsDayText,
                    BsMonthName = excluded.BsMonthName,
                    BsFullDate = excluded.BsFullDate,
                    NepaliWeekday = excluded.NepaliWeekday,
                    AdDateIso = excluded.AdDateIso,
                    AdDateText = excluded.AdDateText,
                    EventSummary = excluded.EventSummary,
                    Tithi = excluded.Tithi,
                    LunarText = excluded.LunarText,
                    Panchanga = excluded.Panchanga,
                    DetailsPath = excluded.DetailsPath,
                    IsHoliday = excluded.IsHoliday,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            dayCommand.Parameters.AddWithValue("$bsYear", day.BsYear);
            dayCommand.Parameters.AddWithValue("$bsMonth", day.BsMonth);
            dayCommand.Parameters.AddWithValue("$bsDay", day.BsDay);
            dayCommand.Parameters.AddWithValue("$bsDayText", day.BsDayText);
            dayCommand.Parameters.AddWithValue("$bsMonthName", day.BsMonthName);
            dayCommand.Parameters.AddWithValue("$bsFullDate", day.BsFullDate);
            dayCommand.Parameters.AddWithValue("$nepaliWeekday", day.NepaliWeekday);
            dayCommand.Parameters.AddWithValue("$adDateIso", day.AdDateIso);
            dayCommand.Parameters.AddWithValue("$adDateText", day.AdDateText);
            dayCommand.Parameters.AddWithValue("$eventSummary", day.EventSummary);
            dayCommand.Parameters.AddWithValue("$tithi", day.Tithi);
            dayCommand.Parameters.AddWithValue("$lunarText", day.LunarText);
            dayCommand.Parameters.AddWithValue("$panchanga", day.Panchanga);
            dayCommand.Parameters.AddWithValue("$detailsPath", day.DetailsPath);
            dayCommand.Parameters.AddWithValue("$isHoliday", day.IsHoliday ? 1 : 0);
            dayCommand.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await dayCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<CalendarMonthRecord?> GetMonthRecordAsync(int year, int month)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, TitleNepali, TitleEnglish, FirstAdDateIso, LastAdDateIso
            FROM CalendarMonths
            WHERE BsYear = $year AND BsMonth = $month;
            """;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$month", month);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new CalendarMonthRecord
        {
            BsYear = reader.GetInt32(0),
            BsMonth = reader.GetInt32(1),
            TitleNepali = reader.GetString(2),
            TitleEnglish = reader.GetString(3),
            FirstAdDateIso = reader.GetString(4),
            LastAdDateIso = reader.GetString(5)
        };
    }

    public async Task<IReadOnlyList<CalendarDayRecord>> GetMonthDaysAsync(int year, int month)
    {
        var results = new List<CalendarDayRecord>();
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, BsDay, BsDayText, BsMonthName, BsFullDate, NepaliWeekday,
                   AdDateIso, AdDateText, EventSummary, Tithi, LunarText, Panchanga, DetailsPath, IsHoliday
            FROM CalendarDays
            WHERE BsYear = $year AND BsMonth = $month
            ORDER BY BsDay;
            """;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$month", month);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(ReadDay(reader));
        }

        return results;
    }

    public async Task<CalendarDayRecord?> GetDayAsync(int year, int month, int day)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, BsDay, BsDayText, BsMonthName, BsFullDate, NepaliWeekday,
                   AdDateIso, AdDateText, EventSummary, Tithi, LunarText, Panchanga, DetailsPath, IsHoliday
            FROM CalendarDays
            WHERE BsYear = $year AND BsMonth = $month AND BsDay = $day;
            """;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$month", month);
        command.Parameters.AddWithValue("$day", day);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDay(reader) : null;
    }

    public async Task<CalendarDayRecord?> GetTodayAsync()
    {
        return await GetByAdDateAsync(_clock.GetNepalToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public async Task<CalendarDayRecord?> GetByAdDateAsync(string adDateIso)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, BsDay, BsDayText, BsMonthName, BsFullDate, NepaliWeekday,
                   AdDateIso, AdDateText, EventSummary, Tithi, LunarText, Panchanga, DetailsPath, IsHoliday
            FROM CalendarDays
            WHERE AdDateIso = $adDateIso;
            """;
        command.Parameters.AddWithValue("$adDateIso", adDateIso);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDay(reader) : null;
    }

    public async Task<IReadOnlyList<CalendarDayRecord>> SearchAsync(string query)
    {
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
        {
            return [];
        }

        var adDateIso = TryNormalizeAdDateIso(trimmedQuery) ?? "__NO_AD_DATE_MATCH__";

        var results = new List<CalendarDayRecord>();
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, BsDay, BsDayText, BsMonthName, BsFullDate, NepaliWeekday,
                   AdDateIso, AdDateText, EventSummary, Tithi, LunarText, Panchanga, DetailsPath, IsHoliday
            FROM CalendarDays
             WHERE EventSummary LIKE $query
             OR Panchanga LIKE $query
             OR Tithi LIKE $query
             OR BsFullDate LIKE $query
             OR AdDateText LIKE $query
             OR AdDateIso = $adDateIso
             OR NepaliWeekday LIKE $query
             OR BsMonthName LIKE $query
            ORDER BY AdDateIso DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("$query", $"%{trimmedQuery}%");
        command.Parameters.AddWithValue("$adDateIso", adDateIso);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(ReadDay(reader));
        }

        return results;
    }

    private static string? TryNormalizeAdDateIso(string query)
    {
        if (DateOnly.TryParseExact(query, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            return isoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateOnly.TryParse(query, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var localDate))
        {
            return localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateOnly.TryParse(query, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariantDate))
        {
            return invariantDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }

    public async Task<bool> HasMonthAsync(int year, int month)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM CalendarMonths WHERE BsYear = $year AND BsMonth = $month);";
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$month", month);
        var result = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return result == 1L;
    }

    public async Task<IReadOnlyList<int>> GetAvailableBsYearsAsync()
    {
        var years = new List<int>();
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT BsYear FROM CalendarMonths ORDER BY BsYear;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            years.Add(reader.GetInt32(0));
        }

        return years;
    }

    public async Task<IReadOnlyList<CalendarMonthRecord>> GetAvailableMonthsForYearAsync(int year)
    {
        var months = new List<CalendarMonthRecord>();
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, TitleNepali, TitleEnglish, FirstAdDateIso, LastAdDateIso
            FROM CalendarMonths
            WHERE BsYear = $year
            ORDER BY BsMonth;
            """;
        command.Parameters.AddWithValue("$year", year);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            months.Add(new CalendarMonthRecord
            {
                BsYear = reader.GetInt32(0),
                BsMonth = reader.GetInt32(1),
                TitleNepali = reader.GetString(2),
                TitleEnglish = reader.GetString(3),
                FirstAdDateIso = reader.GetString(4),
                LastAdDateIso = reader.GetString(5)
            });
        }

        return months;
    }

    private static CalendarDayRecord ReadDay(SqliteDataReader reader)
    {
        return new CalendarDayRecord
        {
            BsYear = reader.GetInt32(0),
            BsMonth = reader.GetInt32(1),
            BsDay = reader.GetInt32(2),
            BsDayText = reader.GetString(3),
            BsMonthName = reader.GetString(4),
            BsFullDate = reader.GetString(5),
            NepaliWeekday = reader.GetString(6),
            AdDateIso = reader.GetString(7),
            AdDateText = reader.GetString(8),
            EventSummary = reader.GetString(9),
            Tithi = reader.GetString(10),
            LunarText = reader.GetString(11),
            Panchanga = reader.GetString(12),
            DetailsPath = reader.GetString(13),
            IsHoliday = reader.GetInt32(14) == 1
        };
    }
}