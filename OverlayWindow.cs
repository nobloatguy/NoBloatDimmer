using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace NoBloatDimmer;

internal sealed class OverlayWindow : Window
{
    private nint _handle;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Black;
        Topmost = true;
        Width = 1;
        Height = 1;
        Opacity = 0;

        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeOverlayClickThrough(_handle);
        };
    }

    public void Apply(Screen screen, int dimPercent)
    {
        if (dimPercent <= 0)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        // 100% is intentional: it creates a fully black overlay.
        // The app registers Alt + Shift + 0 as a global recovery shortcut.
        Opacity = Math.Clamp(dimPercent, 0, 100) / 100d;

        if (!IsVisible)
        {
            Show();
        }

        _handle = new WindowInteropHelper(this).Handle;
        var bounds = screen.Bounds;

        NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public void Remove()
    {
        try
        {
            Close();
        }
        catch
        {
            // Shutdown should not be blocked by a window already in teardown.
        }
    }
}

