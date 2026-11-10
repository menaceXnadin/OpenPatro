# OpenPatro — Codebase Audit

**Date:** 2026-11-10 · **Commit:** `2542d73` (master, release 1.0.1) · **Scope:** this was a read-only audit, so no source, XAML, project or asset files were changed.

**How it was verified**
- `dotnet build OpenPatro.csproj -c Debug -p:Platform=x64 --no-incremental` succeeds with **0 warnings and 0 errors** (SDK 8.0.425).
- Every file listed by `git ls-files` (89 files, ~14.2k lines of C#/XAML) was read.
- `dotnet list package --vulnerable --include-transitive` and `--outdated` were run.
- A throwaway xUnit project was built **outside the repo** (session scratchpad). It references the built `OpenPatro.dll` and reproduces bugs without a UI. Its tests are reproduced in [Appendix A](#appendix-a--failing-tests) so they can be added to a real test project.
- The app's own diagnostic log (`~/Desktop/openpatro-diag.log`: 10,060 lines, 2026-03-13 → 2026-11-10) was mined for real runtime exceptions.
- The app was **not launched** during the audit. Launching it writes to the registry (see Bug 1) and to `user.db`. Bugs that need the GUI were confirmed by tracing a deterministic code path, and this is stated for each one.

> **Fix status (branch `fix/audit-1.0.2`).** Fixed:
> - All 11 confirmed bugs.
> - P1–P9 and P11.
> - The vulnerable SQLite (pinned `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14).
> - C2, C3, C5 (partial) and C8.
> - Dead code.
> - `tests/OpenPatro.Tests` (21 passing tests) and CI.
>
> Not done in this pass:
> - P10 is inherent to low-level hooks; the popup's deactivate auto-hide stays the fallback.
> - H.NotifyIcon 2.4.1 requires .NET 10.
> - The architecture items (A1–A5, W1–W5, C4, C10 seed-merge) and the .NET 10 / WinAppSDK 2.x move remain roadmap steps 5–10.

---

## 1. Summary

**Overall health: Fair (C+).** The app works and has some careful engineering, especially around the tray and window lifecycle and SQL hygiene. It has the typical shape of early AI-assisted code, though:
- a 2,267-line code-behind god class
- no DI and no tests
- exceptions swallowed almost everywhere
- several hand-rolled MVVM primitives
- view models that create WinUI brushes

There are 11 confirmed bugs: 2 High, 3 Medium and 6 Low.

**Tech stack (confirmed from `OpenPatro.csproj`)**

| Item | Value |
|---|---|
| UI | WinUI 3, Windows App SDK **1.8.260209005** (`UseWinUI=true`), single-project MSIX tooling |
| Runtime | **.NET 8** (`net8.0-windows10.0.19041.0`, min 10.0.17763), x86/x64/ARM64 |
| Packages | H.NotifyIcon.WinUI 2.3.0, Microsoft.Data.Sqlite 8.0.13, System.Formats.Nrbf 9.0.1, Microsoft.Windows.SDK.BuildTools 10.0.26100.7705 |
| Settings | `Nullable` enabled, `ImplicitUsings` disabled, custom `Program.Main` (`DISABLE_XAML_GENERATED_MAIN`), `RequestedTheme="Dark"` |
| Tests / CI | None / none |

**Top 5 priorities**
1. **Stop overriding the user's "Start with Windows" choice.** The app re-enables autostart on every launch (Bug 1).
2. **Fix the Live Market VOLUME/TURNOVER swap.** The app shows wrong financial data (Bug 2).
3. **Make startup failures visible and fix logging.**
   - A corrupt `user.db` leaves an invisible process that cannot be re-opened (Bug 3).
   - The diagnostic log grows forever on the user's Desktop (Bug 5).
   - `UnhandledException` swallows everything.
4. **Patch dependencies and plan the runtime upgrade.**
   - The transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.6` has a **High** advisory (GHSA-2m69-gcr7-jv3q).
   - **.NET 8 reaches end of support today (2026-11-10).**
5. **Create a test project, then break up `MainWindow.xaml.cs`.** Adopt CommunityToolkit.Mvvm and introduce DI incrementally (see roadmap).

---

## 2. Architecture overview

```
Program.Main ── AppInstance single-instance (key "OpenPatro-SingleInstance")
   └─ App (App.xaml.cs, 955 lines): window + tray lifecycle
        ├─ AppServices.CreateAsync()           ← composition root, all `new`, no interfaces
        │    ├─ DatabaseBootstrapper → calendar.db / user.db (%LocalAppData%\OpenPatro or package LocalFolder)
        │    ├─ CalendarRepository / UserRepository   (raw Microsoft.Data.Sqlite, connection per call)
        │    ├─ HamroPatroClient + HamroPatroParser     (scrapes hamropatro.com month pages)
        │    ├─ NepaliPatroApiClient / ForexClient / BullionClient / ShareHubNepseClient (one HttpClient each)
        │    └─ CalendarSyncService, StartupService (HKCU Run), WindowBoundsService, NepalTimeService …
        ├─ MainViewModel ── owns 9 section VMs (Calendar, Search*, StockMarket, Settings, Rashifal,
        │                    ShubhaSait, DateConverter, Bullion, Forex)      *Search has no UI
        ├─ MainWindow (MainWindow.xaml 2,641 lines + .xaml.cs 2,267 lines)
        │    └─ all 8 sections live in one XAML file; visibility toggled from code-behind
        ├─ TrayPopupWindow + Controls/TrayCalendarPopup (second, independent CalendarViewModel)
        ├─ DispatcherKeepaliveWindow (1×1 transparent window that keeps the dispatcher alive while hidden)
        └─ H.NotifyIcon TaskbarIcon (icon glyph rendered by TrayIconGlyphFactory every minute)
```

- **Navigation.** There is no `Frame` or page navigation. Eight `ToggleButton`s set `MainViewModel.SelectedSection`. Code-behind then sets `Visibility` on eight `Grid`s and re-syncs `IsChecked` (`MainWindow.xaml.cs:452-530`, `772-812`).
- **Data flow.**
  - Calendar: SQLite → `CalendarRepository` → `CalendarViewModel.LoadMonthAsync`, which fills a 42-cell `BulkObservableCollection`. Missing months are fetched from hamropatro.com and upserted.
  - Market sections: VM → HTTP client → model → row-VM strings, all in memory with no caching.
- **Window lifecycle.** Closing the main window hides it to the tray (`App.xaml.cs:881-912`). The app exits only via the tray menu (`ExitApplication`, which ends in `Environment.Exit(0)`).
- **Threading.** The VMs run on the UI thread through `DispatcherQueueSynchronizationContext` (`Program.cs:45-47`), with async I/O. Background callbacks (the date watcher timer and AppInstance activation) are marshalled with `DispatcherQueue.TryEnqueue` (`App.xaml.cs:114-131`, `278-297`).

---

## 3. Best-practice findings

Severity reflects impact on maintainability, correctness risk or users.

### 3.1 Architecture

**A1 — `MainWindow.xaml.cs` is a god class (High)**
- **Location:** `MainWindow.xaml.cs` (2,267 lines), `MainWindow.xaml` (2,641 lines).
- **What's wrong:** One window holds 8 feature sections and all of the following:
  - navigation
  - Win32 interop
  - window persistence
  - three hand-written chart renderers
  - Bullion pagination UI state
  - sort-icon state for 3 tables
  - date-converter input parsing
  - Shubha Sait picker syncing
  - support-link launching
  - 19 `FindName()` lookups and 6 `_suppress…Events` re-entrancy flags (`:36-41`)
- **Why it matters:** Any change risks unrelated sections. None of this logic can be tested, and merge conflicts are guaranteed.
- **Recommended approach:** Give each section its own `UserControl` (`CalendarView`, `StockMarketView`, …) bound to its VM. Move chart drawing into a reusable `LineChart` control. Express UI state (`IsEnabled`, labels, selections) as bindings instead of imperative `FindName` writes.

**A2 — Logic leaks into code-behind, breaking MVVM (High)**
- **Locations:**
  - `MainWindow.xaml.cs:1408-1479` (date-converter input validation and composition)
  - `:325-376` (Bullion paging buttons enabled/disabled by hand)
  - `:591-724` (sort-icon glyphs and tab states)
  - `:1974-2143` (chart data shaping, downsampling and price parsing)
  - `:1527-1572` (hardcoded external links)
- **Why it matters:** The VMs don't own their state. For example, `BullionViewModel.ItemsPerPage` and the picker disagree (Bug 8), and the converter's default date is decided in the view (Bug 4).
- **Recommended approach:** Move these into the VMs as properties and commands. Bind `ComboBox.SelectedItem` and `ToggleButton.IsChecked` TwoWay.

**A3 — No DI; service locator via `Application.Current` (Medium)**
- **Locations:**
  - `Infrastructure/AppServices.cs:68-90` (all services created with `new`, concrete types, no interfaces)
  - VMs reach back into the app: `CalendarViewModel.cs:51`, `SearchViewModel.cs:201`, `SettingsViewModel.cs:55`, `MainWindow.xaml.cs:95, 888, 933`
  - `AppServices` is passed whole to every VM, which can then use every service.
- **Why it matters:** VMs can't be unit-tested without reflection tricks (Appendix A has to inject `HttpClient`s by reflection), and the hidden dependencies make lifetimes unclear.
- **Recommended approach:** Use `Microsoft.Extensions.DependencyInjection` with `IHttpClientFactory`. Define interfaces for the repositories and clients, inject only what each VM uses, and replace `App` calls with an `IWindowService` or messenger.

**A4 — Hand-rolled MVVM primitives, duplicated (Medium)**
- **Locations:** `Infrastructure/BindableBase.cs`, `RelayCommand.cs`, `AsyncRelayCommand.cs`, and a second `AsyncRelayCommand<T>` hidden in `ViewModels/ForexViewModel.cs:863-896`.
- **What's wrong:**
  - Both async commands are `async void` and swallow exceptions into `Debug.WriteLine` (`AsyncRelayCommand.cs:42-45`), which is invisible in Release.
  - Callers cast `ICommand` back to the concrete type to raise CanExecuteChanged, e.g. `((AsyncRelayCommand)RefreshCommand)`, a pattern used ~20 times.
- **Recommended approach:** Use **CommunityToolkit.Mvvm**: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, and `IAsyncRelayCommand` with `ExecutionTask` and exception surfacing.

**A5 — DRY violations (Medium)**
- Three nearly identical chart renderers: `DrawForexChart`, `DrawForexTrendChart` and `DrawBullionChart` (`MainWindow.xaml.cs:1733-1888`, `2145-2238`).
- Five copies of `SafeInitializeAsync` that differ only by type (`MainViewModel.cs:106-129`).
- The same `IsBusy`/`ErrorMessage`/`RefreshAsync`/timeout/try-catch skeleton in 6 VMs.
- `ReplaceCollection` wrappers that only call `ReplaceAll` (`StockMarketViewModel.cs:565`, `ForexViewModel.cs:854`, `BullionViewModel.cs:616`).
- Three copies of the reflection `ShowNotification` invoker (`App.xaml.cs:624-660`, `TrayNotificationService.cs:28-49`).
- Nepali month-name tables in 4 places with **different spellings**: `कार्तिक` vs `कात्तिक`, `फागुन` vs `फाल्गुन`/`फाल्गुण`, `पुष` vs `पौष` (`CalendarViewModel.cs:37-41`, `MainWindow.xaml.cs:1355`, `ShubhaSaitViewModel.cs:38-52`, `HamroPatroParser.cs:19`).
- **Recommended approach:** A `SectionViewModelBase` (load/refresh/error), one chart control, and one `NepaliCalendarNames` static.

**A6 — Dead code (Low)**
- `SearchViewModel` is constructed and wired, but there is no Search UI. `IsSearchVisible => false` (`MainViewModel.cs:68`), and `SelectedSection` silently remaps Search to Calendar (`:46-49`). `SearchQueryBox_KeyDown` and `SearchResultsView_ItemClick` in `MainWindow.xaml.cs:751-768` reference no XAML element.
- `ShowTodayFromTrayAsync` (`App.xaml.cs:566-622`) has no callers.
- `TrayNotificationService` / `ShowPinHintOnceAsync` is never called.
- `CalendarRepository.GetDayAsync`, `ShubhaSaitViewModel.GetAuspiciousDays` and `AuspiciousDays` are unused, and `System.Formats.Nrbf` is referenced but never used in code.
- **Recommended approach:** Delete them, or re-expose Search, which already works and would make notes searchable.

### 3.2 WinUI 3 specifics

**W1 — View models create UI objects (Medium)**
- **Locations:**
  - `CalendarDayCellViewModel.cs:45-115` builds `SolidColorBrush`, `Thickness` and `CornerRadius`, allocating a new brush on **every getter call** (6 brush properties × 42 cells × every selection change).
  - `CalendarViewModel.cs:155,161` returns `Visibility`.
  - `ForexViewModel.cs:325, 590-603` creates `BitmapImage`.
  - The market row VMs expose hex color strings, e.g. `StockMarketViewModel.cs:910, 959-985, 1040-1073`.
- **Why it matters:** These VMs need a WinUI dispatcher to run, so they can't be tested headlessly. Colors bypass theming.
- **Recommended approach:** Expose state (`IsHoliday`, `IsSelected`, `Trend`) and map it to brushes with `ThemeResource` styles, `VisualStateManager` or converters.

**W2 — Binding usage is mixed (Low)**
- **Location:** `MainWindow.xaml` uses 178 `{x:Bind}` and 93 `{Binding}`. `TrayCalendarPopup.xaml` is 100% `{Binding}` through `DataContext` (`TrayCalendarPopup.xaml.cs:14-17`).
- **What's wrong:**
  - Index bindings `MajorIndices[0..3]` (`MainWindow.xaml:678-714`) crashed the app in April. The diag log has 2× `ArgumentOutOfRangeException` in `Update_ViewModel_StockMarket_MajorIndices`. The current fix is to pad with placeholders (`StockMarketViewModel.cs:60-64`).
  - Plain-method "converters" `BoolToVisibility` and `StringToVisibility` on the Window (`MainWindow.xaml.cs:1655-1657`) are used 36 times.
- **Recommended approach:** Use `x:Bind` with `x:DataType` everywhere (compile-time checked). Expose `NepseIndex`, `SensitiveIndex` etc. as named properties instead of indexes. WinUI `x:Bind` already converts `bool` to `Visibility` implicitly.

**W3 — Theming is dark-only and hardcoded (Medium)**
- **Locations:**
  - `App.xaml:7` sets `RequestedTheme="Dark"`.
  - `MainWindow.xaml.cs:1139-1159` forces a dark title bar.
  - 38 hex colors in `MainWindow.xaml` and 10 in `TrayCalendarPopup.xaml`.
  - Hardcoded day-cell palette (`CalendarDayCellViewModel.cs:16-24`) and chart colors (`MainWindow.xaml.cs:1781-1804, 2020-2033`).
- **Why it matters:** The app ignores the user's Windows theme and high-contrast mode. Hardcoded colors fail HC accessibility requirements.
- **Recommended approach:** Define theme dictionaries (Light/Dark/HighContrast) and use `{ThemeResource}` for every color. The 122 existing `ThemeResource` uses are a good start.

**W4 — No accessibility or localization (Medium)**
- **Location:** Zero `AutomationProperties` and zero `x:Uid` in any XAML. There are no `.resw` files. UI strings are hardcoded in XAML and C#, e.g. `App.xaml.cs:484-512` and every `ErrorMessage`.
- **What's wrong:**
  - Icon-only buttons such as the `FontIcon` sort headers have no accessible name.
  - The calendar grid cells have no automation names, so a screen reader announces nothing useful.
  - Keyboard: Enter is handled in the converter and search, but day cells use `ItemClick` only.
- **Recommended approach:** Add `AutomationProperties.Name` to icon buttons and set it per day cell from the full date. Move strings to `Strings/en-US/Resources.resw` (and `ne-NP`).

**W5 — Window management via raw Win32 (Low)**
- **Locations:**
  - Hand subclassing with `SetWindowLongPtr(GWLP_WNDPROC)` for min size and `WM_TIMECHANGE` (`MainWindow.xaml.cs:1163-1205`).
  - Style flipping with `WS_EX_TOOLWINDOW` (`App.xaml.cs:193-214, 902-910`).
  - A low-level global mouse hook (`TrayPopupWindow.cs:205-254`).
- **Why it matters:** It works (the delegate is rooted, which is correct), but it is fragile. Windows App SDK 1.6+ offers `OverlappedPresenter.PreferredMinimumWidth/Height`.
- **Recommended approach:** Replace the min-size WndProc with presenter properties. Keep the WndProc only for `WM_TIMECHANGE`, or use `SystemEvents.TimeChanged`.

### 3.3 C# and .NET practices

**C1 — Exceptions are swallowed and global handling is incomplete (High)**
- **Locations:**
  - `App.xaml.cs:52-56` sets `e.Handled = true` for **every** unhandled XAML exception.
  - No `TaskScheduler.UnobservedTaskException` and no `AppDomain.CurrentDomain.UnhandledException` handler.
  - 27 bare or blanket `catch` blocks, e.g. `MainViewModel.cs:106-129`, `CalendarViewModel.cs:256-259`, `CalendarSyncService.cs:36-39, 64-67`, `WindowBoundsService.cs:69, 89`.
  - Both command classes write only to `Debug`.
- **Evidence:** The diag log records 19 `UnhandledException`s and 7 `FAILED` events, all hidden from the user.
- **Why it matters:** Failures look like "nothing happened". For example, a failed save of a day note (`SaveNoteCommand` → `AsyncRelayCommand` catch) shows no error at all.
- **Recommended approach:**
  - Use a single logger.
  - Show an `InfoBar` for user-initiated failures.
  - Only mark `Handled` for known-recoverable exceptions.
  - Add the two missing global handlers.

**C2 — `async void` outside event handlers, and fire-and-forget tasks (Medium)**
- **Location:** `AsyncRelayCommand.Execute` and `AsyncRelayCommand<T>.Execute` are `async void`. That is tolerable only because they catch everything. About 15 `_ = SomethingAsync()` fire-and-forget calls exist, e.g. `App.xaml.cs:95, 104, 119, 289, 299, 460, 528`, `ForexViewModel.cs:389, 658, 750`, `MainWindow.xaml.cs:447, 491`. Most are guarded internally; `ForexViewModel` trend loads are, but `PersistWindowBoundsAsync` relies on its own catch.
- **What's wrong:**
  - The async event handlers in `MainWindow.xaml.cs` (`:466-530`, `994-1028`, `1321-1329`) await VM calls without `try/finally`. The `_suppress…` flags at `:1006-1009`, `:1024-1027` and `:1325-1328` stay `true` forever if the await throws, which silently disables those pickers.
- **Recommended approach:** Use `try/finally` for the flags and toolkit `IAsyncRelayCommand`. Have one `SafeFireAndForget(this Task, ILogger)` helper.

**C3 — Blocking call on the STA thread (Low)**
- **Location:** `Program.cs:28` calls `RedirectActivationToAsync(...).GetAwaiter().GetResult()` on the `[STAThread]` main thread.
- **Why it matters:** Microsoft's single-instancing sample avoids blocking the STA thread here, because a COM call back into the STA can deadlock.
- **Recommended approach:** Follow the documented pattern: redirect on a worker thread and wait with `CoWaitForMultipleObjects`.

**C4 — Nullable warnings are suppressed with `null!` instead of modelled (Low)**
- **Location:** `App.xaml.cs:41-42` declares `Services` and `MainViewModel` as `= null!`, but both **are** null after a startup failure (see Bug 3). `MainWindow.xaml.cs:95` dereferences them unconditionally.
- **What's wrong:** Nullable is enabled and the build has 0 warnings, which is good, but these two properties defeat it.
- **Recommended approach:** Make startup state explicit, e.g. a `StartupResult` value, or fail fast with a dialog.

**C5 — Resource management (Low)**
- **What's wrong:**
  - `HttpResponseMessage`/`StringContent` are not disposed (`NepaliPatroApiClient.cs:64-68`, `ForexClient.cs:64-68`).
  - `SqliteCommand`s are not disposed in either repository.
  - Five HTTP clients each own an `HttpClient` for the app's lifetime. This is fine for a desktop app, but `IHttpClientFactory` gives DNS refresh and testability.
  - `TrayIconGlyphFactory.GetFontFamily()` creates a new `FontFamily("Segoe UI")` every minute on the fallback path and never disposes it (`:150`).
  - A tray icon is regenerated every minute even when the date hasn't changed (`App.xaml.cs:457-461`).
  - Event subscriptions to VMs via lambdas (`MainWindow.xaml.cs:140-143, 203-225`) are never removed. That's acceptable only because `MainWindow` is never recreated.
  - `WindowBoundsService.SaveAsync` opens **5 separate connections** per save and runs on every 30 ms-debounced resize (`MainWindow.xaml.cs:444-448`).
- **Recommended approach:** Use `using` everywhere. Regenerate the icon only when the BS date or holiday flag changes. Save the bounds with one upsert in one transaction, ideally only on close or hide.

**C6 — Logging, configuration and secrets (Medium)**
- **Logging:**
  - `App.Log` appends synchronously to **`Desktop\openpatro-diag.log`** in every build configuration, with no size cap (Bug 5).
  - It also logs every tray click and popup position (`TrayPopupWindow.cs:296-391`).
- **Configuration:** Endpoints, CDN hosts, timeouts and the hardcoded calendar fallback `2082/11` (`CalendarViewModel.cs:199-200`) are all compile-time constants.
- **Secrets:**
  - `NepaliPatroApiClient.cs:24-25` embeds the AES key and IV used to decrypt a third party's obfuscated Shubha Sait payload.
  - This isn't a secret of *yours*, but it is reverse-engineered access to a private API. It will break whenever they rotate the key, and it carries terms-of-service risk.
  - The same applies to scraping hamropatro.com (`HamroPatroClient.cs:23`) and the undocumented `sharehubnepal.com` and `api.nepalipatro.com.np` endpoints. None of them has a fallback or user-visible "source unavailable" state.
- **Repo:** Signing assets (`OpenPatro_PackageSigning.pfx`, `OpenPatro_TemporaryKey.pfx`) are committed. Make sure the `.pfx` passwords are not trivial. Ideally keep the private key out of git and sign in CI.

**C7 — Packages and runtime (High)**
- **Vulnerable package:** `SQLitePCLRaw.lib.e_sqlite3` **2.1.6**, pulled in transitively by Microsoft.Data.Sqlite 8.0.13, has a **High** advisory, [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).
- **Runtime:** **.NET 8 support ends 2026-11-10 (today).** Plan the move to .NET 10 (LTS). `AGENTS.md` already records a .NET 10 publish quirk with the XAML compiler, so budget time for it.
- **Outdated packages** (`dotnet list package --outdated`):

| Package | Current | Latest |
|---|---|---|
| H.NotifyIcon.WinUI | 2.3.0 | 2.4.1 |
| Microsoft.Data.Sqlite | 8.0.13 | 10.0.14 |
| Microsoft.WindowsAppSDK | 1.8.260209005 | 2.6.5 (major) |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.7705 | 10.0.28000.2957 |
| System.Formats.Nrbf | 9.0.1 | 10.0.14 (unused) |

- **Recommended approach:**
  - Upgrade Microsoft.Data.Sqlite, or add an explicit `SQLitePCLRaw.bundle_e_sqlite3` at a patched version, now.
  - Take H.NotifyIcon 2.4.1 (see P9).
  - Evaluate Windows App SDK 2.x separately.

**C8 — Build and publish configuration drift (Low)**
- **What's wrong:**
  - `OpenPatro.csproj:84-88` turns on `PublishTrimmed=True` and `PublishReadyToRun=True` for every non-Debug configuration.
  - `win-x64.pubxml` and `msix-x64.pubxml` override trimming to `false`, but `win-x86.pubxml` and `win-arm64.pubxml` do not, so **x86/ARM64 publishes are trimmed** (P2).
  - `AGENTS.md` says `win-x64.pubxml` sets `PublishReadyToRun=false`, but it doesn't.
  - `build_calendar_db.py::ensure_schema` adds a script-only `SyncLog` table and an extra index; the app's tables and both app indexes match *(corrected: an earlier version of this report wrongly said the indexes were missing)*. Its default end year is 2085, while the in-app seeder uses 2089 (`BundledCalendarSeedService.cs:42`).
- **Recommended approach:** Move publish settings into the pubxml files only, and fix the doc.

**C9 — Testability (High)**
- **What's wrong:** There are no tests and no CI (`.github/workflows/` is empty). Several pure-logic units would be easy to test but are private or `internal`:
  - `HamroPatroParser`
  - `WindowLayoutService.ComputeCalendarGridMetrics`
  - `NepaliPatroApiClient.DecryptSchemeB`
  - `CalendarRepository.EscapeLikePattern`
  - `SafeDecimalJsonConverter`
  - BS/AD weekday mapping
- **Why it matters:** VMs depend on concrete sealed clients with private `HttpClient`s and on `Application.Current`.
- **Recommended approach:** Add `OpenPatro.Tests`, starting with Appendix A. Add `InternalsVisibleTo` and interfaces as the DI work lands.

**C10 — Data layer details (Low)**
- **What's wrong:**
  - A new `SqliteConnection` is opened per call (`CalendarViewModel.LoadMonthAsync` does ~8 sequential queries per month change, `:248-274`). Pooling makes this cheap, but one connection per operation would be simpler.
  - The bundled seed DB is only copied when the local DB is **empty** (`DatabaseBootstrapper.cs:22-24`). Corrected or extended bundled data never reaches existing installs, except month-by-month through scraping.
  - Dates are taken from the local machine clock in places that mean "Nepal today": `DateTime.Today` in `ForexViewModel.cs:525, 773`, `BullionViewModel.cs:581` and `BullionClient.cs:57`. Elsewhere the code correctly uses `NepalTimeService`.
- **What's right:** SQL is fully parameterized and LIKE input is escaped (`CalendarRepository.cs:248-251`).
- **Recommended approach:** Add a schema version and a "bundled data version" so newer seed data can be merged on upgrade.

---

## 4. Confirmed bugs

Evidence types:
- **[TEST]** has a failing test in Appendix A, run against the built `OpenPatro.dll`.
- **[TRACE]** is a deterministic code path traced line by line.
- **[OBSERVED]** was seen on this machine.

### Bug 1 — "Start with Windows" is silently re-enabled on every launch · **High** · [TRACE]
- **Location:** `App.xaml.cs:69-80`, `Services/StartupService.cs:18-28`, `ViewModels/SettingsViewModel.cs:26-36,46`.
- **Steps to reproduce:**
  1. Launch OpenPatro.
  2. Open **Settings** and switch **Start with Windows** off. Alternatively, untick it in the tray menu. Either way the `HKCU\…\Run\OpenPatro` value is deleted.
  3. Right-click the tray icon and choose **Exit**.
  4. Launch OpenPatro again from the Start menu.
  5. Open **Settings**.
- **Expected:** The toggle is off and no Run value exists.
- **Actual:** The toggle is on and the Run value is recreated. The app will autostart at the next sign-in.
- **Root cause:** `OnLaunched` runs `if (!Services.Startup.IsEnabled()) Services.Startup.SetEnabled(true);` unconditionally on every non-seed launch (`App.xaml.cs:71-75`). No "user opted out" setting exists. `SettingsViewModel.InitializeAsync` then reads the re-enabled state (`:46`).
- **Evidence:** A deterministic trace; there is no branch that skips it. It was not run, to avoid modifying the auditor's registry.
- **Fix:** Enable autostart only on first run, by storing `StartupConfigured=1` in the `Settings` table. Never override an explicit choice after that.

### Bug 2 — Live Market table shows turnover under VOLUME and volume under TURNOVER · **High** · [TEST]
- **Location:** `ViewModels/StockMarketViewModel.cs:520-543` (call site) vs the constructor signature at `:1016`. Bindings are at `MainWindow.xaml:1134/1140` (headers) and `:1188-1189` (cells).
- **Steps to reproduce:**
  1. Launch OpenPatro and click **Stock Market**.
  2. Scroll to the **Live Market** table and compare any row's VOLUME and TURNOVER with the source, or with the Top Turnover list.
- **Expected:** VOLUME shows shares traded and TURNOVER shows the rupee value.
- **Actual:** The two columns are swapped. Sorting by VOLUME orders rows by the *hidden* quantity, so the visible VOLUME column looks unsorted.
- **Root cause:** The constructor's positional parameters are `…, changePercent, turnover, quantity, transactions, …`. The call passes `TotalTradeQuantity.ToString()` into `turnover` and `TotalTradeValue.ToString()` into `quantity`. The numeric arguments further down are in the correct order, so only the display strings are wrong.
- **Evidence:** `LiveMarket_volume_and_turnover_text_match_their_columns` fails with `Expected "1,000.00", Actual "500,000.00"`. The control test `LiveMarket_sorting_by_volume_uses_quantity` passes, which confirms the numeric values are correct.
- **Fix:** Swap the two string arguments. Better, use named arguments, or build the row VM from the model directly. The 22-parameter positional constructor is what made this easy to get wrong.

### Bug 3 — A corrupt `user.db` leaves an invisible, unrecoverable process · **Medium** · [TEST + TRACE]
- **Location:** `Services/DatabaseBootstrapper.cs:47-53, 135-160`, `App.xaml.cs:63-109`, `Program.cs:52-57`, `MainWindow.xaml.cs:95-97`.
- **Steps to reproduce:**
  1. Quit OpenPatro.
  2. Replace `%LocalAppData%\OpenPatro\user.db` with any non-SQLite file. This could also follow a crash during a write, disk corruption, or a sync tool interfering.
  3. Launch OpenPatro.
  4. Launch it again.
- **Expected:** An error message, or the DB is recreated or backed up and the app starts.
- **Actual:**
  - No window and no tray icon appear, but `OpenPatro.exe` keeps running.
  - Every further launch is redirected to that process and still shows nothing.
  - The only recovery is Task Manager.
- **Root cause:**
  1. `EnsureUserSchemaAsync` throws `SqliteException: file is not a database`.
  2. `OnLaunched` catches it, logs it and returns (`:106-109`), leaving `Services` and `MainViewModel` null while the process holds the single-instance key.
  3. A second launch calls `OnInstanceActivated` → `ShowMainWindow()` → `new MainWindow()`.
  4. The constructor dereferences `MainViewModel` (`MainWindow.xaml.cs:95-97`) and throws `NullReferenceException`.
  5. That exception is rethrown (`App.xaml.cs:184-188`) and swallowed by `OnUnhandledException` (`:55`).
- **Evidence:** `Bootstrapper_survives_a_corrupt_user_db` fails with `SqliteException (0x80004005): SQLite Error 26: 'file is not a database'`. The remaining steps are a deterministic trace.
- **Fix:**
  - On schema failure, rename the bad file to `user.db.corrupt-<timestamp>` and recreate it.
  - If startup still fails, show a message dialog and `Exit()` so the single-instance key is released.

### Bug 4 — Date Converter pre-fills today's **AD** date into the **BS** fields · **Medium** · [TRACE]
- **Location:** `MainWindow.xaml.cs:1576-1624` (especially `:1584-1586`), `ViewModels/DateConverterViewModel.cs:19`, `MainWindow.xaml:1476-1483`.
- **Steps to reproduce:**
  1. Launch OpenPatro and click **Date Converter**. The direction defaults to "BS → AD".
  2. Look at the pre-filled inputs, then click **Convert** without editing.
- **Expected:** Today's BS date is pre-filled, around 2083-07-24, or the fields are empty.
- **Actual:**
  - Year `2026`, month `11` (shown as फाल्गुन) and day `10` are pre-filled.
  - Converting gives a 1969 AD date.
  - After switching to AD → BS, the fields keep these numbers, which happen to be right for AD.
- **Root cause:**
  - `SyncDateConverterInputParts` falls back to `DateTime.Now.Year/Month/Day` when `InputDate` is empty, whatever the `ConversionDirection` (`"BS"` by default).
  - Once `InputDate` is set to `"2026-11-10"`, it is never re-derived when the direction changes.
- **Evidence:** A deterministic trace: XAML `SelectedIndex="0"` → `Tag="BS"`; `RootGrid_Loaded` calls `SyncDateConverterInputParts()` (`:187`) with an empty `InputDate`.
- **Fix:** For the BS default, use `CalendarRepository.GetTodayAsync()` (BsYear/BsMonth/BsDay). Move this defaulting into `DateConverterViewModel`.

### Bug 5 — Diagnostic log is written to the user's Desktop, unbounded, in Release builds · **Medium** · [OBSERVED]
- **Location:** `App.xaml.cs:20-21, 942-953`, plus callers throughout `App.xaml.cs` and `TrayPopupWindow.cs:296-391`.
- **Steps to reproduce:**
  1. Install from `OpenPatroSetup-1.0.1-x64.exe` and launch it.
  2. Look at the Desktop.
- **Expected:** No files on the Desktop. Logs live in `%LocalAppData%\OpenPatro\logs`, are size-capped, and verbose tracing is Debug-only.
- **Actual:**
  - `openpatro-diag.log` appears on the Desktop.
  - It is appended to on every launch, every tray click (about 5 lines per click), each popup show, and every error, and it is never rotated.
  - On this machine it is **700 KB, 10,060 lines, covering 651 launches since 2026-03-13**.
- **Root cause:** `DiagLogPath` is hardcoded to `SpecialFolder.Desktop`, and `Log` is not conditional on configuration.
- **Evidence:** The file exists at `C:\Users\MenaceXnadin\Desktop\openpatro-diag.log`.
- **Fix:** Log to `ApplicationPaths.LocalFolderPath\logs` with rolling files (e.g. 1 MB × 3). Keep trace-level messages behind `#if DEBUG` or a setting.

### Bug 6 — Settings › "Refresh current data now" does nothing after the first sync of the day · **Low** · [TEST]
- **Location:** `ViewModels/SettingsViewModel.cs:51-56`, `Services/CalendarSyncService.cs:52-57`, `MainWindow.xaml:549`.
- **Steps to reproduce:**
  1. Launch OpenPatro while online. The silent refresh runs at launch and stores today's marker.
  2. Open **Settings** and click **Refresh current data now**.
- **Expected:** The current and next month are re-downloaded.
- **Actual:** No network request is made and the label is unchanged.
- **Root cause:** The button calls `RunSilentRefreshAsync()`, which returns early when `CalendarSilentSyncDate == today`. That guard exists for the *automatic* refresh, and the manual path has no way to bypass it.
- **Evidence:** `Settings_refresh_now_fetches_even_if_already_synced_today` fails with `Assert.NotEmpty() Failure: Collection was empty`, meaning 0 HTTP requests were recorded.
- **Fix:** Add a `force` parameter, e.g. `RunSilentRefreshAsync(force: true)`, for the button.

### Bug 7 — Changing the Bullion range while it's loading is lost · **Low** · [TEST]
- **Location:** `ViewModels/BullionViewModel.cs:345-355, 370-375`, `MainWindow.xaml.cs:230-243`.
- **Steps to reproduce:**
  1. Launch OpenPatro and click **Bullion**. The first 30-day load starts.
  2. Before it finishes, choose **Last 1 year** in the range picker.
- **Expected:** One year of data is loaded.
- **Actual:**
  - The picker shows "Last 1 year" but the table and charts show 30 days.
  - Re-selecting "Last 1 year" does nothing; you must pick another range and then come back.
- **Root cause:** `ApplyRangeAsync` sets `SelectedRangeKey` and then calls `RefreshAsync()`, which returns immediately because `IsBusy` is true. After that, `ApplyRangeAsync("1Y")` short-circuits on `SelectedRangeKey == "1Y" && HasData`.
- **Evidence:** `Bullion_range_change_during_load_is_not_lost` fails. Only the `from-date=<today−30>` request was recorded.
- **Fix:** Cancel the in-flight request and restart, or queue a reload when the key changes during a load.

### Bug 8 — Bullion "items per page" shows 100 while 120 rows are displayed · **Low** · [TEST]
- **Location:** `ViewModels/BullionViewModel.cs:65, 567-577`, `MainWindow.xaml:2086-2094`, `MainWindow.xaml.cs:280-304`.
- **Steps to reproduce:**
  1. Open **Bullion** and choose **Last 6 months** or longer, so there are more than 120 rows.
  2. Look at the page-size picker and the row summary.
- **Expected:** The picker value matches the rows per page.
- **Actual:** The picker shows **100**, while the summary reads "Showing 1–120 of N rows".
- **Root cause:** `DefaultItemsPerPage = 120` is not one of the picker options (25/50/100/200/500). `SyncBullionItemsPerPageSelection` finds no match and falls back to `SelectedIndex = 2` ("100") with events suppressed, so the VM is never corrected.
- **Evidence:** `Bullion_default_page_size_is_a_selectable_option` fails with `Not found: 120`.
- **Fix:** `DefaultItemsPerPage = 100`.

### Bug 9 — Forex history requests send non-Gregorian years on some system locales · **Low** · [TEST]
- **Location:** `Services/ForexClient.cs:60-61`.
- **Steps to reproduce:**
  1. Set the Windows region format to Thai (th-TH). Saudi Arabic (ar-SA) is also affected.
  2. Launch OpenPatro, open **Forex** and click a currency row.
- **Expected:** History for the last 30 days.
- **Actual:**
  - The request body says `"from":"2569-11-01"` (Buddhist Era), so the history and trend panels come back empty or error.
  - The trend chart is affected the same way.
- **Root cause:** `DateOnly.ToString("yyyy-MM-dd")` uses the current culture's calendar. `CultureInfo.InvariantCulture` is missing.
- **Evidence:** `Forex_history_dates_are_gregorian_regardless_of_culture` fails with `String: "{"currency_code":"USD","from":"2569-11-01"…`.
- **Fix:** Pass `CultureInfo.InvariantCulture`.

### Bug 10 — Restoring from maximized discards the user's window size · **Low** · [TRACE]
- **Location:** `MainWindow.xaml.cs:1221-1241` (especially `:1229-1232`).
- **Steps to reproduce:**
  1. Resize the main window to a custom size.
  2. Maximize it.
  3. Restore it with the title-bar button or a double-click.
- **Expected:** The window returns to the custom size.
- **Actual:** It snaps to the computed default (72% × 88% of the work area).
- **Root cause:** `AppWindow_Changed` calls `TryResizeToInitialWindowSize()` on every Maximized → Restored transition.
- **Evidence:** A deterministic trace. The call is unconditional on that transition.
- **Fix:** Remove the call. Windows restores the previous bounds by itself.

### Bug 11 — Tray popup always opens at the bottom-right of the primary monitor · **Low** · [TRACE]
- **Location:** `TrayPopupWindow.cs:348-376, 417-425`.
- **Steps to reproduce:**
  1. Move the taskbar to the top or left, or put the tray on a secondary monitor.
  2. Left-click the tray icon.
- **Expected:** The popup opens next to the tray icon.
- **Actual:** It opens in the bottom-right corner of the *primary* monitor's work area, far from the icon.
- **Root cause:** `SystemParametersInfo(SPI_GETWORKAREA)` returns the primary monitor's work area, and the position is always `Right − width`, `Bottom − height`.
- **Evidence:** A deterministic trace.
- **Fix:** Use `Shell_NotifyIconGetRect` or the cursor position with `DisplayArea.GetFromPoint`, and anchor to the taskbar edge (`SHAppBarMessage(ABM_GETTASKBARPOS)`).

---

## 5. Potential issues (unverified)

| # | Suspicion | Where | What would confirm it |
|---|---|---|---|
| P1 | **Autostart does not work in the MSIX build.** Packaged apps' HKCU writes are virtualized into a private hive, so Explorer never sees the Run value. MSIX should use a `StartupTask`. | `StartupService.cs:18-28`, `Package.appxmanifest` (no `windows.startupTask`) | Install `msix-x64`, enable the toggle, sign out and in, and check `reg query HKCU\…\Run` from outside the package. |
| P2 | **x86 and ARM64 release publishes are trimmed** (`PublishTrimmed=True` is not overridden). Code that may break under trimming: reflection (`App.SetEnumProperty`, reflective `ShowNotification`), reflection-based System.Text.Json, and WinRT projections. | `OpenPatro.csproj:87-88`, `win-x86.pubxml`, `win-arm64.pubxml` | Publish `win-arm64`, check for IL2026/IL2104 warnings, then run it and test the tray menu activation mode and JSON-backed sections. |
| P3 | **Minimized bounds are persisted.** Minimizing triggers `PersistWindowBoundsAsync` with `AppWindow.Position = (-32000,-32000)` and the minimized size. Exiting from the tray while minimized then restores a minimum-size window at the top-left. | `MainWindow.xaml.cs:929-960, 1234-1238` | Minimize, exit from the tray, relaunch, and inspect the `WindowBounds.*` rows in `user.db`. |
| P4 | **Out-of-order month loads.** `LoadMonthAsync` has no cancellation or sequence check. Overlapping calls (picker navigation, midnight `HandleDateChangeAsync`, tray "open for date") can finish in reverse order, so the grid shows month A while the title and pickers show month B. | `CalendarViewModel.cs:229-307` | A test with a delayed repository: start two loads and complete them in reverse order. |
| P5 | **Bullion charts dive to zero on missing prices.** `FormatPrice(0)` renders "--", which `ParseBullionPrice` turns back into `0` and plots, crushing the Y-scale. | `BullionViewModel.cs:549-552`, `MainWindow.xaml.cs:2128-2143` | An API day where `t_s` or `t_ha` is `"-"` or empty. |
| P6 | **Shubha Sait shows "Month 1"** if the API's month keys aren't zero-padded (`BsMonthNames` keys are `"01"`–`"12"`). | `ShubhaSaitViewModel.cs:38-52, 296` | Inspect a live decrypted payload. |
| P7 | **A second launch during startup** (before `MainViewModel` is set) hits the same NRE path as Bug 3. It is swallowed, and the first launch's window then appears normally. | `Program.cs:52-57`, `App.xaml.cs:63-101` | Double-click the shortcut twice quickly and check the log for `MainWindow constructor FAILED`. |
| P8 | **STA deadlock on redirection** (see C3). | `Program.cs:28` | Stress-launch many instances simultaneously. |
| P9 | **Tray tooltip and icon go stale.** The log shows `RefreshTray FAILED: UpdateToolTip failed` on 2026-05-12, 06-22 and 10-02, probably after an Explorer restart or resume. Because `ToolTipText` is set first, the icon update is skipped for that tick. | `App.xaml.cs:387-393`, diag log lines 5259, 6068, 8786 | Kill `explorer.exe` and restart it while OpenPatro runs. Check whether H.NotifyIcon 2.4.1 handles `TaskbarCreated`. |
| P10 | **The outside-click dismiss hook can silently disappear.** `WH_MOUSE_LL` runs on the UI thread, and Windows removes the hook if a callback exceeds `LowLevelHooksTimeout`. | `TrayPopupWindow.cs:205-254` | Open the popup, block the UI thread for more than 1 s (e.g. a long DB load), then click outside. |
| P11 | **Stale history after quick currency switching.** `LoadHistoryAsync` cancels the previous CTS but doesn't check whether it is still current after `await`. A completed-but-not-yet-resumed older call can overwrite the newer one's data, or clear `IsHistoryBusy` early. | `ForexViewModel.cs:512-577, 753-818` | A test with two delayed responses completing in reverse order. |

---

## 6. What's done well (keep these)

- **Single-instance and activation redirect** through `AppInstance.FindOrRegisterForKey`, with a custom `Main` (`Program.cs`). Tray-first lifecycle: close hides to tray, and a `--startup` flag gives a tray-only launch.
- **The tray reliability work is careful and well documented.**
  - The dispatcher keepalive window (`DispatcherKeepaliveWindow.cs`).
  - The pre-bootstrapped popup that avoids first-open flicker.
  - Foreground-rights handling ("show first, then load", `App.xaml.cs:757-766`).
  - Click-through suppression (`TrayPopupWindow.WasRecentlyDismissedByOutsideClick`).
- **Native resources are handled correctly where it matters most.** `TrayIconGlyphFactory.OwnHicon` clones and destroys the HICON, and the old tray icon is disposed (`App.xaml.cs:389-393`). The WndProc and hook delegates are kept in fields so they aren't garbage-collected.
- **SQL is fully parameterized**, LIKE input is escaped, upserts are wrapped in a transaction, and the DBs run in WAL mode.
- **The scraper validates its input defensively.** `HamroPatroParser` rejects pages whose day count or weekday doesn't match (`:33-36, 75-80`) instead of writing bad data. `GetWeekdayColumnIndex` cross-checks AD vs Nepali weekdays.
- **UI update batching.** `BulkObservableCollection.ReplaceAll` turns roughly 43 change notifications into one per month load. There is a debounced `ResizePipeline` and a 300 ms debounced search with a stale-result guard (`SearchViewModel.cs:100-105`).
- **The date-change watcher** handles midnight, sleep/resume and `WM_TIMECHANGE`, and is made thread-safe with `Interlocked` (`NepalTimeService.cs`).
- **Thread marshalling.** Background callbacks consistently check `HasThreadAccess` and `TryEnqueue`.
- **Network resilience basics.** Every HTTP call has a timeout or `CancellationToken`, sections show `ErrorMessage` instead of crashing, and the bundled seed DB lets the calendar work offline.
- **Clean compile.** Nullable is enabled with 0 warnings, and `WindowLayoutService` already extracts sizing math into pure, testable functions.
- **Good "why" comments** throughout, which made this audit much faster.

---

## 7. Recommended refactoring roadmap

Each step is small, shippable and safe on its own. Do them in order.

1. **Quick bug fixes (≈1 day).**
   - Bugs 1, 2, 6, 8 and 9 are one-to-five-line changes.
   - Bug 10 is a single deletion.
   - Bug 5: change the log path and add a size cap.
   - Bug 3: recreate a corrupt DB, and on fatal startup failure show a dialog and exit.
2. **Patch dependencies.** Bump Microsoft.Data.Sqlite, or pin `SQLitePCLRaw.bundle_e_sqlite3` to a patched version. Bump H.NotifyIcon.WinUI to 2.4.1. Remove `System.Formats.Nrbf` if nothing needs it. Set `PublishTrimmed=false` in the x86/ARM64 pubxml files, or prove trimming is safe (P2). Fix the R2R note in `AGENTS.md`.
3. **Add `OpenPatro.Tests` (xUnit) and CI.**
   - Port Appendix A; the tests should now pass.
   - Add parser tests with saved HTML fixtures, `WindowLayoutService` tests and `DecryptSchemeB` round-trip tests.
   - Add a GitHub Actions workflow that runs `dotnet build` and `dotnet test` on `windows-latest`.
4. **Error handling and logging.**
   - Introduce a small `ILog` (or `Microsoft.Extensions.Logging` with a file sink) that writes to `LocalFolderPath\logs`.
   - Add `TaskScheduler.UnobservedTaskException` and `AppDomain.UnhandledException` handlers.
   - Stop blanket `e.Handled = true`.
   - Show an `InfoBar` for user-initiated failures, starting with the save-note command.
5. **Adopt CommunityToolkit.Mvvm**, one VM at a time. Replace `BindableBase`, both `RelayCommand`s and both `AsyncRelayCommand`s with `ObservableObject`, `[ObservableProperty]` and `[RelayCommand]`. Delete `Infrastructure/*Command*.cs` once all VMs have moved.
6. **Introduce DI behind `AppServices`.**
   - Register the existing classes in `Microsoft.Extensions.DependencyInjection` with `AddHttpClient<T>()`.
   - Add interfaces (`ICalendarRepository`, `IForexClient`, …).
   - Inject only what each VM needs.
   - Replace `((App)Application.Current)` calls with an `IShellService` (show window / open date / refresh tray).
7. **Split `MainWindow`, one section per PR.**
   - Extract each section into a `UserControl` (start with Settings and Date Converter, the smallest), moving its handlers with it.
   - Then make one reusable `LineChart` control to replace the three chart renderers.
   - Finally, replace the `FindName` and `_suppress…` patterns with TwoWay bindings.
   - The goal is `MainWindow.xaml.cs` under 300 lines.
8. **Take UI types out of the VMs.** Swap brushes, `Visibility` and hex strings for state properties plus `ThemeResource`-based styles and visual states. This unblocks Light and High-Contrast themes, so you can remove `RequestedTheme="Dark"` afterwards.
9. **Accessibility and localization pass.** Add `AutomationProperties.Name` to icon buttons and day cells, move strings into `.resw` with `x:Uid`, and consolidate the Nepali month and weekday names into one source.
10. **Platform upgrades.**
    - Move to **.NET 10 LTS** (.NET 8 is EOL as of today) and re-verify the XAML-compiler publish quirk.
    - Evaluate Windows App SDK 2.x.
    - Add a `windows.startupTask` for the MSIX build (P1).
    - Merge bundled-seed updates using a data version (C10).

---

## Appendix A — failing tests

These were run against `bin/x64/Debug/.../OpenPatro.dll` at commit `2542d73`.

**Result: 6 failed (the bugs), 3 passed (controls).**

**Project setup**
- `net8.0-windows10.0.19041.0`, `win-x64`.
- Packages: `xunit` 2.9.2, `Microsoft.NET.Test.Sdk` 17.11.1, `Microsoft.Data.Sqlite` 8.0.13.
- `<Reference Include="OpenPatro" HintPath=".../OpenPatro.dll" />`.
- Copy `Microsoft.WindowsAppRuntime*.dll` and `Microsoft.Internal.FrameworkUdk.dll` from the app output. `OpenPatro.dll`'s module initializer loads the Windows App Runtime.
- In a real test project, prefer a `ProjectReference` plus `InternalsVisibleTo`, and constructor-injected `HttpClient`s instead of the reflection below.

```csharp
// Helpers — swap an app client's private HttpClient for a recording handler; build AppServices via its private ctor.
public sealed class RecordingHandler(Func<HttpRequestMessage, string> body) : HttpMessageHandler {
    public TaskCompletionSource? Gate { get; set; }
    public List<(string Url, string? Body)> Requests { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) {
        var c = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
        lock (Requests) Requests.Add((r.RequestUri!.ToString(), c));
        if (Gate is not null) await Gate.Task.WaitAsync(ct);
        return new(HttpStatusCode.OK) { Content = new StringContent(body(r)) };
    }
}
static T WithHandler<T>(T client, HttpMessageHandler h) where T : class {
    typeof(T).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, new HttpClient(h));
    return client;
}
static AppServices Services(BullionClient? bullion = null) {          // 14 ctor args; index 12 = Bullion
    var args = new object?[14]; args[12] = bullion;
    return (AppServices)typeof(AppServices).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke(args);
}
public sealed class TempPaths : ApplicationPaths, IDisposable {      // protected internal ctor → subclass
    public TempPaths() : base(Directory.CreateTempSubdirectory("openpatro-audit-").FullName) { }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(LocalFolderPath, true); } catch { } }
}

// Bug 2 — FAILS: Expected "1,000.00", Actual "500,000.00"
[Fact] public void LiveMarket_volume_and_turnover_text_match_their_columns() {
    var vm = new StockMarketViewModel(null!);
    typeof(StockMarketViewModel).GetMethod("MapResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(vm, new object[] { new StockMarketHomeData { LiveCompanyData = new() {
            new LiveCompanyDataInfo { Symbol = "NABIL", SecurityName = "Nabil Bank", TotalTradeQuantity = 1_000m, TotalTradeValue = 500_000m } } } });
    var row = vm.FilteredLiveCompanies.Single();
    Assert.Equal(1_000m, row.QuantityValue);   // passes — numeric values are right
    Assert.Equal("1,000.00", row.Quantity);    // fails — text is swapped
    Assert.Equal("500,000.00", row.Turnover);
}

// Bug 6 — FAILS: Assert.NotEmpty() Failure: Collection was empty
[Fact] public async Task Settings_refresh_now_fetches_even_if_already_synced_today() {
    using var paths = new TempPaths();
    await new DatabaseBootstrapper(paths).InitializeAsync();
    var clock = new NepalTimeService(); var cal = new CalendarRepository(paths, clock); var user = new UserRepository(paths);
    var todayIso = clock.GetNepalToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    await cal.UpsertMonthAsync(new ParsedCalendarMonth { RequestedYear = 2083, RequestedMonth = 7, TitleNepali = "t", TitleEnglish = "t",
        Days = new() { Day(2083, 7, 1, todayIso) } });            // Day(...) = CalendarDayRecord with required fields
    await user.SetSettingAsync("CalendarSilentSyncDate", todayIso); // the launch-time sync already ran today
    var handler = new RecordingHandler(_ => "<html></html>");
    var sync = new CalendarSyncService(clock, cal, WithHandler(new HamroPatroClient(new HamroPatroParser()), handler), user);
    await sync.RunSilentRefreshAsync();                              // what the Settings button calls
    Assert.NotEmpty(handler.Requests);
}

// Bug 3 — FAILS: SqliteException SQLite Error 26: 'file is not a database'
[Fact] public async Task Bootstrapper_survives_a_corrupt_user_db() {
    using var paths = new TempPaths();
    await File.WriteAllTextAsync(paths.UserDatabasePath, "this is not a sqlite database, it is long enough to have a header");
    Assert.Null(await Record.ExceptionAsync(() => new DatabaseBootstrapper(paths).InitializeAsync()));
}

// Bug 8 — FAILS: Not found: 120
[Fact] public void Bullion_default_page_size_is_a_selectable_option() =>
    Assert.Contains(new BullionViewModel(null!).ItemsPerPage, new[] { 25, 50, 100, 200, 500 });

// Bug 7 — FAILS: only the 30-day request was made
[Fact] public async Task Bullion_range_change_during_load_is_not_lost() {
    var handler = new RecordingHandler(_ => """{"source":"x","data":{"2026-11-09":{"bs":"24.07.2083","t_ha":"150000","t_te":"149000","t_s":"2000","g_ha":"1","g_te":"1","g_s":"1"}}}""")
        { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    var vm = new BullionViewModel(Services(bullion: WithHandler(new BullionClient(), handler)));
    var initial = vm.InitializeAsync();      // 30D load in flight
    await vm.ApplyRangeAsync("1Y");          // user changes range while busy
    handler.Gate.SetResult(); await initial;
    await vm.ApplyRangeAsync("1Y");
    var y = DateTime.Today.AddYears(-1);
    Assert.Contains(handler.Requests, r => r.Url.Contains($"from-date={y.Year}-{y.Month}-{y.Day}"));
}

// Bug 9 — FAILS: body contains "from":"2569-11-01"
[Fact] public async Task Forex_history_dates_are_gregorian_regardless_of_culture() {
    var handler = new RecordingHandler(_ => "[]");
    var forex = WithHandler(new ForexClient(), handler);
    var prev = CultureInfo.CurrentCulture;
    try { CultureInfo.CurrentCulture = new CultureInfo("th-TH");
          await forex.FetchHistoryAsync("USD", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10)); }
    finally { CultureInfo.CurrentCulture = prev; }
    Assert.Contains("\"from\":\"2026-11-01\"", handler.Requests.Single().Body!);
}

// Controls (PASS): Live-market sort by "Volume" orders by quantity; SafeDecimalJsonConverter parses "1,234.50" and "-";
// the RecordingHandler rig records HamroPatroClient requests (proves Bug 6's empty list is real, not a rig failure).
```
