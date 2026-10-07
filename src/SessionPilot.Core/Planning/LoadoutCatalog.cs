using System.Text.Json;
using System.Text.Json.Serialization;

namespace SessionPilot.Core;

public sealed class LoadoutCatalog
{
    private readonly Dictionary<string, Loadout> _loadouts;

    private LoadoutCatalog(IEnumerable<Loadout> loadouts, IReadOnlyList<string> loadErrors)
    {
        _loadouts = loadouts.ToDictionary(loadout => loadout.Id, StringComparer.OrdinalIgnoreCase);
        LoadErrors = loadErrors;
    }

    public IReadOnlyList<Loadout> All => _loadouts.Values.OrderBy(loadout => loadout.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> LoadErrors { get; }

    public bool TryGet(string id, out Loadout loadout) => _loadouts.TryGetValue(id, out loadout!);

    public static LoadoutCatalog Load(string builtInDirectory, string? userDirectory = null)
    {
        var loadouts = ReadDirectory(builtInDirectory, builtIn: true).ToList();
        var ids = new HashSet<string>(loadouts.Select(loadout => loadout.Id), StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        if (userDirectory is not null && Directory.Exists(userDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(userDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Loadout loadout;
                try
                {
                    loadout = ParseLoadout(File.ReadAllText(path), builtIn: false, path);
                }
                catch (JsonException)
                {
                    errors.Add(Path.GetFileName(path) + ": The file is not valid JSON.");
                    continue;
                }
                catch (InvalidDataException exception)
                {
                    errors.Add(Path.GetFileName(path) + ": " + exception.Message);
                    continue;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add(Path.GetFileName(path) + ": The file could not be read.");
                    continue;
                }

                if (!ids.Add(loadout.Id))
                {
                    errors.Add(Path.GetFileName(path) + ": User loadout '" + loadout.Id + "' collides with an existing id.");
                    continue;
                }

                loadouts.Add(loadout);
            }
        }

        Require(loadouts, "balanced");
        return new LoadoutCatalog(loadouts, errors);
    }

    public Loadout Clone(string sourceId, string newId, string displayName)
    {
        if (!TryGet(sourceId, out var source))
        {
            throw new InvalidOperationException($"Unknown loadout '{sourceId}'.");
        }

        var validation = LoadoutValidator.ValidateId(newId);
        if (validation is not null)
        {
            throw new InvalidOperationException(validation);
        }

        if (_loadouts.ContainsKey(newId))
        {
            throw new InvalidOperationException($"Loadout '{newId}' already exists.");
        }

        return Copy(source, newId, displayName, builtIn: false, sourcePath: null);
    }

    public static void Save(Loadout loadout, string directory)
    {
        if (loadout.BuiltIn)
        {
            throw new InvalidOperationException("Built-in loadouts are read-only.");
        }

        var error = LoadoutValidator.Validate(loadout);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, loadout.Id + ".json");
        var json = JsonSerializer.Serialize(ToDocument(loadout), JsonOptions);
        File.WriteAllText(path, json);
    }

    private static IEnumerable<Loadout> ReadDirectory(string directory, bool builtIn)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            Loadout loadout;
            try
            {
                loadout = ParseLoadout(File.ReadAllText(path), builtIn, path);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"{path}: {exception.Message}", exception);
            }

            yield return loadout;
        }
    }

    private static Loadout ParseLoadout(string json, bool builtIn, string? path)
    {
        var document = JsonSerializer.Deserialize<LoadoutDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty loadout file.");
        var loadout = FromDocument(document, builtIn, path);
        var error = LoadoutValidator.Validate(loadout);
        if (error is not null)
        {
            throw new InvalidDataException(error);
        }

        return loadout;
    }

    private static void Require(IReadOnlyList<Loadout> loadouts, string id)
    {
        if (loadouts.All(loadout => !string.Equals(loadout.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Built-in loadout '{id}' is missing.");
        }
    }

    private static Loadout Copy(Loadout source, string id, string displayName, bool builtIn, string? sourcePath) => new()
    {
        Id = id,
        DisplayName = displayName,
        Summary = source.Summary,
        Objective = source.Objective,
        SessionMode = source.SessionMode,
        PowerPreference = source.PowerPreference,
        BackgroundPolicy = source.BackgroundPolicy,
        OfferPerformanceMode = source.OfferPerformanceMode,
        OfferEfficiencyModeOff = source.OfferEfficiencyModeOff,
        PriorityPolicy = source.PriorityPolicy,
        CpuPlacementPolicy = source.CpuPlacementPolicy,
        ProBalancePolicy = source.ProBalancePolicy,
        RequiresExplicitWorkers = source.RequiresExplicitWorkers,
        ParticipantRoles = source.ParticipantRoles.ToList(),
        Notes = source.Notes.ToList(),
        BuiltIn = builtIn,
        SourcePath = sourcePath
    };

    internal static Loadout FromDocument(LoadoutDocument document, bool builtIn, string? path) => new()
    {
        SchemaVersion = document.SchemaVersion,
        Id = document.Id ?? "",
        DisplayName = document.DisplayName ?? "",
        Summary = document.Summary ?? "",
        Objective = document.Objective ?? "",
        SessionMode = document.SessionMode ?? "",
        PowerPreference = document.PowerPreference ?? "",
        BackgroundPolicy = document.BackgroundPolicy ?? "",
        OfferPerformanceMode = document.OfferPerformanceMode,
        OfferEfficiencyModeOff = document.OfferEfficiencyModeOff,
        PriorityPolicy = document.PriorityPolicy ?? "",
        CpuPlacementPolicy = document.CpuPlacementPolicy ?? "",
        ProBalancePolicy = document.ProBalancePolicy ?? "",
        RequiresExplicitWorkers = document.RequiresExplicitWorkers,
        ParticipantRoles = document.ParticipantRoles ?? [],
        Notes = document.Notes ?? [],
        BuiltIn = builtIn,
        SourcePath = path
    };

    private static LoadoutDocument ToDocument(Loadout loadout) => new()
    {
        SchemaVersion = Schema.Current,
        Id = loadout.Id,
        DisplayName = loadout.DisplayName,
        Summary = loadout.Summary,
        Objective = loadout.Objective,
        SessionMode = loadout.SessionMode,
        PowerPreference = loadout.PowerPreference,
        BackgroundPolicy = loadout.BackgroundPolicy,
        OfferPerformanceMode = loadout.OfferPerformanceMode,
        OfferEfficiencyModeOff = loadout.OfferEfficiencyModeOff,
        PriorityPolicy = Names.Unchanged,
        CpuPlacementPolicy = Names.Unchanged,
        ProBalancePolicy = Names.Preserve,
        RequiresExplicitWorkers = loadout.RequiresExplicitWorkers,
        ParticipantRoles = loadout.ParticipantRoles.ToList(),
        Notes = loadout.Notes.ToList()
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

public static class LoadoutValidator
{
    private static readonly HashSet<string> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "compositor", "streaming", "audio", "companion"
    };

    public static string? ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > IntentLimits.MaxLoadoutIdLength)
        {
            return "Loadout id is missing or too long.";
        }

        if (!id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            return "Loadout id must contain only letters, digits, and hyphens.";
        }

        return null;
    }

    public static string? Validate(Loadout loadout)
    {
        if (loadout.SchemaVersion != Schema.Current)
        {
            return "Unsupported loadout schemaVersion.";
        }

        var idError = ValidateId(loadout.Id);
        if (idError is not null)
        {
            return idError;
        }

        if (!Names.TryParseObjective(loadout.Objective, out _))
        {
            return "Unknown objective.";
        }

        if (!Names.TryParseSession(loadout.SessionMode, out _))
        {
            return "Unknown sessionMode.";
        }

        if (!Names.TryParsePower(loadout.PowerPreference, out _))
        {
            return "Unknown powerPreference.";
        }

        if (!Names.TryParseBackground(loadout.BackgroundPolicy, out _))
        {
            return "Unknown backgroundPolicy.";
        }

        if (!string.Equals(loadout.PriorityPolicy, Names.Unchanged, StringComparison.OrdinalIgnoreCase))
        {
            return "priorityPolicy must stay 'unchanged'.";
        }

        if (!string.Equals(loadout.CpuPlacementPolicy, Names.Unchanged, StringComparison.OrdinalIgnoreCase))
        {
            return "cpuPlacementPolicy must stay 'unchanged'.";
        }

        if (!string.Equals(loadout.ProBalancePolicy, Names.Preserve, StringComparison.OrdinalIgnoreCase))
        {
            return "proBalancePolicy must stay 'preserve'.";
        }

        if (loadout.ParticipantRoles.Any(role => !Roles.Contains(role)))
        {
            return "participantRoles contains an unknown role.";
        }

        return null;
    }
}

public sealed class LoadoutDocument
{
    public int SchemaVersion { get; set; }
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Summary { get; set; }
    public string? Objective { get; set; }
    public string? SessionMode { get; set; }
    public string? PowerPreference { get; set; }
    public string? BackgroundPolicy { get; set; }
    public bool OfferPerformanceMode { get; set; }
    public bool OfferEfficiencyModeOff { get; set; }
    public string? PriorityPolicy { get; set; }
    public string? CpuPlacementPolicy { get; set; }
    public string? ProBalancePolicy { get; set; }
    public bool RequiresExplicitWorkers { get; set; }
    public List<string>? ParticipantRoles { get; set; }
    public List<string>? Notes { get; set; }
}
