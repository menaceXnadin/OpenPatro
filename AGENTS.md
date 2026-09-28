# AGENTS.md — OpenPatro

Single-project WinUI 3 / Windows App SDK desktop app (.NET 8). No tests, no lint, no CI (`.github/workflows/` is empty). Windows-only: build/run on Windows 10/11; plain `dotnet build` defaults to **x64** (csproj forces `Platform=x64` when unspecified).

## Build / run / publish

- Open `OpenPatro.slnx` in Visual Studio (recommended) or:
  - `dotnet build -c Debug` (unpackaged dev loop; uses `Properties/launchSettings.json` profiles)
  - `dotnet build -c Release -p:Platform=x64`
- `global.json` pins the .NET 8 SDK (matches the `net8.0` target; newer SDKs break the WinAppSDK XAML compiler).
- Publish profiles in `Properties/PublishProfiles/`: `win-*.pubxml` = self-contained unpackaged (`WindowsPackageType=None`); `msix-*.pubxml` = sideload MSIX. Don't mix them up.
- CLI quirks: target `OpenPatro.csproj` explicitly — bare `dotnet` commands pick up `OpenPatro.slnx` and fail when a RID is passed (`NETSDK1134`). `win-x64.pubxml` sets `PublishReadyToRun=false` to keep the self-contained output (which ships inside the installer) smaller.
- `dotnet publish` with the .NET 10 SDK fails in the WinAppSDK XAML compiler (`token recognition error`, MSB3077) even though `dotnet build` succeeds. Workaround: build first, then `dotnet publish OpenPatro.csproj -c Release -p:Platform=x64 /p:PublishProfile=win-x64.pubxml --no-build`. VS publish is unaffected.
- Do not touch signing assets in repo root (`OpenPatro_PackageSigning.*`, `OpenPatro_TemporaryKey.pfx`) or `Package.appxmanifest` identity/publisher.
- `bin/`, `obj/`, `.vs/`, `*.user`, `*.log`, `installer/Output/`, root `calendar.db` / `failed.txt` are gitignored. `Assets/Data/calendar.db` (bundled seed) **is** committed — keep it that way.

## Installer (Inno Setup)

- `installer/OpenPatro.iss` builds the user-facing `Setup.exe` (per-user, no admin/UAC, Start Menu entry, working uninstaller). Requires publish output first:
  1. `dotnet publish OpenPatro.csproj -c Release -p:Platform=x64 /p:PublishProfile=win-x64.pubxml` (+ `--no-build`, see quirk above)
  2. `ISCC installer\OpenPatro.iss` → `installer\Output\OpenPatroSetup-<version>-x64.exe` (gitignored)
- Inno 6 installs via `winget install --id JRSoftware.InnoSetup -e` (lands in `%LocalAppData%\Programs\Inno Setup 6`, not on PATH).
- The script kills a running `OpenPatro.exe` before install/uninstall (tray app = locked files otherwise) and removes the HKCU Run startup value on uninstall. Startup itself is app-managed (`Services/StartupService.cs`), not installer-managed. User data (`%LocalAppData%\OpenPatro\*.db`) is preserved on uninstall.
- Bump `#MyAppVersion` in the .iss together with the `Package.appxmanifest` version.

## Architecture (read before changing)

- Entry: `Program.cs` is the real `Main` (`DISABLE_XAML_GENERATED_MAIN` is set in csproj). Single-instance via `AppInstance.FindOrRegisterForKey("OpenPatro-SingleInstance")` — second launch redirects activation to the first and exits.
- Composition root: `Infrastructure/AppServices.cs` (`AppServices.CreateAsync()`). `ViewModels/MainViewModel.cs` owns the 9 section VMs (Calendar, Search, StockMarket, Settings, Rashifal, ShubhaSait, DateConverter, Bullion, Forex).
- `App.xaml.cs` owns window/tray lifecycle: `MainWindow` close **hides to tray** (not exit; exit only via tray menu / `ExitApplication`). `DispatcherKeepaliveWindow.cs` must stay alive while hidden or the WinUI dispatcher shuts down. Tray icon via `H.NotifyIcon.WinUI` (`TaskbarIcon`). `--startup` launch flag = tray-only, no main window. Diag log: `~/Desktop/openpatro-diag.log`.
- Conventions: `ImplicitUsings` is **disabled** (add explicit `using`s), `Nullable` is **enabled**. No DI container — wire new services through `AppServices`.

## Data / calendar DB (gotcha-prone)

- Two SQLite DBs (`Microsoft.Data.Sqlite`): `calendar.db` + `user.db` in `%LocalAppData%\OpenPatro` when unpackaged, or package `LocalFolder` when MSIX-packaged (`Services/ApplicationPaths.cs`). Schema is created at runtime by `Services/DatabaseBootstrapper.cs` — it also restores `calendar.db` from the bundle if local is empty.
- If you change calendar schema, update **both** `DatabaseBootstrapper.EnsureCalendarSchemaAsync` and `build_calendar_db.py::ensure_schema` or they will diverge.
- Rebuilding the bundled seed: `python build_calendar_db.py --start-year 2000 --end-year 2085 --output calendar.db` (scrapes hamropatro.com; slow, writes root `calendar.db` + `failed.txt`), then copy the result to `Assets/Data/calendar.db`. In-app alternative: run with `--seed-bundled-db` (`Services/BundledCalendarSeedService.cs`, covers BS 2000–2089).
