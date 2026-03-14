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
    private static readonly TimeSpan DeactivateGracePeriod = TimeSpan.FromMilliseconds(350);
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
    private const uint SpiGetWorkArea = 0x0030;
    private const int PopupWidthDip = 436;
    private const int PopupHeightDip = 690;
    private const int PopupMarginDip = 12;
    // Park the window far off-screen instead of hiding it so the Win32 HWND stays
    // alive and the WinUI dispatcher keeps running when no main window is visible.
    private const int ParkX = -32000;
    private const int ParkY = -32000;

    private bool _isBootstrapped;
    private DateTimeOffset _lastShownAt;

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

    public TrayPopupWindow(CalendarViewModel viewModel)
    {
        var popupContent = new TrayCalendarPopup();
        popupContent.Attach(viewModel);
        Content = popupContent;

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
        Activate();
        IsPopupVisible = true;
    }

    public void HidePopup()
    {
        if (!IsPopupVisible)
        {
            return;
        }

        ParkOffScreen();
        IsPopupVisible = false;
    }

    private void ParkOffScreen()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero, ParkX, ParkY, 0, 0, SwpNoSize | SwpNoActivate | SwpNoZOrder);
        }
    }

    private void TrayPopupWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && IsPopupVisible)
        {
            if (DateTimeOffset.UtcNow - _lastShownAt < DeactivateGracePeriod)
            {
                return;
            }

            HidePopup();
        }
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

        var widthPx = ScaleDip(PopupWidthDip, dpi);
        var heightPx = ScaleDip(PopupHeightDip, dpi);
        var marginPx = ScaleDip(PopupMarginDip, dpi);

        var workArea = GetWorkArea();
        var x = workArea.Right - widthPx - marginPx;
        var y = workArea.Bottom - heightPx - marginPx;

        AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
    }

    private static int ScaleDip(int dip, uint dpi)
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