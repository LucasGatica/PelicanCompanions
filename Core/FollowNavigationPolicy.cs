namespace PelicanCompanions;

/// <summary>Pure scheduling policy for the companion follow hot path.</summary>
internal static class FollowNavigationPolicy
{
    public static bool ShouldResetForRecall(
        bool sameLocation,
        float ownerDistance,
        float ordinaryFollowRadius,
        bool wasStuckOrReturning)
    {
        return !sameLocation
            || ownerDistance > Math.Max(0f, ordinaryFollowRadius)
            || wasStuckOrReturning;
    }

    public static bool ShouldProbeConnectivity(
        bool shouldMove,
        int stalledUpdates,
        int stallThreshold,
        int currentTick,
        int? lastProbeTick,
        int cooldownTicks)
    {
        if (!shouldMove || stalledUpdates < Math.Max(1, stallThreshold))
            return false;

        return !lastProbeTick.HasValue
            || unchecked((uint)(currentTick - lastProbeTick.Value)) >= (uint)Math.Max(1, cooldownTicks);
    }

    public static bool ShouldStartPath(
        bool shouldMove,
        bool targetIsCurrentTile,
        bool probedConnectivityThisUpdate,
        bool hasExpectedController,
        bool pathCooldownElapsed,
        bool hasPathStartBudget)
    {
        return shouldMove
            && !targetIsCurrentTile
            && !probedConnectivityThisUpdate
            && !hasExpectedController
            && pathCooldownElapsed
            && hasPathStartBudget;
    }
}
