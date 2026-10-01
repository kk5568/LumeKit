using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinUIEx;
using HWND = global::Windows.Win32.Foundation.HWND;
using PInvoke = global::Windows.Win32.PInvoke;
using SET_WINDOW_POS_FLAGS = global::Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS;
using WINDOW_LONG_PTR_INDEX = global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX;

namespace BluetoothAudioReceiver.Views.Windows;

public sealed partial class WallpaperDimWindow : WindowEx
{
    public WallpaperDimWindow()
    {
        InitializeComponent();

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;

        try
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }
        catch
        {
        }
    }

    public void ShowOverlay(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.0, 0.85);
        DimRectangle.Opacity = opacity;

        try
        {
            AttachToDesktopWorkerW();
            var displayArea = DisplayArea.Primary;
            if (displayArea is not null)
            {
                var bounds = displayArea.WorkArea;
                AppWindow.Resize(new SizeInt32(bounds.Width, bounds.Height));
                AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
            }

            MakeClickThroughAndTopMost();
            if (!Visible)
            {
                Activate();
            }
            else
            {
                AppWindow.Show();
            }
        }
        catch
        {
        }
    }

    public void HideOverlay()
    {
        try
        {
            WindowExtensions.Hide(this);
        }
        catch
        {
        }
    }

    private void MakeClickThroughAndTopMost()
    {
        try
        {
            var hwnd = new HWND(this.GetWindowHandle());
            var exStyle = (int)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            exStyle |= 0x00080000; // WS_EX_LAYERED
            exStyle |= 0x00000020; // WS_EX_TRANSPARENT
            exStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
            PInvoke.SetWindowPos(
                hwnd,
                new HWND(0),
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        }
        catch
        {
        }
    }

    private void AttachToDesktopWorkerW()
    {
        try
        {
            var hwnd = new HWND(this.GetWindowHandle());
            var progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                _ = SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
            }

            var workerw = FindWorkerW();
            if (workerw != IntPtr.Zero)
            {
                SetParent(hwnd, workerw);
            }
        }
        catch
        {
        }
    }

    private static IntPtr FindWorkerW()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                found = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}
