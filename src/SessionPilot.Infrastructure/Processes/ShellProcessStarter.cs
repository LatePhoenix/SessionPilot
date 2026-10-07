using System.Diagnostics;
using SessionPilot.Core;

namespace SessionPilot.Infrastructure;

public sealed class ShellProcessStarter : IProcessStarter
{
    public bool Start(string pathOrUri, IReadOnlyList<string> arguments)
    {
        try
        {
            return Process.Start(CreateStartInfo(pathOrUri, arguments)) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static ProcessStartInfo CreateStartInfo(string pathOrUri, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = pathOrUri,
            UseShellExecute = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}

public sealed class LiveWindowCloser(Process process) : ICloseRequest
{
    public bool HasMainWindow => process.MainWindowHandle != IntPtr.Zero;

    public bool TryCloseMainWindow() => process.CloseMainWindow();
}
