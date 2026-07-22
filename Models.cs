using Microsoft.Xna.Framework;
using StardewValley;

namespace PelicanCompanions;

internal enum CompanionMode
{
    Following,
    Waiting,
    ParkedForDisconnect
}

internal enum CompanionMovementIntent
{
    Follow,
    Recall,
    Task
}

internal sealed class SquadMemberState
{
    public string NpcName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long OwnerId { get; set; }
    public CompanionMode Mode { get; set; } = CompanionMode.Following;
    public string? OriginalLocationName { get; set; }
    public float OriginalTileX { get; set; }
    public float OriginalTileY { get; set; }
    public bool HasOriginalPosition { get; set; }
    public int OriginalDayIndex { get; set; } = -1;
    public bool OriginalScheduleCaptured { get; set; }
    public string? OriginalScheduleKey { get; set; }
    public string? OriginalPetBehavior { get; set; }
    public bool OriginalSpousePatioActivity { get; set; }
    public bool OriginalMovementSpeedCaptured { get; set; }
    public int OriginalMovementSpeed { get; set; }
    public float OriginalAddedSpeed { get; set; }
    public string? WaitingLocationName { get; set; }
    public float WaitingTileX { get; set; }
    public float WaitingTileY { get; set; }
    public long ParkedAtUtcTicks { get; set; }
    public long LastDialogueUtcTicks { get; set; }
    public List<string> RecentDialogueKeys { get; set; } = new();
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int UnspentSkillPoints { get; set; }
    public bool BonusLevelTenPointGranted { get; set; }
    public List<string> UnlockedSkillIds { get; set; } = new();
    public List<SavedItemStack> Inventory { get; set; } = new();
    public bool SearchWood { get; set; }
    public bool SearchMining { get; set; }
    public bool ClearArea { get; set; }
    public bool CurrentWorkIsDirect { get; set; }
    public CompanionWorkSpecialty PreferredWorkSpecialty { get; set; } = CompanionWorkSpecialty.ClearArea;
    public bool WorkAreaActive { get; set; }
    public string WorkAreaOrderId { get; set; } = "";
    public string WorkAreaLocationName { get; set; } = "";
    public int WorkAreaCenterX { get; set; } = -1;
    public int WorkAreaCenterY { get; set; } = -1;
    public int WorkAreaRadius { get; set; } = 8;
    public CompanionWorkSpecialty WorkAreaSpecialty { get; set; } = CompanionWorkSpecialty.ClearArea;
    public string CurrentActivityKey { get; set; } = "companion.status.following";
    public string LastTaskResultKey { get; set; } = "";
    public string LastFailureReasonKey { get; set; } = "";
    public string CurrentTargetKey { get; set; } = "";
    public int CurrentTargetX { get; set; } = -1;
    public int CurrentTargetY { get; set; } = -1;
    public string PreviewTargetKey { get; set; } = "";
    public string PreviewReasonKey { get; set; } = "";
    public int PreviewTargetX { get; set; } = -1;
    public int PreviewTargetY { get; set; } = -1;
    public List<RecentCompanionLoot> RecentLoot { get; set; } = new();
}

internal sealed class SavedModState
{
    public int Version { get; set; } = 1;
    public long Revision { get; set; }
    public List<SquadMemberState> Members { get; set; } = new();
    public List<NpcCosmeticState> NpcCosmetics { get; set; } = new();
    public Dictionary<string, bool> TaskTogglesByPlayer { get; set; } = new();
    public List<SavedItemStack> SquadInventory { get; set; } = new();
    public List<SavedItemStack> LegacyOverflowItems { get; set; } = new();
    public List<DeferredNpcRestoreState> PendingNpcRestores { get; set; } = new();
    public CompanionHostRules? HostRules { get; set; }
}

/// <summary>Cosmetic equipment which belongs to an NPC independently of recruitment.</summary>
internal sealed class NpcCosmeticState
{
    public string NpcName { get; set; } = "";
    public SavedItemStack? EquippedHat { get; set; }
}

internal sealed class DeferredNpcRestoreState
{
    public string NpcName { get; set; } = "";
    public string? OriginalLocationName { get; set; }
    public float OriginalTileX { get; set; }
    public float OriginalTileY { get; set; }
    public bool HasOriginalPosition { get; set; }
    public int OriginalDayIndex { get; set; } = -1;
    public bool OriginalScheduleCaptured { get; set; }
    public string? OriginalScheduleKey { get; set; }
    public string? OriginalPetBehavior { get; set; }
    public bool OriginalSpousePatioActivity { get; set; }
    public bool OriginalMovementSpeedCaptured { get; set; }
    public int OriginalMovementSpeed { get; set; }
    public float OriginalAddedSpeed { get; set; }
}

