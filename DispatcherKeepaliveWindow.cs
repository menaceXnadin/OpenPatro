using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace OpenPatro;

/// <summary>
/// A tiny, invisible window whose sole purpose is to keep the WinUI dispatcher
/// alive when all other application windows are hidden or parked off-screen.
///
/// Without this, WinUI 3 may shut down the dispatcher message loop when it decides
/// no windows are "visible," which causes H.NotifyIcon left-click commands to stop
/// firing entirely.
///
/// The window is 1×1 pixel, fully transparent, positioned at (0,0), styled as a
/// Win32 tool window (no taskbar entry, not shown in Alt+Tab), and never receives
/// focus or activation.
/// </summary>
public sealed class DispatcherKeepaliveWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const byte ZeroAlpha = 0;
    private const uint LwaAlpha = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    private bool _isCreated;

    /// <summary>
    /// Creates the window and makes it invisible. Call this once during app startup.
    /// </summary>
    public void Create()
    {
        if (_isCreated)
        {
            return;
        }

        _isCreated = true;

        // Make it tiny.
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(0, 0, 1, 1));

        // Remove from switchers / taskbar.
        AppWindow.IsShownInSwitchers = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Show the window (this creates the HWND and registers it with WinUI).
        // Use the H.NotifyIcon-style Show that disables efficiency mode.
        H.NotifyIcon.WindowExtensions.Show(this, disableEfficiencyMode: true);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Set extended styles: tool window + layered + transparent to input + no-activate.
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow | WsExLayered | WsExTransparent | WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle));

        // Make the window fully transparent (alpha = 0).
        SetLayeredWindowAttributes(hwnd, 0, ZeroAlpha, LwaAlpha);
    }
}
