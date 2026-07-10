using Microsoft.Xna.Framework;
using StardewValley;

namespace PelicanCompanions;

internal enum CompanionMode
{
    Following,
    Waiting,
    ParkedForDisconnect
}

internal sealed class SquadMemberState
{
    public string NpcName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long OwnerId { get; set; }
    public CompanionMode Mode { get; set; } = CompanionMode.Following;
    public string? OriginalLocationName { get; set; }
    public string? WaitingLocationName { get; set; }
    public float WaitingTileX { get; set; }
    public float WaitingTileY { get; set; }
    public long ParkedAtUtcTicks { get; set; }
    public long LastDialogueUtcTicks { get; set; }
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int UnspentSkillPoints { get; set; }
    public bool BonusLevelTenPointGranted { get; set; }
    public List<string> UnlockedSkillIds { get; set; } = new();
    public List<SavedItemStack> Inventory { get; set; } = new();
    public bool SearchWood { get; set; }
    public bool SearchMining { get; set; }
    public bool ClearArea { get; set; }
    public CompanionWorkSpecialty PreferredWorkSpecialty { get; set; } = CompanionWorkSpecialty.ClearArea;
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
    public List<SquadMemberState> Members { get; set; } = new();
    public Dictionary<string, bool> TaskTogglesByPlayer { get; set; } = new();
    public List<SavedItemStack> SquadInventory { get; set; } = new();
    public List<SavedItemStack> LegacyOverflowItems { get; set; } = new();
}

internal sealed class SavedItemStack
{
    public string QualifiedItemId { get; set; } = "";
    public int Stack { get; set; } = 1;
    public int Quality { get; set; }
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
    public string TextKey { get; set; } = "";
    public string? Condition { get; set; }
}

internal sealed class SquadActionMessage
{
    public string Action { get; set; } = "";
    public string NpcName { get; set; } = "";
}

internal enum CompanionTaskKind
{
    Lumbering,
    Mining
}

internal sealed class PendingCompanionTask
{
    public CompanionTaskKind Kind { get; set; }
    public string NpcName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public Vector2 TargetTile { get; set; }
    public bool Manual { get; set; }
    public bool UsesWorkDirective { get; set; }
    public bool UsesConfiguredAutonomy { get; set; }
    public bool RequiresPlayerTool { get; set; }
    public int WorkRadius { get; set; }
    public int ReturnDistance { get; set; }
    public int LastPathTick { get; set; }
    public int LastActionTick { get; set; }
    public int StartedTick { get; set; }
    public float LastDistanceToStandTile { get; set; } = -1f;
    public int NoProgressTicks { get; set; }
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
        new("SKILL-COMBAT-001", "Combat", "skills.combat_1.name", "skills.combat_1.description", 1, null),
        new("SKILL-COMBAT-002", "Combat", "skills.combat_2.name", "skills.combat_2.description", 1, "SKILL-COMBAT-001"),
        new("SKILL-LUMBER-001", "Lumbering", "skills.lumber_1.name", "skills.lumber_1.description", 1, null),
        new("SKILL-LUMBER-002", "Lumbering", "skills.lumber_2.name", "skills.lumber_2.description", 1, "SKILL-LUMBER-001"),
        new("SKILL-LUMBER-003", "Lumbering", "skills.lumber_3.name", "skills.lumber_3.description", 2, "SKILL-LUMBER-002"),
        new("SKILL-MINING-001", "Mining", "skills.mining_1.name", "skills.mining_1.description", 1, null),
        new("SKILL-MINING-002", "Mining", "skills.mining_2.name", "skills.mining_2.description", 1, "SKILL-MINING-001"),
        new("SKILL-MINING-003", "Mining", "skills.mining_3.name", "skills.mining_3.description", 2, "SKILL-MINING-002"),
        new("SKILL-UTILITY-001", "Utility", "skills.utility_1.name", "skills.utility_1.description", 1, null),
        new("SKILL-UTILITY-002", "Utility", "skills.utility_2.name", "skills.utility_2.description", 1, "SKILL-UTILITY-001"),
        new("SKILL-UTILITY-003", "Utility", "skills.utility_3.name", "skills.utility_3.description", 2, "SKILL-UTILITY-002")
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
}
