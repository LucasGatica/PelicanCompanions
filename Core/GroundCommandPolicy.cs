namespace PelicanCompanions;

/// <summary>Pure context rules for empty-ground squad commands.</summary>
internal static class GroundCommandPolicy
{
    public static bool CanOpen(bool hasOwnedCompanion, bool sameLocation, bool tileAvailable)
    {
        return hasOwnedCompanion && sameLocation && tileAvailable;
    }

    public static bool CanListMember(bool ownedByPlayer, bool parkedForDisconnect, bool sameLocation)
    {
        return ownedByPlayer && !parkedForDisconnect && sameLocation;
    }
}
