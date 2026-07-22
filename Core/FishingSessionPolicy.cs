namespace PelicanCompanions;

/// <summary>
/// Pure timing, reward, cast and skill rules for a companion fishing session.
/// Stardew time values use HHMM notation and may advance beyond 2400.
/// </summary>
internal static class FishingSessionPolicy
{
    public const int DayEndTime = 2600;
    public const int XpPerCatch = 8;
    public const int BaseCatchIntervalMinutes = 60;
    public const int SkilledCatchIntervalMinutes = 50;
    public const int BaseCastDistance = 3;
    public const int MaximumRodUpgradeLevel = 4;
    public const double ExtraCatchChance = 0.25d;

    /// <summary>Fishing ends at 26:00 and remains ended afterward.</summary>
    public static bool HasDayEnded(int timeOfDay)
    {
        return timeOfDay >= DayEndTime;
    }

    /// <summary>Get the in-game minute interval between catch attempts.</summary>
    public static int GetCatchIntervalMinutes(bool hasFishingSkillOne)
    {
        return hasFishingSkillOne ? SkilledCatchIntervalMinutes : BaseCatchIntervalMinutes;
    }

    /// <summary>Add non-negative minutes to a valid non-negative HHMM value.</summary>
    public static int AddMinutes(int timeOfDay, int minutesToAdd)
    {
        ValidateTime(timeOfDay, nameof(timeOfDay));
        if (minutesToAdd < 0)
            throw new ArgumentOutOfRangeException(nameof(minutesToAdd));

        long totalMinutes = (long)(timeOfDay / 100) * 60 + timeOfDay % 100 + minutesToAdd;
        long result = totalMinutes / 60 * 100 + totalMinutes % 60;
        if (result > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(minutesToAdd));

        return (int)result;
    }

    /// <summary>
    /// A catch is ready at the scheduled minute boundary, but never once the
    /// day-ending cutoff has been reached.
    /// </summary>
    public static bool IsCatchReady(int currentTime, int scheduledTime)
    {
        ValidateTime(currentTime, nameof(currentTime));
        ValidateTime(scheduledTime, nameof(scheduledTime));
        return !HasDayEnded(currentTime) && ToMinutes(currentTime) >= ToMinutes(scheduledTime);
    }

    /// <summary>
    /// Cast range is three tiles plus a clamped 0-4 rod upgrade. Fishing skill
    /// two adds one more tile after that clamp.
    /// </summary>
    public static int GetMaximumCastDistance(int rodUpgradeLevel, bool hasFishingSkillTwo)
    {
        return BaseCastDistance
            + Math.Clamp(rodUpgradeLevel, 0, MaximumRodUpgradeLevel)
            + (hasFishingSkillTwo ? 1 : 0);
    }

    /// <summary>
    /// Convert approximate water depth to Stardew item quality: depth 1 is
    /// normal, 2-3 is silver and 4+ is gold. Fishing skill two upgrades the
    /// result by one tier, with gold becoming iridium.
    /// </summary>
    public static int GetCatchQuality(int approximateDepth, bool hasFishingSkillTwo)
    {
        int quality = approximateDepth >= 4
            ? 2
            : approximateDepth >= 2
                ? 1
                : 0;
        if (!hasFishingSkillTwo)
            return quality;

        return quality switch
        {
            0 => 1,
            1 => 2,
            _ => 4
        };
    }

    /// <summary>
    /// Fishing skill three grants an extra catch only for a finite RNG roll in
    /// the conventional [0, 1) range which is strictly below 0.25.
    /// </summary>
    public static bool RollsExtraCatch(bool hasFishingSkillThree, double roll)
    {
        return hasFishingSkillThree
            && double.IsFinite(roll)
            && roll >= 0d
            && roll < ExtraCatchChance;
    }

    private static int ToMinutes(int timeOfDay)
    {
        return timeOfDay / 100 * 60 + timeOfDay % 100;
    }

    private static void ValidateTime(int timeOfDay, string parameterName)
    {
        if (timeOfDay < 0 || timeOfDay % 100 >= 60)
            throw new ArgumentOutOfRangeException(parameterName, "Time must be a non-negative HHMM value.");
    }
}
