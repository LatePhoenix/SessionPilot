namespace SessionPilot.Core;

public static class ProcessorTopologyReader
{
    private const int RelationProcessorCore = 0;
    private const int RelationNumaNode = 1;
    private const int RelationCache = 2;
    private const int RelationGroup = 4;
    private const int RelationNumaNodeEx = 6;
    private const int GroupAffinitySize = 16;
    private const int GroupInfoSize = 48;

    public static HardwareInventory Read(ReadOnlySpan<byte> logicalInfo, ReadOnlySpan<byte> cpuSets, string? processorName)
    {
        var ambiguities = new List<string>();
        var cores = new List<CoreBits>();
        var groups = new List<GroupBits>();
        var caches = new List<CacheRecord>();
        var numa = new List<NumaRecord>();
        var incomplete = false;
        var offset = 0;
        while (offset + 8 <= logicalInfo.Length)
        {
            var relationship = BitConverter.ToInt32(logicalInfo.Slice(offset, 4));
            var size = BitConverter.ToInt32(logicalInfo.Slice(offset + 4, 4));
            if (size < 8 || offset + size > logicalInfo.Length)
            {
                incomplete = true;
                ambiguities.Add("A processor-information record was truncated.");
                break;
            }

            var union = logicalInfo.Slice(offset + 8, size - 8);
            switch (relationship)
            {
                case RelationProcessorCore:
                    ReadCore(union, cores);
                    break;
                case RelationGroup:
                    ReadGroup(union, groups);
                    break;
                case RelationCache:
                    ReadCache(union, caches);
                    break;
                case RelationNumaNode:
                case RelationNumaNodeEx:
                    ReadNuma(union, numa);
                    break;
            }

            offset += size;
        }

        if (incomplete)
        {
            ambiguities.Add("Topology data was incomplete.");
        }

        var logical = new List<LogicalProcessorRecord>();
        if (groups.Count > 0)
        {
            foreach (var group in groups)
            {
                AddBits(logical, group.Group, group.Mask, null);
            }
        }
        else
        {
            ambiguities.Add("Processor group information was missing. Logical processors were taken from core records.");
            foreach (var core in cores)
            {
                AddBits(logical, core.Group, core.Mask, core.EfficiencyClass);
            }
        }

        var classes = cores.Select(core => core.EfficiencyClass).Distinct().ToList();
        var heterogeneous = classes.Count > 1;
        var singleMask = groups.Count == 1 && groups[0].ActiveCount is > 0 and <= 64;
        if (!singleMask)
        {
            ambiguities.Add("A single 64-bit affinity mask does not cover this machine, or group information was not established.");
        }

        if (!heterogeneous)
        {
            ambiguities.Add("No mixed efficiency classes were reported. P-core and E-core numbering is not assumed.");
        }

        ambiguities.Add("A cache index is not treated as a gaming CCD recommendation.");
        var sets = ReadCpuSets(cpuSets, ambiguities);
        return new HardwareInventory
        {
            ProcessorName = processorName,
            Confidence = incomplete || logical.Count == 0 ? DiscoveryConfidence.Low : DiscoveryConfidence.Medium,
            HeterogeneousCoresReported = heterogeneous,
            Single64BitMaskCoversMachine = singleMask,
            GroupCount = Math.Max(groups.Count, logical.Select(item => item.Group).Distinct().Count()),
            LogicalProcessorCount = logical.Count,
            Ambiguities = ambiguities,
            LogicalProcessors = logical,
            CpuSets = sets,
            Caches = caches,
            NumaNodes = numa
        };
    }

    private static void ReadCore(ReadOnlySpan<byte> union, List<CoreBits> cores)
    {
        if (union.Length < 24 + GroupAffinitySize)
        {
            return;
        }

        var efficiency = union[1];
        var groupCount = BitConverter.ToUInt16(union.Slice(22, 2));
        var count = groupCount == 0 ? 1 : groupCount;
        for (var i = 0; i < count; i++)
        {
            var at = 24 + (i * GroupAffinitySize);
            if (at + GroupAffinitySize > union.Length)
            {
                break;
            }

            cores.Add(new CoreBits(BitConverter.ToUInt16(union.Slice(at + 8, 2)), BitConverter.ToUInt64(union.Slice(at, 8)), efficiency));
        }
    }

