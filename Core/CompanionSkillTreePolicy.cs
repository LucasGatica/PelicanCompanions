namespace PelicanCompanions;

/// <summary>The player-facing state of a skill-tree node.</summary>
internal enum CompanionSkillTreeState
{
    Learned,
    Available,
    LockedByPrerequisite,
    NeedsPoints,
    ProgressionDisabled
}

/// <summary>Keeps skill availability consistent between the panel and authoritative unlock flow.</summary>
internal static class CompanionSkillTreePolicy
{
    public static CompanionSkillTreeState GetState(
        CompanionSkillDefinition skill,
        IReadOnlyList<string>? unlockedSkillIds,
        int unspentSkillPoints,
        bool progressionEnabled)
    {
        if (IsUnlocked(unlockedSkillIds, skill.Id))
            return CompanionSkillTreeState.Learned;
        if (!progressionEnabled)
            return CompanionSkillTreeState.ProgressionDisabled;
        if (!string.IsNullOrWhiteSpace(skill.PrerequisiteId) && !IsUnlocked(unlockedSkillIds, skill.PrerequisiteId))
            return CompanionSkillTreeState.LockedByPrerequisite;
        if (unspentSkillPoints < skill.Cost)
            return CompanionSkillTreeState.NeedsPoints;
        return CompanionSkillTreeState.Available;
    }

    private static bool IsUnlocked(IReadOnlyList<string>? unlockedSkillIds, string skillId)
    {
        if (unlockedSkillIds is null)
            return false;
        for (int index = 0; index < unlockedSkillIds.Count; index++)
        {
            if (string.Equals(unlockedSkillIds[index], skillId, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
