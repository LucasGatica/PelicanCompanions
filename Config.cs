using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace PelicanCompanions;

internal sealed class ModConfig
{
    public int ConfigVersion { get; set; } = 8;

    public KeybindList QuickActionWheelKey { get; set; } = KeybindList.Parse("X");
    public KeybindList ControllerQuickActionWheelKey { get; set; } = KeybindList.Parse("LeftStick");
    public KeybindList RecruitKey { get; set; } = KeybindList.Parse("F5");
    public KeybindList ManualTaskKey { get; set; } = KeybindList.Parse("F6");
    public KeybindList OpenSquadInventoryKey { get; set; } = KeybindList.Parse("F7");
    public KeybindList TasksToggleKey { get; set; } = KeybindList.Parse("F8");
    public KeybindList OpenCompanionPanelKey { get; set; } = KeybindList.Parse("F9");
    public KeybindList RecallAllCompanionsKey { get; set; } = KeybindList.Parse("");
    public bool ShowCompanionQuickHud { get; set; } = true;
    public CompanionQuickHudMode CompanionQuickHudMode { get; set; } = CompanionQuickHudMode.Detailed;
    public CompanionQuickHudSide CompanionQuickHudSide { get; set; } = CompanionQuickHudSide.Left;
    public int CompanionQuickHudMaxRows { get; set; } = 6;
    public CompanionFormationMode CompanionFormationMode { get; set; } = CompanionFormationMode.Adaptive;
    public bool ShowCompanionMovementDebug { get; set; } = false;

    public bool UseSquadInventory { get; set; } = true;
    public bool UseVanillaDialogueUi { get; set; } = true;
    public bool EnableCompanionProgression { get; set; } = true;
    public int CompanionInventorySlots { get; set; } = 10;
    public int CompanionWorkRadius { get; set; } = 8;
    public int CompanionWorkReturnDistance { get; set; } = 12;
    public bool ShowCompanionLevelUpHud { get; set; } = true;
    public int FriendshipRequirement { get; set; } = 2;
    public int FriendshipPointsPerHour { get; set; } = 2;
    public int MaxSquadSize { get; set; } = 3;
    public bool RecruitAllNpcs { get; set; } = false;
    // Retained in config.json so older configs remain loadable. These options
    // aren't exposed in GMCM until their gameplay modules are implemented.
    public DisableInteractionMode DisableInteraction { get; set; } = DisableInteractionMode.Never;
    public TrashReactionMode DisableTrashRummagingReaction { get; set; } = TrashReactionMode.Never;

    public bool EnableCommunication { get; set; } = true;
    public int DialogueCooldownSeconds { get; set; } = 45;
    public int CommunicationGroupCooldownSeconds { get; set; } = 3;
    public bool EnablePetExpressions { get; set; } = true;
    public bool EnableIdleAnimations { get; set; } = false;

    public bool EnableGathering { get; set; } = false;
    public TaskMode AttackingMode { get; set; } = TaskMode.Disabled;
    public TaskMode HarvestingMode { get; set; } = TaskMode.Mimicking;
    public int ProtectBeehouseFlowers { get; set; } = 5;
    public TaskMode ForagingMode { get; set; } = TaskMode.Disabled;
    public TaskMode LumberingMode { get; set; } = TaskMode.Mimicking;
    public TaskMode MiningMode { get; set; } = TaskMode.Mimicking;
    public TaskMode WateringMode { get; set; } = TaskMode.Mimicking;
    public FishingTaskMode FishingMode { get; set; } = FishingTaskMode.Disabled;
    public TaskMode PettingMode { get; set; } = TaskMode.Mimicking;
    public TaskMode ShearingMode { get; set; } = TaskMode.Disabled;
    public TaskMode MilkingMode { get; set; } = TaskMode.Disabled;

    public bool EnableRiding { get; set; } = false;
    public bool EnableSitting { get; set; } = false;

    public bool WarpHomeOnDisconnect { get; set; } = false;
    public int ParkTimeoutMinutes { get; set; } = 0;

