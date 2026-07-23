namespace PelicanCompanions;

/// <summary>Pure scheduling rules for task destinations and path starts.</summary>
internal static class TaskNavigationPolicy
{
    public static bool IsRoutineWorkOrder(bool usesFixedWorkArea, string? workAreaOrderId)
    {
        return usesFixedWorkArea
            && !string.IsNullOrWhiteSpace(workAreaOrderId)
            && workAreaOrderId.StartsWith("routine-", StringComparison.Ordinal);
    }

    public static bool ShouldAdvanceRoutineOffscreen(
        bool usesFixedWorkArea,
        string? workAreaOrderId,
        bool hasFarmerInLocation)
    {
        return !hasFarmerInLocation
            && IsRoutineWorkOrder(usesFixedWorkArea, workAreaOrderId);
    }

    public static bool CanReuseStandTile(
        bool tileAvailable,
        bool adjacentToTarget,
        bool withinOwnerRange,
        bool reservedByAnotherCompanion,
        bool npcAlreadyAtStand,
        bool hasExpectedController,
        bool pathStartAttempted)
    {
        return tileAvailable
            && adjacentToTarget
            && withinOwnerRange
            && !reservedByAnotherCompanion
            && (npcAlreadyAtStand || hasExpectedController || !pathStartAttempted);
    }

    public static bool ShouldStartPath(
        bool targetIsCurrentTile,
        bool forceRestart,
        bool hasExpectedController,
        bool retryCooldownElapsed,
        bool hasPathStartBudget)
    {
        return !targetIsCurrentTile
            && hasPathStartBudget
            && (forceRestart || !hasExpectedController)
            && (forceRestart || retryCooldownElapsed);
    }
}
