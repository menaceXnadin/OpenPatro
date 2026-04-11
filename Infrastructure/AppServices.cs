using System.Threading.Tasks;
using OpenPatro.Services;

namespace OpenPatro.Infrastructure;

public sealed class AppServices
{
    private AppServices(
        ApplicationPaths paths,
        NepalTimeService clock,
        CalendarRepository calendarRepository,
        UserRepository userRepository,
        HamroPatroClient hamroPatro,
        CalendarSyncService calendarSync,
        StartupService startup,
        TrayNotificationService notifications,
        BundledCalendarSeedService bundledSeed,
        NepaliPatroApiClient nepaliPatroApi)
    {
        Paths = paths;
        Clock = clock;
        CalendarRepository = calendarRepository;
        UserRepository = userRepository;
        HamroPatro = hamroPatro;
        CalendarSync = calendarSync;
        Startup = startup;
        Notifications = notifications;
        BundledSeed = bundledSeed;
        NepaliPatroApi = nepaliPatroApi;
    }

    public ApplicationPaths Paths { get; }

    public NepalTimeService Clock { get; }

    public CalendarRepository CalendarRepository { get; }

    public UserRepository UserRepository { get; }

    public HamroPatroClient HamroPatro { get; }

    public CalendarSyncService CalendarSync { get; }

    public StartupService Startup { get; }

    public TrayNotificationService Notifications { get; }

    public BundledCalendarSeedService BundledSeed { get; }

    public NepaliPatroApiClient NepaliPatroApi { get; }

    public static async Task<AppServices> CreateAsync()
    {
        var paths = ApplicationPaths.Create();
        var clock = new NepalTimeService();
        var bootstrapper = new DatabaseBootstrapper(paths);
        await bootstrapper.InitializeAsync();

        var calendarRepository = new CalendarRepository(paths, clock);
        var userRepository = new UserRepository(paths);
        var parser = new HamroPatroParser();
        var hamroPatro = new HamroPatroClient(parser);
        var calendarSync = new CalendarSyncService(clock, calendarRepository, hamroPatro, userRepository);
        var startup = new StartupService();
        var notifications = new TrayNotificationService(userRepository);
        var bundledSeed = new BundledCalendarSeedService(paths, parser);
        var nepaliPatroApi = new NepaliPatroApiClient();

        return new AppServices(paths, clock, calendarRepository, userRepository, hamroPatro, calendarSync, startup, notifications, bundledSeed, nepaliPatroApi);
    }
}