internal sealed class CompanionHostRules
{
    public bool UseSquadInventory { get; set; }
    public bool EnableCompanionProgression { get; set; }
    public int CompanionInventorySlots { get; set; }
    public int CompanionWorkRadius { get; set; }
    public int CompanionWorkReturnDistance { get; set; }
    public int FriendshipRequirement { get; set; }
    public int MaxSquadSize { get; set; }
    public bool RecruitAllNpcs { get; set; }
    public bool EnableGathering { get; set; }
    public int ProtectBeehouseFlowers { get; set; }
    public TaskMode HarvestingMode { get; set; }
    public TaskMode ForagingMode { get; set; }
    public TaskMode LumberingMode { get; set; }
    public TaskMode MiningMode { get; set; }
    public TaskMode WateringMode { get; set; }
    public TaskMode PettingMode { get; set; }
}

internal sealed class SavedItemStack
{
    public string QualifiedItemId { get; set; } = "";
    public int Stack { get; set; } = 1;
    public int Quality { get; set; }
    public Dictionary<string, string> ModData { get; set; } = new(StringComparer.Ordinal);
    public string? PreservedParentItemId { get; set; }
    public bool HasColor { get; set; }
    public byte ColorR { get; set; }
    public byte ColorG { get; set; }
    public byte ColorB { get; set; }
    public byte ColorA { get; set; } = byte.MaxValue;
}

internal sealed class RecentCompanionLoot
{
    public string QualifiedItemId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Stack { get; set; } = 1;
    public string SourceKey { get; set; } = "";
    public long AddedAtUtcTicks { get; set; }
}

internal readonly record struct CompanionPanelMapInfo(
    string StatusKey,
    bool SameLocation,
    int OwnerX,
    int OwnerY,
    int NpcX,
    int NpcY);

internal sealed class NpcCompanionProfile
{
    public Dictionary<string, List<CompanionDialogueLine>> Dialogue { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> IdleAnimations { get; set; } = new();
}

internal sealed class CompanionDialogueLine
{
    public string Id { get; set; } = "";
    public string TextKey { get; set; } = "";
    public string? Condition { get; set; }
    public bool Overlay { get; set; }
    public int Weight { get; set; } = 1;
    public int MinIntervalSeconds { get; set; }
}

internal enum CompanionDialoguePriority
{
    Ambient,
    Task,
    Command,
    Milestone
}

internal sealed class CompanionDialogueContext
{
    public long? OwnerId { get; init; }
    public CompanionTaskKind? TaskKind { get; init; }
    public string ItemName { get; init; } = "";
    public string ItemId { get; init; } = "";
    public string ResultKey { get; init; } = "";
    public string FailureKey { get; init; } = "";
    public int? Level { get; init; }
    public int Hearts { get; init; }
    public int TimeOfDay { get; init; }
    public string Season { get; init; } = "";
    public string Weather { get; init; } = "";
    public string DayPeriod { get; init; } = "";
    public string LocationName { get; init; } = "";
    public string LocationContext { get; init; } = "";
    public bool IsSpouse { get; init; }
    public bool IsOutdoors { get; init; }
    public bool IsManual { get; init; }
}

internal sealed class SquadActionMessage
{
    public string CommandId { get; set; } = "";
    public string Action { get; set; } = "";
    public string NpcName { get; set; } = "";
    public string Argument { get; set; } = "";
    public string LocationName { get; set; } = "";
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int Index { get; set; } = -1;
    public string ExpectedItemToken { get; set; } = "";
    public string ExpectedStateToken { get; set; } = "";
    public bool? DesiredEnabled { get; set; }
}

internal sealed class CompanionCommandFeedbackMessage
{
    public string Text { get; set; } = "";
    public bool IsError { get; set; }
    public string Action { get; set; } = "";
    public string CommandId { get; set; } = "";
}

internal sealed class CompanionExpressionMessage
{
    public string NpcName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextKey { get; set; } = "";
    public CompanionDialogueContext? Context { get; set; }
    public int EmoteId { get; set; } = -1;
    public string SoundCue { get; set; } = "";
    public float JumpHeight { get; set; }
    public int ShakeMilliseconds { get; set; }
}

internal sealed class CompanionWorkVisualMessage
{
    public string NpcName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public CompanionTaskKind TaskKind { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public string Outcome { get; set; } = "work";
}

internal enum CompanionTaskKind
{
    MovingToWait,
    Lumbering,
    Mining,
    Watering,
    Gathering,
    Harvesting,
    Petting,
    Fishing
}

internal sealed class PendingCompanionTask
{
    public CompanionTaskKind Kind { get; set; }
    public string NpcName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public Vector2 TargetTile { get; set; }
    public long TargetEntityId { get; set; }
    public bool Manual { get; set; }
    public bool UsesWorkDirective { get; set; }
    public bool UsesConfiguredAutonomy { get; set; }
    public bool UsesFixedWorkArea { get; set; }
    public string FixedWorkAreaOrderId { get; set; } = "";
    public Vector2 FixedWorkAreaCenter { get; set; }
    public bool RequiresPlayerTool { get; set; }
    public bool IgnoresTaskMode { get; set; }
    public bool IgnoresTaskToggle { get; set; }
    public string ExpectedTargetToken { get; set; } = "";
    public object? ExpectedTargetInstance { get; set; }
    public string SharedTargetGroupId { get; set; } = "";
    public int WorkRadius { get; set; }
    public int ReturnDistance { get; set; }
    public int LastPathTick { get; set; }
    public int LastActionTick { get; set; }
    public bool AwaitingWorkAnimation { get; set; }
    public int WorkAnimationReadyTick { get; set; }
    public Vector2 WorkAnimationTargetTile { get; set; }
    public int StartedTick { get; set; }
    public int LastProcessedTick { get; set; }
    public int InactiveTicks { get; set; }
    public Vector2 StandTile { get; set; }
    public Vector2 LastProgressPosition { get; set; }
    public bool HasLastProgressPosition { get; set; }
    public bool HasPathStartAttempted { get; set; }
    public HashSet<Vector2> RejectedStandTiles { get; } = new();
    public int NoProgressTicks { get; set; }
    public Vector2 FishingWaterAnchorTile { get; set; }
    public Vector2 FishingCastTile { get; set; }
    public string FishingWaterBodyToken { get; set; } = "";
    public int FishingWaterDepth { get; set; }
    public int NextFishingTime { get; set; }
}

internal sealed class SharedWorkTargetReservation
{
    public string GroupId { get; init; } = "";
    public HashSet<string> NpcNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal readonly record struct EligibilityResult(bool Allowed, string ReasonKey)
{
    public static EligibilityResult Success { get; } = new(true, "");
}

internal enum CompanionDirective
{
    SearchWood,
    SearchMining,
    ClearArea
}

internal enum CompanionWorkSpecialty
{
    ClearArea,
    Wood,
    Mining
}

internal sealed record CompanionSkillDefinition(
    string Id,
    string Branch,
    string NameKey,
    string DescriptionKey,
    int Cost,
    string? PrerequisiteId);

internal static class CompanionProgression
{
    public const int MaxLevel = 10;

