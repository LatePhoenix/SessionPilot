using System.Text.Json;

namespace SessionPilot.Core;

public interface IFileReplacer
{
    void Replace(string targetPath, byte[] contents);
}

public sealed class SameVolumeFileReplacer : IFileReplacer
{
    public void Replace(string targetPath, byte[] contents)
    {
        var directory = Path.GetDirectoryName(targetPath) ?? throw new InvalidOperationException("Target has no directory.");
        var temp = Path.Combine(directory, ".sessionpilot-" + Guid.NewGuid().ToString("n") + ".tmp");
        File.WriteAllBytes(temp, contents);
        try
        {
            var attributes = File.GetAttributes(targetPath);
            File.SetAttributes(temp, attributes);
            File.Replace(temp, targetPath, null);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}

public sealed record ApplyRequest
{
    public required string TargetPath { get; init; }
    public required string ExpectedBaselineHash { get; init; }
    public required bool Approved { get; init; }
    public required string JournalDirectory { get; init; }
    public required IReadOnlyList<IniEdit> Edits { get; init; }
    public IReadOnlyList<string> LiveCandidatePaths { get; init; } = [];
    public string? PlanId { get; init; }
}

public sealed class TransactionCoordinator
{
    private readonly IFileReplacer _replacer;

    public TransactionCoordinator(IFileReplacer? replacer = null)
    {
        _replacer = replacer ?? new SameVolumeFileReplacer();
    }

    public ApplyTransaction Apply(ApplyRequest request)
    {
        var id = Guid.NewGuid().ToString("n");
        if (!request.Approved)
        {
            return Refuse(id, request.TargetPath, "The plan was not approved.");
        }

        if (!TryNormalize(request.TargetPath, out var targetFullPath))
        {
            return Refuse(id, request.TargetPath, "The target path is not valid.");
        }

        if (IsLivePath(request, targetFullPath) && !LiveApplyPolicy.Enabled)
        {
            return Refuse(id, request.TargetPath, LiveApplyPolicy.Reason);
        }

        Directory.CreateDirectory(request.JournalDirectory);

        if (request.Edits.Count == 0)
        {
            return Refuse(id, request.TargetPath, "The plan has no writable edits. Dry-run approval does not change a file.");
        }

        var lockPath = Path.Combine(request.JournalDirectory, "apply.lock");
        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return Refuse(id, request.TargetPath, "Another SessionPilot transaction holds the application lock. Process Lasso itself is not locked.");
        }

        using (lockStream)
        {
            var journal = NewJournal(id, request);
            var journalPath = JournalPath(request.JournalDirectory, id);
            try
            {
                var baseline = File.ReadAllBytes(request.TargetPath);
                var baselineHash = ContentHashing.Sha256(baseline);
                journal.BaselineHash = baselineHash;
                journal.State = "BaselineRecorded";
                Save(journal, journalPath);
                if (!string.Equals(baselineHash, request.ExpectedBaselineHash, StringComparison.OrdinalIgnoreCase))
                {
                    journal.State = "Failed";
                    journal.Message = "The file changed after the plan was created.";
                    Save(journal, journalPath);
                    return FromJournal(journal, journalPath, "failed", journal.Message, "not-written");
                }

                var document = IniDocument.Parse(baseline);
                var edited = document.Apply(request.Edits);
                if (!edited.Succeeded)
                {
                    journal.State = "Failed";
                    journal.Message = edited.Message;
                    Save(journal, journalPath);
                    return FromJournal(journal, journalPath, "failed", edited.Message, "not-written");
                }

                var intended = edited.Document.Serialize();
                journal.IntendedHash = ContentHashing.Sha256(intended);
                journal.OwnedValues = CaptureOwned(document, edited.Document, request.Edits);
                var backupPath = Path.Combine(request.JournalDirectory, id + ".baseline");
                File.WriteAllBytes(backupPath, baseline);
                journal.BackupPath = backupPath;
                journal.State = "BackupWritten";
                Save(journal, journalPath);

                var again = ContentHashing.Sha256(File.ReadAllBytes(request.TargetPath));
                if (!string.Equals(again, baselineHash, StringComparison.OrdinalIgnoreCase))
                {
                    journal.State = "NeedsReconciliation";
                    journal.Message = "The file changed after the backup was saved and before replacement.";
                    Save(journal, journalPath);
                    return FromJournal(journal, journalPath, "needs-reconciliation", journal.Message, "not-written");
                }

                journal.State = "Replacing";
                Save(journal, journalPath);
                _replacer.Replace(request.TargetPath, intended);
                var observed = File.ReadAllBytes(request.TargetPath);
                var observedHash = ContentHashing.Sha256(observed);
                journal.ObservedHash = observedHash;
                if (!string.Equals(observedHash, journal.IntendedHash, StringComparison.OrdinalIgnoreCase))
                {
                    journal.State = "Failed";
                    journal.Message = "Replacement finished but the file bytes do not match the intended document. No automatic rollback was performed.";
                    Save(journal, journalPath);
                    return FromJournal(journal, journalPath, "failed", journal.Message, "verify-failed");
                }

                journal.State = "Completed";
                journal.Message = "Isolated file content matches the approved bytes. Governor consumption is not applicable to this target.";
                Save(journal, journalPath);
                return FromJournal(journal, journalPath, "completed-isolated", journal.Message, "verified");
            }
            catch (Exception exception)
            {
                journal.State = "Failed";
                journal.Message = exception.Message;
                Save(journal, journalPath);
                return FromJournal(journal, journalPath, "failed", exception.Message, "not-written");
            }
        }
    }

    public static string JournalPath(string directory, string id) => Path.Combine(directory, id + ".json");

    private static bool TryNormalize(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            fullPath = "";
            return false;
        }
    }

