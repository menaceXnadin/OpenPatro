using System;
using System.IO;

namespace OpenPatro.Services;

public class ApplicationPaths
{
    protected internal ApplicationPaths(string localFolderPath)
    {
        LocalFolderPath = localFolderPath;
        CalendarDatabasePath = Path.Combine(localFolderPath, "calendar.db");
        UserDatabasePath = Path.Combine(localFolderPath, "user.db");
        BundledCalendarDatabasePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "calendar.db");
        BundledFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NotoSansDevanagari.ttf");
    }

    public string LocalFolderPath { get; protected set; }

    public string CalendarDatabasePath { get; protected set; }

    public string UserDatabasePath { get; protected set; }

    public string BundledCalendarDatabasePath { get; protected set; }

    public string BundledFontPath { get; protected set; }

    public static ApplicationPaths Create()
    {
        string localFolderPath;

        // Try the packaged (MSIX) path first.
        try
        {
            localFolderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            // Not running as a packaged app – fall back to a folder next to the exe,
            // or to %LocalAppData%\OpenPatro when that is not writable.
            localFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenPatro");
        }

        Directory.CreateDirectory(localFolderPath);
        return new ApplicationPaths(localFolderPath);
    }
}