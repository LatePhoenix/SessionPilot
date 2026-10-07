using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using SessionPilot.Core;
using SessionPilot.Infrastructure;

namespace SessionPilot.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(argument => string.Equals(argument, "--check", StringComparison.OrdinalIgnoreCase)))
        {
            var report = CheckReport.Format(LiveDiscovery.CheckOnce());
            ParentConsole.Write(report);
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }
}

internal static class ParentConsole
{
    public static void Write(string text)
    {
        var handle = GetStdHandle(-11);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            AttachConsole(-1);
            handle = GetStdHandle(-11);
        }

        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        WriteFile(handle, bytes, bytes.Length, out _, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr handle, byte[] buffer, int count, out int written, IntPtr overlapped);
}
