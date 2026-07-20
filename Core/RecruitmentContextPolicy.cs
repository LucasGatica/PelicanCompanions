namespace PelicanCompanions;

internal static class RecruitmentContextPolicy
{
    public static bool IsLocationValid(bool ownerHasCurrentLocation, bool npcSharesCurrentLocation)
    {
        return ownerHasCurrentLocation && npcSharesCurrentLocation;
    }
}
