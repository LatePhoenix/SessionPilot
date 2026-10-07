using System.Text.Json;
using SessionPilot.Core;

namespace SessionPilot.Infrastructure;

public sealed record AppState
{
    public string PowerOwner { get; init; } = nameof(PowerOwnerKind.Unset);
    public IReadOnlyList<MeasurementRun> Measurements { get; init; } = [];
}

public static class AppStateStore
{
    public static AppState Load(string path, out string? note)
    {
        note = null;
        if (!File.Exists(path))
        {
            return new AppState();
        }

        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllBytes(path));
            if (state is not null)
            {
                return Normalize(state);
            }

            note = SetAside(path, "The file is not valid JSON.");
        }
        catch (JsonException)
        {
            note = SetAside(path, "The file is not valid JSON.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            note = Path.GetFileName(path) + ": The file could not be read. Defaults were used.";
        }

        return new AppState();
    }

    /// <summary>
    /// Moves an unparseable state file aside so the next save cannot overwrite what was in it.
    /// The note is shown once, because the file is gone on the next start.
    /// </summary>
    private static string SetAside(string path, string reason)
    {
        var name = Path.GetFileName(path);
        var aside = path + ".unreadable";
        try
        {
            File.Move(path, aside, overwrite: true);
            return name + ": " + reason + " It was moved to " + Path.GetFileName(aside) + " and defaults were used.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return name + ": " + reason + " Defaults were used.";
        }
    }

    public static void Save(string path, AppState state)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("State path has no directory.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, ".sessionpilot-" + Guid.NewGuid().ToString("n") + ".tmp");
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(Normalize(state)));
        try
        {
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.SetAttributes(temp, FileAttributes.Normal);
                    File.Delete(temp);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public static bool TryParseOwner(string? value, out PowerOwnerKind owner) =>
        Enum.TryParse(value, ignoreCase: false, out owner) && Enum.IsDefined(owner);

    private static AppState Normalize(AppState state)
    {
        var owner = TryParseOwner(state.PowerOwner, out var parsed) ? parsed : PowerOwnerKind.Unset;
        return new AppState
        {
            PowerOwner = owner.ToString(),
            Measurements = (state.Measurements ?? []).Select(run => run with { PerformanceEffect = "not-measured" }).ToList()
        };
    }
}
