namespace SessionPilot.Infrastructure;

public static class AppPaths
{
    public const string FolderName = "SessionPilot";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static string JournalDirectory => Path.Combine(DataDirectory, "journals");

    public static string LoadoutDirectory => Path.Combine(DataDirectory, "loadouts");
}
