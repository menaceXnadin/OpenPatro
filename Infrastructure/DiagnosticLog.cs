using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using OpenPatro.Services;

namespace OpenPatro.Infrastructure;

/// <summary>
/// Size-capped diagnostic log under the app's data folder (never the Desktop).
/// <see cref="Error"/> is written in every build; <see cref="Trace"/> calls are
/// compiled out of Release builds entirely (arguments are not even evaluated).
/// </summary>
public static class DiagnosticLog
{
    private const long MaxFileBytes = 1024 * 1024; // rotate at 1 MB, keep one previous file
    private static readonly object Gate = new();
    private static string? _logPath;

    public static string LogPath => _logPath ??= ResolveLogPath();

    public static void Error(string message) => Write("ERROR", message);

    [Conditional("DEBUG")]
    public static void Trace(string message) => Write("TRACE", message);

    private static void Write(string level, string message)
    {
        try
        {
            var entry = $"[{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}] {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                var path = LogPath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }

                File.AppendAllText(path, entry);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static string ResolveLogPath()
    {
        string folder;
        try
        {
            // Tests point this elsewhere so they never write into the user's real log.
            folder = Environment.GetEnvironmentVariable("OPENPATRO_LOG_DIR") is { Length: > 0 } overrideDir
                ? overrideDir
                : Path.Combine(ApplicationPaths.ResolveLocalFolderPath(), "logs");
        }
        catch
        {
            folder = Path.Combine(Path.GetTempPath(), "OpenPatro", "logs");
        }

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "openpatro.log");
    }
}
