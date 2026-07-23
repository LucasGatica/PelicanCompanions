namespace PelicanCompanions;

/// <summary>Fail-closed rules for deciding whether an automatic refill may start.</summary>
internal static class CompanionSmartRefillPolicy
{
    public static bool AllowsLocation(
        SmartWaterRefillMode mode,
        bool isFarm,
        bool isOutdoors)
    {
        return mode switch
        {
            SmartWaterRefillMode.FarmOnly => isFarm,
            SmartWaterRefillMode.AnySafeWater => isOutdoors,
            _ => false
        };
    }

    public static bool ShouldRefill(
        SmartWaterRefillMode mode,
        int waterLeft,
        bool isFarm,
        bool isOutdoors,
        bool hasReachableWater)
    {
        return waterLeft <= 0
            && hasReachableWater
            && AllowsLocation(mode, isFarm, isOutdoors);
    }
}
