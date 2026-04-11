using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenPatro.Services;

public class ApplicationPaths
{
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, string? packageFullName);

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
        var localFolderPath = IsRunningAsPackagedApp()
            ? Windows.Storage.ApplicationData.Current.LocalFolder.Path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenPatro");

        Directory.CreateDirectory(localFolderPath);
        return new ApplicationPaths(localFolderPath);
    }

    private static bool IsRunningAsPackagedApp()
    {
        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return result != AppModelErrorNoPackage;
    }
}