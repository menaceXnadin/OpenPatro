using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OpenPatro.Services;

public sealed class UserRepository
{
    private readonly ApplicationPaths _paths;

    public UserRepository(ApplicationPaths paths)
    {
        _paths = paths;
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.UserDatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return (string?)await command.ExecuteScalarAsync();
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.UserDatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetNoteAsync(int year, int month, int day)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.UserDatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT NoteText FROM DayNotes WHERE BsYear = $year AND BsMonth = $month AND BsDay = $day;";
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$month", month);
        command.Parameters.AddWithValue("$day", day);
        return (string?)await command.ExecuteScalarAsync();
    }

    public async Task SetNoteAsync(int year, int month, int day, string noteText)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.UserDatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DayNotes (BsYear, BsMonth, BsDay, NoteText, UpdatedAtUtc)
            VALUES ($year, $month, $day, $noteText, $updatedAtUtc)
            ON CONFLICT(BsYear, BsMonth, BsDay) DO UPDATE SET
                NoteText = excluded.NoteText,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$month", month);
        command.Parameters.AddWithValue("$day", day);
        command.Parameters.AddWithValue("$noteText", noteText);
        command.Parameters.AddWithValue("$updatedAtUtc", System.DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyDictionary<string, string>> SearchNotesAsync(string query)
    {
        var results = new Dictionary<string, string>();
        await using var connection = new SqliteConnection($"Data Source={_paths.UserDatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BsYear, BsMonth, BsDay, NoteText
            FROM DayNotes
            WHERE NoteText LIKE $query
            ORDER BY UpdatedAtUtc DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("$query", $"%{query}%");

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results[$"{reader.GetInt32(0)}-{reader.GetInt32(1)}-{reader.GetInt32(2)}"] = reader.GetString(3);
        }

        return results;
    }
}