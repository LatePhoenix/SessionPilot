namespace SessionPilot.Tests;

public class TopologyAndTriggerTests
{
    [Fact]
    public void TwoProcessorGroups_DoNotClaimASingleMask()
    {
        var inventory = ProcessorTopologyReader.Read(TwoGroups(), [], "Synthetic CPU");
        Assert.False(inventory.Single64BitMaskCoversMachine);
        Assert.Equal(2, inventory.GroupCount);
        Assert.Contains(inventory.Ambiguities, item => item.Contains("64-bit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inventory.Ambiguities, item => item.Contains("CCD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MixedEfficiencyClasses_AreReportedWithoutCoreNumberAssumptions()
    {
        var inventory = ProcessorTopologyReader.Read(HeterogeneousCores(), CpuSet(), "Synthetic CPU");
        Assert.True(inventory.HeterogeneousCoresReported);
        Assert.Equal(2, inventory.LogicalProcessorCount);
        var set = Assert.Single(inventory.CpuSets);
        Assert.Equal(7u, set.Id);
        Assert.DoesNotContain(inventory.Ambiguities, item => item.Contains("E-core 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ManualOverride_BeatsAHigherSignal()
    {
        var decision = TriggerEngine.Evaluate(
            new TriggerSnapshot { ManualOverrideActive = true, ManualLoadoutId = "balanced", SignaledLoadouts = ["vrchat-steamvr"] },
            new TriggerOptions { OptedIn = true },
            DateTimeOffset.Parse("2026-10-07T12:00:00Z"));
        Assert.Equal("hold", decision.Action);
        Assert.Equal("balanced", decision.LoadoutId);
    }

    [Fact]
    public void Signal_WaitsForDwell_ThenSuggestsWithoutApplying()
    {
        var start = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var options = new TriggerOptions { OptedIn = true };
        var first = TriggerEngine.Evaluate(new TriggerSnapshot { SignaledLoadouts = ["desktop-gaming", "media-playback"] }, options, start);
        Assert.Equal("wait", first.Action);
        Assert.Equal("desktop-gaming", first.PendingLoadoutId);
        var early = TriggerEngine.Evaluate(
            new TriggerSnapshot { SignaledLoadouts = ["desktop-gaming"], PendingLoadoutId = "desktop-gaming", PendingSince = start },
            options,
            start.AddSeconds(6));
        Assert.Equal("wait", early.Action);
        var ready = TriggerEngine.Evaluate(
            new TriggerSnapshot { SignaledLoadouts = ["desktop-gaming"], PendingLoadoutId = "desktop-gaming", PendingSince = start },
            options,
            start.AddSeconds(20));
        Assert.Equal("suggest", ready.Action);
        Assert.Equal("desktop-gaming", ready.LoadoutId);
        Assert.Contains("approved", ready.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptOut_DoesNotSuggest()
    {
        var decision = TriggerEngine.Evaluate(
            new TriggerSnapshot { SignaledLoadouts = ["vrchat-steamvr"] },
            new TriggerOptions { OptedIn = false },
            DateTimeOffset.UnixEpoch);
        Assert.Equal("hold", decision.Action);
    }

    [Fact]
    public void ExitGrace_DelaysBalancedSuggestion()
    {
        var ended = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var options = new TriggerOptions { OptedIn = true, ExitGrace = TimeSpan.FromSeconds(45) };
        var during = TriggerEngine.Evaluate(
            new TriggerSnapshot { ActiveLoadoutId = "desktop-gaming", SessionEndedAt = ended, SignaledLoadouts = [] },
            options,
            ended.AddSeconds(10));
        Assert.Equal("hold", during.Action);
        var after = TriggerEngine.Evaluate(
            new TriggerSnapshot { ActiveLoadoutId = "desktop-gaming", SessionEndedAt = ended, SignaledLoadouts = [] },
            options,
            ended.AddSeconds(46));
        Assert.Equal("balanced", after.LoadoutId);
    }

    private static byte[] TwoGroups()
    {
        var bytes = new byte[8 + 24 + (48 * 2)];
        WriteInt(bytes, 0, 4);
        WriteInt(bytes, 4, bytes.Length);
        WriteUShort(bytes, 8 + 2, 2);
        WriteGroup(bytes, 8 + 24, 0x3);
        WriteGroup(bytes, 8 + 24 + 48, 0x1);
        return bytes;
    }

    private static byte[] HeterogeneousCores()
    {
        var group = GroupRecord(1, 0x3);
        var first = CoreRecord(0, 0x1);
        var second = CoreRecord(1, 0x2);
        return group.Concat(first).Concat(second).ToArray();
    }

    private static byte[] GroupRecord(ushort activeGroups, ulong mask)
    {
        var bytes = new byte[8 + 24 + 48];
        WriteInt(bytes, 0, 4);
        WriteInt(bytes, 4, bytes.Length);
        WriteUShort(bytes, 8 + 2, activeGroups);
        WriteGroup(bytes, 8 + 24, mask);
        return bytes;
    }

    private static byte[] CoreRecord(byte efficiency, ulong mask)
    {
        var bytes = new byte[8 + 24 + 16];
        WriteInt(bytes, 0, 0);
        WriteInt(bytes, 4, bytes.Length);
        bytes[8 + 1] = efficiency;
        WriteUShort(bytes, 8 + 22, 1);
        WriteULong(bytes, 8 + 24, mask);
        return bytes;
    }

    private static byte[] CpuSet()
    {
        var bytes = new byte[32];
        WriteInt(bytes, 0, 32);
        WriteInt(bytes, 4, 0);
        WriteInt(bytes, 8, 7);
        return bytes;
    }

    private static void WriteGroup(byte[] bytes, int at, ulong mask)
    {
        bytes[at + 1] = 2;
        WriteULong(bytes, at + 40, mask);
    }

    private static void WriteInt(byte[] bytes, int at, int value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);
    private static void WriteUShort(byte[] bytes, int at, ushort value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);
    private static void WriteULong(byte[] bytes, int at, ulong value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);
}