    // These skills shipped in older saves even though combat was never
    // implemented. They are kept only as migration metadata so players get
    // their spent points back instead of carrying permanently inert unlocks.
    private static readonly IReadOnlyDictionary<string, int> LegacySkillRefunds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SKILL-COMBAT-001"] = 1,
            ["SKILL-COMBAT-002"] = 1
        };

    public static readonly int[] LevelXpThresholds =
    {
        0,
        50,
        125,
        250,
        450,
        700,
        1000,
        1400,
        1900,
        2500
    };

    public static readonly CompanionSkillDefinition[] Skills =
    {
        new("SKILL-LUMBER-001", "Lumbering", "skills.lumber_1.name", "skills.lumber_1.description", 1, null),
        new("SKILL-LUMBER-002", "Lumbering", "skills.lumber_2.name", "skills.lumber_2.description", 1, "SKILL-LUMBER-001"),
        new("SKILL-LUMBER-003", "Lumbering", "skills.lumber_3.name", "skills.lumber_3.description", 2, "SKILL-LUMBER-002"),
        new("SKILL-MINING-001", "Mining", "skills.mining_1.name", "skills.mining_1.description", 1, null),
        new("SKILL-MINING-002", "Mining", "skills.mining_2.name", "skills.mining_2.description", 1, "SKILL-MINING-001"),
        new("SKILL-MINING-003", "Mining", "skills.mining_3.name", "skills.mining_3.description", 2, "SKILL-MINING-002"),
        new("SKILL-UTILITY-001", "Utility", "skills.utility_1.name", "skills.utility_1.description", 1, null),
        new("SKILL-UTILITY-002", "Utility", "skills.utility_2.name", "skills.utility_2.description", 1, "SKILL-UTILITY-001"),
        new("SKILL-UTILITY-003", "Utility", "skills.utility_3.name", "skills.utility_3.description", 2, "SKILL-UTILITY-002"),
        new("SKILL-FISHING-001", "Fishing", "skills.fishing_1.name", "skills.fishing_1.description", 1, null),
        new("SKILL-FISHING-002", "Fishing", "skills.fishing_2.name", "skills.fishing_2.description", 1, "SKILL-FISHING-001"),
        new("SKILL-FISHING-003", "Fishing", "skills.fishing_3.name", "skills.fishing_3.description", 2, "SKILL-FISHING-002")
    };

    public static int GetLevelForXp(int xp)
    {
        int level = 1;
        for (int i = 0; i < LevelXpThresholds.Length; i++)
        {
            if (xp >= LevelXpThresholds[i])
                level = i + 1;
        }

        return Math.Clamp(level, 1, MaxLevel);
    }

    public static int GetXpForLevel(int level)
    {
        int index = Math.Clamp(level, 1, MaxLevel) - 1;
        return LevelXpThresholds[index];
    }

    public static int GetNextLevelXp(int level)
    {
        if (level >= MaxLevel)
            return GetXpForLevel(MaxLevel);

        return GetXpForLevel(level + 1);
    }

    public static int GetLegacySkillPointRefund(IEnumerable<string>? unlockedSkillIds)
    {
        if (unlockedSkillIds is null)
            return 0;

        return unlockedSkillIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(id => LegacySkillRefunds.TryGetValue(id, out int cost) ? cost : 0);
    }
}