    private static bool IsLivePath(ApplyRequest request, string targetFullPath) =>
        request.LiveCandidatePaths.Any(path =>
            TryNormalize(path, out var candidate) &&
            string.Equals(candidate, targetFullPath, StringComparison.OrdinalIgnoreCase));

    private static JournalRecord NewJournal(string id, ApplyRequest request) => new()
    {
        SchemaVersion = Schema.Current,
        Id = id,
        PlanId = request.PlanId,
        TargetPath = request.TargetPath,
        State = "Preparing",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static List<OwnedValue> CaptureOwned(IniDocument baseline, IniDocument written, IReadOnlyList<IniEdit> edits)
    {
        var owned = new List<OwnedValue>();
        foreach (var edit in edits)
        {
            var before = baseline.Find(edit.Section, edit.Key);
            var after = written.Find(edit.Section, edit.Key);
            owned.Add(new OwnedValue
            {
                Section = edit.Section,
                Key = edit.Key,
                BaselineValue = before.Line?.Value ?? "",
                WrittenValue = after.Line?.Value ?? edit.Value
            });
        }

        return owned;
    }

    private static ApplyTransaction Refuse(string id, string target, string message) => new()
    {
        Id = id,
        Status = "refused",
        Message = message,
        TargetPath = target,
        PersistenceStatus = "not-written",
        GovernorStatus = "not-verified",
        EffectiveStatus = "not-observed",
        PerformanceStatus = "not-measured"
    };

    private static ApplyTransaction FromJournal(JournalRecord journal, string journalPath, string status, string message, string persistence) => new()
    {
        Id = journal.Id,
        Status = status,
        Message = message,
        TargetPath = journal.TargetPath,
        BaselineHash = journal.BaselineHash,
        IntendedHash = journal.IntendedHash,
        ObservedHash = journal.ObservedHash,
        BackupPath = journal.BackupPath,
        JournalPath = journalPath,
        PersistenceStatus = persistence,
        GovernorStatus = status == "completed-isolated" ? "not-applicable" : "not-verified",
        EffectiveStatus = "not-observed",
        PerformanceStatus = "not-measured",
        OwnedValues = journal.OwnedValues,
        CreatedUtc = journal.CreatedUtc
    };

    internal static void Save(JournalRecord journal, string path)
    {
        journal.UpdatedUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(path, JsonSerializer.Serialize(journal, Json));
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed class JournalRecord
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string? PlanId { get; set; }
    public string TargetPath { get; set; } = "";
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
    public string? BaselineHash { get; set; }
    public string? IntendedHash { get; set; }
    public string? ObservedHash { get; set; }
    public string? BackupPath { get; set; }
    public List<OwnedValue> OwnedValues { get; set; } = [];
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed record JournalScan
{
    public IReadOnlyList<JournalRecord> Incomplete { get; init; } = [];
    public IReadOnlyList<string> UnreadableFileNames { get; init; } = [];
}

public static class JournalRecovery
{
    public static IReadOnlyList<JournalRecord> FindIncomplete(string directory) => Scan(directory).Incomplete;

    public static JournalScan Scan(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new JournalScan();
        }

        var incomplete = new List<JournalRecord>();
        var unreadable = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<JournalRecord>(File.ReadAllText(path), TransactionCoordinator.Json);
                if (record is not null && record.State is not ("Completed" or "Failed" or "Refused"))
                {
                    incomplete.Add(record);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                unreadable.Add(Path.GetFileName(path));
            }
        }

        return new JournalScan { Incomplete = incomplete, UnreadableFileNames = unreadable };
    }

    public static string Describe(JournalRecord journal)
    {
        if (!File.Exists(journal.TargetPath))
        {
            return "The target file is missing. No rollback was applied.";
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(journal.TargetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "The target could not be read. No rollback was applied.";
        }

        var hash = ContentHashing.Sha256(bytes);
        if (journal.BaselineHash is not null && string.Equals(hash, journal.BaselineHash, StringComparison.OrdinalIgnoreCase))
        {
            return "The target still matches the baseline. Replacement does not appear to have landed. No rollback was applied.";
        }

        if (journal.IntendedHash is not null && string.Equals(hash, journal.IntendedHash, StringComparison.OrdinalIgnoreCase))
        {
            return "The target matches the intended bytes. A newer configuration was not rolled back.";
        }

        return "The target matches neither the baseline nor the intended bytes. No rollback was applied.";
    }
}

public static class RestorePlanner
{
    public static RestoreAnalysis Analyze(IniDocument baseline, IniDocument written, IniDocument current, IReadOnlyList<OwnedValue> owned)
    {
        var restorable = new List<RestoreAction>();
        var conflicts = new List<RestoreConflict>();
        var already = new List<string>();
        foreach (var item in owned)
        {
            var currentLookup = current.Find(item.Section, item.Key);
            if (currentLookup.Status != IniLookupStatus.Found)
            {
                conflicts.Add(new RestoreConflict
                {
                    Section = item.Section,
                    Key = item.Key,
                    BaselineValue = item.BaselineValue,
                    WrittenValue = item.WrittenValue,
                    CurrentValue = "",
                    Reason = currentLookup.Status == IniLookupStatus.Ambiguous
                        ? "The current key is ambiguous."
                        : "The current key is missing."
                });
                continue;
            }

            var value = currentLookup.Line!.Value;
            if (value == item.WrittenValue)
            {
                restorable.Add(new RestoreAction
                {
                    Section = item.Section,
                    Key = item.Key,
                    FromValue = value,
                    ToValue = item.BaselineValue
                });
            }
            else if (value == item.BaselineValue)
            {
                already.Add($"[{item.Section}] {item.Key}");
            }
            else
            {
                conflicts.Add(new RestoreConflict
                {
                    Section = item.Section,
                    Key = item.Key,
                    BaselineValue = item.BaselineValue,
                    WrittenValue = item.WrittenValue,
                    CurrentValue = value,
                    Reason = "The current value matches neither the app-written value nor the baseline."
                });
            }
        }

        return new RestoreAnalysis
        {
            Restorable = restorable,
            Conflicts = conflicts,
            AlreadyOriginal = already,
            Summary = conflicts.Count == 0
                ? $"{restorable.Count} owned value(s) can be restored. {already.Count} already match the baseline."
                : $"{conflicts.Count} conflict(s) were left untouched."
        };
    }
}
