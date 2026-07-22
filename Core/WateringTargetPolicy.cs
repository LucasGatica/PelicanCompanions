namespace PelicanCompanions;

/// <summary>Pure watering eligibility rules shared by planning and execution.</summary>
internal static class WateringTargetPolicy
{
    public static bool IsValid(bool cropNeedsWatering, bool dirtIsWatered)
    {
        // HoeDirt.needsWatering() reports the crop's irrigation requirement;
        // it remains true after the separate HoeDirt state becomes watered.
        return cropNeedsWatering && !dirtIsWatered;
    }
}