    private static void ReadGroup(ReadOnlySpan<byte> union, List<GroupBits> groups)
    {
        if (union.Length < 24)
        {
            return;
        }

        var activeGroups = BitConverter.ToUInt16(union.Slice(2, 2));
        for (var i = 0; i < activeGroups; i++)
        {
            var at = 24 + (i * GroupInfoSize);
            if (at + GroupInfoSize > union.Length)
            {
                break;
            }

            groups.Add(new GroupBits(i, union[at + 1], BitConverter.ToUInt64(union.Slice(at + 40, 8))));
        }
    }

    private static void ReadCache(ReadOnlySpan<byte> union, List<CacheRecord> caches)
    {
        if (union.Length < 32 + GroupAffinitySize)
        {
            return;
        }

        var level = union[0];
        var size = BitConverter.ToUInt32(union.Slice(4, 4));
        var group = BitConverter.ToUInt16(union.Slice(32 + 8, 2));
        caches.Add(new CacheRecord { Level = level, SizeBytes = size, Group = group });
    }

    private static void ReadNuma(ReadOnlySpan<byte> union, List<NumaRecord> nodes)
    {
        if (union.Length < 24 + GroupAffinitySize)
        {
            return;
        }

        var node = BitConverter.ToUInt32(union.Slice(0, 4));
        var groupCount = BitConverter.ToUInt16(union.Slice(22, 2));
        var count = groupCount == 0 ? 1 : groupCount;
        for (var i = 0; i < count; i++)
        {
            var at = 24 + (i * GroupAffinitySize);
            if (at + GroupAffinitySize > union.Length)
            {
                break;
            }

            nodes.Add(new NumaRecord { NodeNumber = node, Group = BitConverter.ToUInt16(union.Slice(at + 8, 2)) });
        }
    }

    private static List<CpuSetRecord> ReadCpuSets(ReadOnlySpan<byte> bytes, List<string> ambiguities)
    {
        var sets = new List<CpuSetRecord>();
        var offset = 0;
        while (offset + 8 <= bytes.Length)
        {
            var size = BitConverter.ToInt32(bytes.Slice(offset, 4));
            var type = BitConverter.ToInt32(bytes.Slice(offset + 4, 4));
            if (size < 8 || offset + size > bytes.Length)
            {
                ambiguities.Add("A CPU Set record was truncated.");
                break;
            }

            if (type == 0 && size >= 32)
            {
                sets.Add(new CpuSetRecord
                {
                    Id = BitConverter.ToUInt32(bytes.Slice(offset + 8, 4)),
                    Group = BitConverter.ToUInt16(bytes.Slice(offset + 12, 2)),
                    LogicalProcessorIndex = bytes[offset + 14],
                    CoreIndex = bytes[offset + 15],
                    LastLevelCacheIndex = bytes[offset + 16],
                    NumaNodeIndex = bytes[offset + 17],
                    EfficiencyClass = bytes[offset + 18]
                });
            }

            offset += size;
        }

        return sets;
    }

    private static void AddBits(List<LogicalProcessorRecord> target, int group, ulong mask, int? efficiency)
    {
        for (var bit = 0; bit < 64; bit++)
        {
            if ((mask & (1UL << bit)) != 0)
            {
                target.Add(new LogicalProcessorRecord
                {
                    Group = group,
                    IndexInGroup = bit,
                    EfficiencyClass = efficiency
                });
            }
        }
    }

    private sealed record CoreBits(int Group, ulong Mask, int EfficiencyClass);
    private sealed record GroupBits(int Group, int ActiveCount, ulong Mask);
}
