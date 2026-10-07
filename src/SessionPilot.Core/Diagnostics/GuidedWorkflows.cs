namespace SessionPilot.Core;

public sealed record GuidedStep
{
    public required string Title { get; init; }
    public required string Instruction { get; init; }
}

public sealed record GuidedWorkflow
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Mode { get; init; }
    public bool WritesFiles { get; init; }
    public required string Note { get; init; }
    public IReadOnlyList<GuidedStep> Steps { get; init; } = [];
}

public static class GuidedWorkflows
{
    public static IReadOnlyList<GuidedWorkflow> All =>
    [
        VrChat,
        SteamVr,
        VirtualDesktop
    ];

    public static GuidedWorkflow VrChat { get; } = new()
    {
        Id = "vrchat",
        Title = "VRChat",
        Mode = "guided-only",
        WritesFiles = false,
        Note = "Steps follow the public VRChat configuration window. SessionPilot does not write VRChat settings.",
        Steps =
        [
            new GuidedStep { Title = "Avatar rank", Instruction = "In the VRChat configuration window, set the avatar performance rank you want shown." },
            new GuidedStep { Title = "Max shown avatars", Instruction = "Set the maximum number of shown avatars." },
            new GuidedStep { Title = "Hide-beyond distance", Instruction = "Set the hide-beyond distance for distant avatars." },
            new GuidedStep { Title = "Graphics profile", Instruction = "Choose a graphics profile in VRChat settings." },
            new GuidedStep { Title = "MSAA", Instruction = "Set MSAA in the VRChat graphics settings." },
            new GuidedStep { Title = "Mirrors", Instruction = "Adjust mirror settings in the VRChat configuration window." }
        ]
    };

    public static GuidedWorkflow SteamVr { get; } = new()
    {
        Id = "steamvr",
        Title = "SteamVR",
        Mode = "guided-only",
        WritesFiles = false,
        Note = "Record what the SteamVR overlay shows. No SteamVR settings keys are known to this build.",
        Steps =
        [
            new GuidedStep { Title = "Overlay", Instruction = "Record what the SteamVR overlay shows. SessionPilot does not read or write SteamVR settings." }
        ]
    };

    public static GuidedWorkflow VirtualDesktop { get; } = new()
    {
        Id = "virtual-desktop",
        Title = "Virtual Desktop",
        Mode = "guided-only",
        WritesFiles = false,
        Note = "Record what the Virtual Desktop overlay shows. There is no settings API in this build.",
        Steps =
        [
            new GuidedStep { Title = "Overlay", Instruction = "Record the quality, bitrate, refresh, and codec values the Virtual Desktop overlay shows." }
        ]
    };
}
