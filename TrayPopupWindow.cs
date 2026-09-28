using System;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenPatro.Controls;
using OpenPatro.ViewModels;
using Windows.Graphics;

namespace OpenPatro;

public sealed class TrayPopupWindow : Window
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsCaption = 0x00C00000;
    private const int WsSysMenu = 0x00080000;
    private const int WsThickFrame = 0x00040000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwTopmost = new(-1);
    private static readonly IntPtr HwNoTopmost = new(-2);

    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private LowLevelMouseProc? _mouseHookProc;
    private IntPtr _mouseHookHandle = IntPtr.Zero;

    /// <summary>
    /// How long after an outside-click dismiss a toggle-show request is ignored.
    /// The tray-icon click itself lands outside the popup, so the dismiss hook hides
    /// it a hair before the toggle command runs — without this the toggle would see
    /// "closed" and immediately re-open it, making toggle-close impossible.
    /// </summary>
    private const int OutsideDismissSuppressionMs = 350;

    private DateTime _lastOutsideDismissUtc = DateTime.MinValue;
    private const uint SpiGetWorkArea = 0x0030;
    private const int PopupMarginDip = 12;
    // Fallback size when content hasn't been measured yet (first layout pass).
    private const int FallbackWidthDip = 436;
    private const int FallbackHeightDip = 690;
    // Park the window far off-screen instead of hiding it so the Win32 HWND stays
    // alive and the WinUI dispatcher keeps running when no main window is visible.
    private const int ParkX = -32000;
    private const int ParkY = -32000;

    private bool _isBootstrapped;
    private DateTimeOffset _lastShownAt;
    private bool _hasFocusSinceShown;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out Rect pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public TrayPopupWindow(CalendarViewModel viewModel)
    {
        var popupContent = new TrayCalendarPopup();
        popupContent.Attach(viewModel);
        Content = popupContent;

        // Keep the window snug around its content: re-anchor bottom-right
        // whenever the content size changes (e.g. day-detail panel expands).
        // Guarded by IsPopupVisible so initial layout passes are ignored.
        popupContent.SizeChanged += (_, _) =>
        {
            if (IsPopupVisible)
            {
                PositionWindow();
            }
        };

        Activated += TrayPopupWindow_Activated;
    }

    public bool IsPopupVisible { get; private set; }

    /// <summary>
    /// Called once at tray initialization so the HWND exists before the first user
    /// click. This keeps the WinUI dispatcher alive even when all other windows are
    /// hidden, which is required for H.NotifyIcon commands to fire.
    /// </summary>
    public void Bootstrap()
    {
        if (_isBootstrapped)
        {
            return;
        }

        _isBootstrapped = true;
        WindowExtensions.Show(this, disableEfficiencyMode: true);
        ConfigureWindow();
        ParkOffScreen();
    }

    public void ShowPopup()
    {
        Bootstrap();
        PositionWindow();
        _lastShownAt = DateTimeOffset.UtcNow;
        _hasFocusSinceShown = false;
        Activate();
        MakeTopmost();
        InstallDismissHook();
        IsPopupVisible = true;
        LogShowState("after Activate");
    }

    /// <summary>
    /// Pins the popup above all non-topmost windows without stealing focus.
    /// A tray flyout shown while another app is foreground would otherwise open
    /// <i>behind</i> a maximized window and look like the click did nothing.
    /// </summary>
    private void MakeTopmost()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, HwTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    /// <summary>
    /// Watches for mouse clicks anywhere outside the popup while it is visible.
    /// Needed because the popup is shown without taking focus, so no deactivate
    /// event fires when the user clicks elsewhere — without this the popup would
    /// only close via the tray icon. Clicks are never swallowed.
    /// </summary>
    private void InstallDismissHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        _mouseHookProc = MouseHookCallback;
        _mouseHookHandle = SetWindowsHookEx(WhMouseLl, _mouseHookProc, GetModuleHandle(null), 0);
    }

    private void UninstallDismissHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
        _mouseHookProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsPopupVisible)
        {
            int msg = wParam.ToInt32();
            if (msg == WmLButtonDown || msg == WmRButtonDown || msg == WmMButtonDown)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd != IntPtr.Zero
                    && GetCursorPos(out POINT pt)
                    && GetWindowRect(hwnd, out RECT rc))
                {
                    bool inside = pt.X >= rc.Left && pt.X < rc.Right && pt.Y >= rc.Top && pt.Y < rc.Bottom;
                    if (!inside)
                    {
                        // Stamp synchronously: the tray-icon click that caused this
                        // dismiss will reach the toggle command right after, and it
                        // must know this hide was user-initiated, not a state to flip.
                        _lastOutsideDismissUtc = DateTime.UtcNow;
                        DispatcherQueue.TryEnqueue(HidePopup);
                    }
                }
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    public void HidePopup()
    {
        if (!IsPopupVisible)
        {
            return;
        }

        ParkOffScreen();
        IsPopupVisible = false;
        UninstallDismissHook();
    }

    /// <summary>
    /// True when the popup is closed because an outside click just dismissed it.
    /// The toggle command checks this so the same physical click (e.g. on the tray
    /// icon) doesn't instantly re-open what it just closed.
    /// </summary>
    public bool WasRecentlyDismissedByOutsideClick()
    {
        return !IsPopupVisible
            && (DateTime.UtcNow - _lastOutsideDismissUtc).TotalMilliseconds < OutsideDismissSuppressionMs;
    }

    private void ParkOffScreen()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd != IntPtr.Zero)
        {
            // Also drop topmost status so the parked window never intercepts anything.
            SetWindowPos(hwnd, HwNoTopmost, ParkX, ParkY, 0, 0, SwpNoSize | SwpNoActivate);
        }
    }

    private void TrayPopupWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!IsPopupVisible)
        {
            return;
        }

        App.Log($"TrayPopup: activation -> {args.WindowActivationState}, hadFocus={_hasFocusSinceShown}");
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _hasFocusSinceShown = true;
            return;
        }

        // Ignore the transient deactivation that follows showing: a tray-only app
        // is frequently refused foreground, which fires Deactivated without the
        // popup ever having focus. Only auto-hide once it actually HAD focus
        // (real click-away) or the show is old enough to be a zombie.
        if (!_hasFocusSinceShown && DateTimeOffset.UtcNow - _lastShownAt < TimeSpan.FromSeconds(3))
        {
            App.Log("TrayPopup: transient deactivate ignored (never had focus)");
            return;
        }

        App.Log("TrayPopup: deactivated, hiding");
        HidePopup();
    }

    private void ConfigureWindow()
    {
        // Guard is now handled via _isBootstrapped in Bootstrap().
        // This method is only called from Bootstrap() which runs once.

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        AppWindow.IsShownInSwitchers = false;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsSysMenu | WsThickFrame | WsMinimizeBox | WsMaximizeBox);
        style |= WsPopup;
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow));
        _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void PositionWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var (widthPx, heightPx) = GetFittedSizePx(dpi);
        var marginPx = ScaleDip(PopupMarginDip, dpi);

        var workArea = GetWorkArea();
        var maxWidthPx = workArea.Right - workArea.Left - (marginPx * 2);
        var maxHeightPx = workArea.Bottom - workArea.Top - (marginPx * 2);
        widthPx = Math.Min(widthPx, Math.Max(maxWidthPx, marginPx * 2));
        heightPx = Math.Min(heightPx, Math.Max(maxHeightPx, marginPx * 2));

        var x = workArea.Right - widthPx - marginPx;
        var y = workArea.Bottom - heightPx - marginPx;

        AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
        LogShowState($"positioned workArea=({workArea.Left},{workArea.Top},{workArea.Right},{workArea.Bottom})");
    }

    private void LogShowState(string stage)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            App.Log($"TrayPopup: {stage} hwnd=0x{hwnd.ToInt64():X}, osVisible={IsWindowVisible(hwnd)}, isForeground={GetForegroundWindow() == hwnd}, pos=({pos.X},{pos.Y}), size=({size.Width}x{size.Height})");
        }
        catch (Exception ex)
        {
            App.Log($"TrayPopup: LogShowState FAILED: {ex.Message}");
        }
    }

    /// <summary>
    /// Measures the popup content and converts to physical pixels so the window
    /// wraps its content instead of using a fixed oversized frame.
    /// </summary>
    private (int WidthPx, int HeightPx) GetFittedSizePx(uint dpi)
    {
        if (Content is FrameworkElement content)
        {
            content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = content.DesiredSize;
            if (desired.Width >= 100 && desired.Height >= 100)
            {
                return (ScaleDip(desired.Width, dpi), ScaleDip(desired.Height, dpi));
            }
        }

        return (ScaleDip(FallbackWidthDip, dpi), ScaleDip(FallbackHeightDip, dpi));
    }

    private static int ScaleDip(double dip, uint dpi)
    {
        return (int)Math.Ceiling(dip * dpi / 96.0);
    }

    private static Rect GetWorkArea()
    {
        if (SystemParametersInfo(SpiGetWorkArea, 0, out var workArea, 0))
        {
            return workArea;
        }

        return new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }
}