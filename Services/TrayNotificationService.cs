using System.Reflection;
using System.Threading.Tasks;
using H.NotifyIcon;

namespace OpenPatro.Services;

public sealed class TrayNotificationService
{
    private readonly UserRepository _userRepository;

    public TrayNotificationService(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task ShowPinHintOnceAsync(TaskbarIcon taskbarIcon)
    {
        var alreadyShown = await _userRepository.GetSettingAsync("PinTrayHintShown");
        if (alreadyShown == "1")
        {
            return;
        }

        TryShowNotification(taskbarIcon, "OpenPatro", "Pin this tray icon to the visible taskbar area for quick access.");
        await _userRepository.SetSettingAsync("PinTrayHintShown", "1");
    }

    private static void TryShowNotification(TaskbarIcon taskbarIcon, string title, string message)
    {
        var method = taskbarIcon.GetType().GetMethod("ShowNotification", BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
        {
            return;
        }

        var parameters = method.GetParameters();
        if (parameters.Length >= 2)
        {
            var args = new object?[parameters.Length];
            args[0] = title;
            args[1] = message;
            for (var index = 2; index < parameters.Length; index++)
            {
                args[index] = parameters[index].HasDefaultValue ? parameters[index].DefaultValue : null;
            }

            _ = method.Invoke(taskbarIcon, args);
        }
    }
}