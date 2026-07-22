namespace PelicanCompanions;

/// <summary>Pure geometry rules for direct contextual resource commands.</summary>
internal static class ContextCommandPolicy
{
    private static readonly (int X, int Y)[] CardinalStandOffsets =
    {
        (0, 1),
        (1, 0),
        (-1, 0),
        (0, -1)
    };

    /// <summary>
    /// Get whether at least one cardinal tile beside a target can be within the
    /// owner's allowed stand radius. Passability and reachability are validated
    /// separately by the runtime planner.
    /// </summary>
    public static bool HasAdjacentStandWithinRadius(
        int ownerX,
        int ownerY,
        int targetX,
        int targetY,
        int maximumStandDistance)
    {
        if (maximumStandDistance < 0)
            return false;

        long maximumDistanceSquared = (long)maximumStandDistance * maximumStandDistance;
        foreach ((int offsetX, int offsetY) in CardinalStandOffsets)
        {
            long deltaX = (long)targetX + offsetX - ownerX;
            long deltaY = (long)targetY + offsetY - ownerY;
            if (deltaX * deltaX + deltaY * deltaY <= maximumDistanceSquared)
                return true;
        }

        return false;
    }
}
