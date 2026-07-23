namespace PelicanCompanions;

/// <summary>Pure decisions shared by filtered manual and automatic deposits.</summary>
internal static class CompanionInventoryFilterPolicy
{
    public static bool ShouldDeposit(
        CompanionInventoryRulesState? rules,
        bool isWood,
        bool isMineral,
        bool isFood)
    {
        rules ??= new CompanionInventoryRulesState();
        if (isFood && rules.KeepFood)
            return false;
        if (isWood && !rules.DepositWood)
            return false;
        if (isMineral && !rules.DepositMinerals)
            return false;
        return true;
    }

    public static bool Get(CompanionInventoryRulesState? rules, CompanionInventoryFilter filter)
    {
        rules ??= new CompanionInventoryRulesState();
        return filter switch
        {
            CompanionInventoryFilter.DepositWood => rules.DepositWood,
            CompanionInventoryFilter.DepositMinerals => rules.DepositMinerals,
            CompanionInventoryFilter.KeepFood => rules.KeepFood,
            _ => false
        };
    }

    public static bool Set(
        CompanionInventoryRulesState rules,
        CompanionInventoryFilter filter,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(rules);
        switch (filter)
        {
            case CompanionInventoryFilter.DepositWood:
                rules.DepositWood = enabled;
                return true;
            case CompanionInventoryFilter.DepositMinerals:
                rules.DepositMinerals = enabled;
                return true;
            case CompanionInventoryFilter.KeepFood:
                rules.KeepFood = enabled;
                return true;
            default:
                return false;
        }
    }
}
