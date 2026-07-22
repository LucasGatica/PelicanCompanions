namespace PelicanCompanions;

/// <summary>Pure identity and vanilla tool-state bounds for companion loadouts.</summary>
internal static class CompanionEquipmentPolicy
{
    public const int MinimumUpgradeLevel = 0;
    public const int MaximumUpgradeLevel = 4;
    public const int BaseWateringCanCapacity = 40;
    public const int WateringCanCapacityPerUpgrade = 15;

    public static CompanionOperationalProfileKey CreateKey(long ownerId, string npcName)
    {
        return new CompanionOperationalProfileKey(
            ownerId,
            (npcName ?? "").Trim().ToUpperInvariant());
    }

    public static bool IsValidUpgradeLevel(int upgradeLevel)
    {
        return upgradeLevel is >= MinimumUpgradeLevel and <= MaximumUpgradeLevel;
    }

    public static int GetWateringCanCapacity(int upgradeLevel)
    {
        int normalized = Math.Clamp(upgradeLevel, MinimumUpgradeLevel, MaximumUpgradeLevel);
        return BaseWateringCanCapacity + normalized * WateringCanCapacityPerUpgrade;
    }

    public static bool IsValidWateringCanState(int upgradeLevel, int waterLeft)
    {
        return IsValidUpgradeLevel(upgradeLevel)
            && waterLeft >= 0
            && waterLeft <= GetWateringCanCapacity(upgradeLevel);
    }

    public static bool CanWorkSpecialty(
        CompanionWorkSpecialty specialty,
        bool lumberingEnabled,
        bool miningEnabled,
        bool wateringEnabled,
        bool hasUsableAxe,
        bool hasUsablePickaxe,
        bool hasUsableWateringCan)
    {
        bool canLumber = lumberingEnabled && hasUsableAxe;
        bool canMine = miningEnabled && hasUsablePickaxe;
        bool canWater = wateringEnabled && hasUsableWateringCan;
        return specialty switch
        {
            CompanionWorkSpecialty.Wood => canLumber,
            CompanionWorkSpecialty.Mining => canMine,
            CompanionWorkSpecialty.Watering => canWater,
            CompanionWorkSpecialty.ClearArea => canLumber || canMine,
            _ => false
        };
    }
}