    public void Validate()
    {
        this.QuickActionWheelKey ??= KeybindList.Parse("X");
        this.ControllerQuickActionWheelKey ??= KeybindList.Parse("LeftStick");
        this.RecruitKey ??= KeybindList.Parse("F5");
        this.ManualTaskKey ??= KeybindList.Parse("F6");
        this.OpenSquadInventoryKey ??= KeybindList.Parse("F7");
        this.TasksToggleKey ??= KeybindList.Parse("F8");
        this.OpenCompanionPanelKey ??= KeybindList.Parse("F9");
        this.RecallAllCompanionsKey ??= KeybindList.Parse("");

        this.FriendshipRequirement = Math.Clamp(this.FriendshipRequirement, 0, 14);
        this.FriendshipPointsPerHour = Math.Max(0, this.FriendshipPointsPerHour);
        this.MaxSquadSize = Math.Clamp(this.MaxSquadSize, 1, 12);
        this.CompanionInventorySlots = Math.Clamp(this.CompanionInventorySlots, 1, 10);
        this.CompanionWorkRadius = Math.Clamp(this.CompanionWorkRadius, 3, 20);
        this.CompanionWorkReturnDistance = Math.Clamp(this.CompanionWorkReturnDistance, this.CompanionWorkRadius, 40);
        this.CompanionQuickHudMaxRows = Math.Clamp(this.CompanionQuickHudMaxRows, 1, 12);

        if (!Enum.IsDefined(this.CompanionQuickHudMode))
            this.CompanionQuickHudMode = CompanionQuickHudMode.Detailed;
        if (!Enum.IsDefined(this.CompanionQuickHudSide))
            this.CompanionQuickHudSide = CompanionQuickHudSide.Left;
        if (!Enum.IsDefined(this.CompanionFormationMode))
            this.CompanionFormationMode = CompanionFormationMode.Adaptive;
        if (!Enum.IsDefined(this.DisableInteraction))
            this.DisableInteraction = DisableInteractionMode.Never;
        if (!Enum.IsDefined(this.DisableTrashRummagingReaction))
            this.DisableTrashRummagingReaction = TrashReactionMode.Never;
        if (!Enum.IsDefined(this.FishingMode))
            this.FishingMode = FishingTaskMode.Disabled;

        this.AttackingMode = NormalizeTaskMode(this.AttackingMode, TaskMode.Disabled);
        this.HarvestingMode = NormalizeTaskMode(this.HarvestingMode, TaskMode.Mimicking);
        this.ForagingMode = NormalizeTaskMode(this.ForagingMode, TaskMode.Disabled);
        this.LumberingMode = NormalizeTaskMode(this.LumberingMode, TaskMode.Mimicking);
        this.MiningMode = NormalizeTaskMode(this.MiningMode, TaskMode.Mimicking);
        this.WateringMode = NormalizeTaskMode(this.WateringMode, TaskMode.Mimicking);
        this.PettingMode = NormalizeTaskMode(this.PettingMode, TaskMode.Mimicking);
        this.ShearingMode = NormalizeTaskMode(this.ShearingMode, TaskMode.Disabled);
        this.MilkingMode = NormalizeTaskMode(this.MilkingMode, TaskMode.Disabled);

        this.DialogueCooldownSeconds = Math.Max(0, this.DialogueCooldownSeconds);
        this.CommunicationGroupCooldownSeconds = Math.Clamp(this.CommunicationGroupCooldownSeconds, 1, 30);
        this.ProtectBeehouseFlowers = Math.Max(0, this.ProtectBeehouseFlowers);
        this.ParkTimeoutMinutes = Math.Max(0, this.ParkTimeoutMinutes);
    }

    private static TaskMode NormalizeTaskMode(TaskMode value, TaskMode fallback)
    {
        return Enum.IsDefined(value) ? value : fallback;
    }
}

internal enum TaskMode
{
    Disabled,
    Mimicking,
    Autonomous
}

internal enum FishingTaskMode
{
    Disabled,
    Mimicking
}

internal enum CompanionFormationMode
{
    Behind,
    Compact,
    Adaptive
}

internal enum CompanionQuickHudMode
{
    Detailed,
    Compact
}

internal enum CompanionQuickHudSide
{
    Left,
    Right
}

internal enum DisableInteractionMode
{
    Never,
    CombatOnly,
    Always
}

internal enum TrashReactionMode
{
    Never,
    PetsOnly,
    Everyone
}
