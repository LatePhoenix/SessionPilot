using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SessionPilot.App;

/// <summary>Asks DWM for a dark title bar to match the window. Older Windows builds ignore it.</summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
