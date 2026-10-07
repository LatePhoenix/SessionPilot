namespace SessionPilot.Core;

/// <summary>
/// Lists installed Ollama model names from the manifests folder. Read-only, names only.
/// Layout: manifests/&lt;registry&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;.
/// </summary>
public static class OllamaModels
{
    private const string DefaultRegistry = "registry.ollama.ai";
    private const string DefaultNamespace = "library";

    public static string DefaultDirectory() =>
        Environment.GetEnvironmentVariable("OLLAMA_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");

    public static IReadOnlyList<string> Installed(string modelsDirectory)
    {
        var manifests = Path.Combine(modelsDirectory, "manifests");
        try
        {
            if (!Directory.Exists(manifests))
            {
                return [];
            }

            return Directory.EnumerateFiles(manifests, "*", SearchOption.AllDirectories)
                .Select(path => Name(Path.GetRelativePath(manifests, path)))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static string? Name(string relativePath)
    {
        var parts = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return null;
        }

        var model = parts[2] + (parts[3] == "latest" ? "" : ":" + parts[3]);
        if (parts[0] != DefaultRegistry)
        {
            return parts[0] + "/" + parts[1] + "/" + model;
        }

        return parts[1] == DefaultNamespace ? model : parts[1] + "/" + model;
    }
}
