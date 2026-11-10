using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OpenPatro.Services;

public sealed class DatabaseBootstrapper
{
    private readonly ApplicationPaths _paths;

    public DatabaseBootstrapper(ApplicationPaths paths)
    {
        _paths = paths;
    }

    public async Task InitializeAsync()
    {
        var localCalendarExists = File.Exists(_paths.CalendarDatabasePath);
        var bundledCalendarExists = File.Exists(_paths.BundledCalendarDatabasePath);
        var bundledCalendarLength = bundledCalendarExists ? new FileInfo(_paths.BundledCalendarDatabasePath).Length : 0;
        var localCalendarHasData = localCalendarExists && await HasAnyMonthDataAsync(_paths.CalendarDatabasePath);

        var shouldRestoreFromBundle = bundledCalendarExists && bundledCalendarLength > 0 && !localCalendarHasData;
        if (shouldRestoreFromBundle)
        {
            // Delete WAL/SHM files that could lock the database from a previous run.
            TryDeleteFile(_paths.CalendarDatabasePath + "-wal");
            TryDeleteFile(_paths.CalendarDatabasePath + "-shm");
            TryDeleteFile(_paths.CalendarDatabasePath);

            try
            {
                File.Copy(_paths.BundledCalendarDatabasePath, _paths.CalendarDatabasePath, overwrite: true);
            }
            catch
            {
                // Copy failed – continue with an empty database.
            }
        }

        if (!File.Exists(_paths.CalendarDatabasePath))
        {
            using var file = File.Create(_paths.CalendarDatabasePath);
        }

        if (!File.Exists(_paths.UserDatabasePath))
        {
            using var file = File.Create(_paths.UserDatabasePath);
        }

        await EnsureSchemaWithRecoveryAsync(_paths.CalendarDatabasePath, EnsureCalendarSchemaAsync, restoreFromBundle: true);
        await EnsureSchemaWithRecoveryAsync(_paths.UserDatabasePath, EnsureUserSchemaAsync, restoreFromBundle: false);
    }

    /// <summary>
    /// Runs a schema step; if the file is not a usable SQLite database (corrupt or
    /// replaced), moves it aside as <c>*.corrupt-&lt;timestamp&gt;</c>, recreates it
    /// (from the bundled seed for the calendar) and retries once. Without this a
    /// damaged user.db made startup fail on every launch.
    /// </summary>
    private async Task EnsureSchemaWithRecoveryAsync(string databasePath, Func<Task> ensureSchema, bool restoreFromBundle)
    {
        try
        {
            await ensureSchema();
            return;
        }
        catch (SqliteException ex) when (IsUnusableDatabase(ex))
        {
            OpenPatro.Infrastructure.DiagnosticLog.Error($"Database '{databasePath}' is unusable ({ex.Message}); recreating it.");
        }

        SqliteConnection.ClearAllPools();
        var suffix = ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        TryMoveFile(databasePath, databasePath + suffix);
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");

        var restored = false;
        if (restoreFromBundle && File.Exists(_paths.BundledCalendarDatabasePath))
        {
            try
            {
                File.Copy(_paths.BundledCalendarDatabasePath, databasePath, overwrite: true);
                restored = true;
            }
            catch
            {
                // Fall back to an empty database below.
            }
        }

        if (!restored)
        {
            using var file = File.Create(databasePath);
        }

        await ensureSchema();
    }

    private static bool IsUnusableDatabase(SqliteException ex)
    {
        // SQLITE_CORRUPT (11) and SQLITE_NOTADB (26).
        return ex.SqliteErrorCode is 11 or 26;
    }

    private static void TryMoveFile(string from, string to)
    {
        try
        {
            if (File.Exists(from))
            {
                File.Move(from, to, overwrite: true);
            }
        }
        catch
        {
            TryDeleteFile(from);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static async Task<bool> HasAnyMonthDataAsync(string databasePath)
    {
        try
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM CalendarMonths LIMIT 1);";
            var result = (long)(await command.ExecuteScalarAsync() ?? 0L);
            return result == 1L;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureCalendarSchemaAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.CalendarDatabasePath}");
        await connection.OpenAsync();

        var sql = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS CalendarMonths (
                BsYear INTEGER NOT NULL,
                BsMonth INTEGER NOT NULL,
                TitleNepali TEXT NOT NULL,
                TitleEnglish TEXT NOT NULL,
                FirstAdDateIso TEXT NOT NULL,
                LastAdDateIso TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (BsYear, BsMonth)
            );

            CREATE TABLE IF NOT EXISTS CalendarDays (
                BsYear INTEGER NOT NULL,
                BsMonth INTEGER NOT NULL,
                BsDay INTEGER NOT NULL,
                BsDayText TEXT NOT NULL,
                BsMonthName TEXT NOT NULL,
                BsFullDate TEXT NOT NULL,
                NepaliWeekday TEXT NOT NULL,
                AdDateIso TEXT NOT NULL,
                AdDateText TEXT NOT NULL,
                EventSummary TEXT NOT NULL,
                Tithi TEXT NOT NULL,
                LunarText TEXT NOT NULL,
                Panchanga TEXT NOT NULL,
                DetailsPath TEXT NOT NULL,
                IsHoliday INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (BsYear, BsMonth, BsDay)
            );

            CREATE INDEX IF NOT EXISTS IX_CalendarDays_AdDateIso ON CalendarDays (AdDateIso);
            CREATE INDEX IF NOT EXISTS IX_CalendarDays_EventSummary ON CalendarDays (EventSummary);
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureUserSchemaAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.UserDatabasePath}");
        await connection.OpenAsync();

        var sql = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS DayNotes (
                BsYear INTEGER NOT NULL,
                BsMonth INTEGER NOT NULL,
                BsDay INTEGER NOT NULL,
                NoteText TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (BsYear, BsMonth, BsDay)
            );
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}