using System;
using System.Globalization;
using System.Threading.Tasks;

namespace OpenPatro.Services;

/// <summary>
/// Persists and restores window position, size, and presenter state (maximized/restored)
/// across application sessions using the user settings store.
/// </summary>
public sealed class WindowBoundsService
{
    private const string KeyWindowX = "WindowBounds.X";
    private const string KeyWindowY = "WindowBounds.Y";
    private const string KeyWindowWidth = "WindowBounds.Width";
    private const string KeyWindowHeight = "WindowBounds.Height";
    private const string KeyWindowState = "WindowBounds.State";

    /// <summary>
    /// Known presenter states persisted as string tokens.
    /// </summary>
    public const string StateRestored = "Restored";
    public const string StateMaximized = "Maximized";

    private readonly UserRepository _userRepository;

    public WindowBoundsService(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// Reads the last-saved bounds from the settings store.
    /// Returns null if no bounds have been saved yet or if the stored data is corrupt.
    /// </summary>
    public async Task<SavedWindowBounds?> LoadAsync()
    {
        try
        {
            var xStr = await _userRepository.GetSettingAsync(KeyWindowX);
            var yStr = await _userRepository.GetSettingAsync(KeyWindowY);
            var wStr = await _userRepository.GetSettingAsync(KeyWindowWidth);
            var hStr = await _userRepository.GetSettingAsync(KeyWindowHeight);
            var stateStr = await _userRepository.GetSettingAsync(KeyWindowState);

            if (xStr is null || yStr is null || wStr is null || hStr is null)
            {
                return null;
            }

            if (!int.TryParse(xStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(yStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                || !int.TryParse(wStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                || !int.TryParse(hStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
            {
                return null;
            }

            // Reject garbage values.
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var isMaximized = string.Equals(stateStr, StateMaximized, StringComparison.OrdinalIgnoreCase);

            return new SavedWindowBounds(x, y, w, h, isMaximized);
        }
        catch
        {
            // Settings DB might not be ready during first launch.
            return null;
        }
    }

    /// <summary>
    /// Writes the current window bounds and state to the settings store.
    /// </summary>
    public async Task SaveAsync(int x, int y, int width, int height, bool isMaximized)
    {
        try
        {
            await _userRepository.SetSettingAsync(KeyWindowX, x.ToString(CultureInfo.InvariantCulture));
            await _userRepository.SetSettingAsync(KeyWindowY, y.ToString(CultureInfo.InvariantCulture));
            await _userRepository.SetSettingAsync(KeyWindowWidth, width.ToString(CultureInfo.InvariantCulture));
            await _userRepository.SetSettingAsync(KeyWindowHeight, height.ToString(CultureInfo.InvariantCulture));
            await _userRepository.SetSettingAsync(KeyWindowState, isMaximized ? StateMaximized : StateRestored);
        }
        catch
        {
            // Best-effort persistence – don't crash if the DB write fails.
        }
    }
}

/// <summary>
/// Immutable snapshot of persisted window bounds and state.
/// </summary>
public sealed record SavedWindowBounds(int X, int Y, int Width, int Height, bool IsMaximized);
