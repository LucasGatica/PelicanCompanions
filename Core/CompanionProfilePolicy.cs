namespace PelicanCompanions;

/// <summary>Pure creation, migration, and attachment rules for permanent NPC profiles.</summary>
internal static class CompanionProfilePolicy
{
    public static CompanionProfileState Create(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
            throw new ArgumentException("An NPC name is required.", nameof(npcName));

        return new CompanionProfileState { NpcName = npcName };
    }

    /// <summary>Extract progression from a schema &lt;= 10 active member.</summary>
    public static CompanionProfileState MigrateLegacyMember(SquadMemberState member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (string.IsNullOrWhiteSpace(member.NpcName))
            throw new ArgumentException("The legacy member must identify an NPC.", nameof(member));

        CompanionProfileState migrated = CompanionStateCopy.CloneProfile(member.Profile);
        migrated.NpcName = member.NpcName;
        return migrated;
    }

    public static void Attach(SquadMemberState member, CompanionProfileState profile)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(member.NpcName))
            throw new ArgumentException("The active member must identify an NPC.", nameof(member));

        if (string.IsNullOrWhiteSpace(profile.NpcName))
            profile.NpcName = member.NpcName;
        else if (!string.Equals(member.NpcName, profile.NpcName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Profile '{profile.NpcName}' can't be attached to member '{member.NpcName}'.");

        member.Profile = profile;
    }
}
