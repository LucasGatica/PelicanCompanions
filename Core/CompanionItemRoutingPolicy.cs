namespace PelicanCompanions;

internal enum CompanionItemDestination
{
    Companion,
    Squad,
    Owner,
    WorldDrop
}

/// <summary>Pure destination ordering for items produced or collected by a companion.</summary>
internal static class CompanionItemRoutingPolicy
{
    public static IReadOnlyList<CompanionItemDestination> GetRoute(
        bool useSquadInventory,
        bool ownerAvailable)
    {
        if (useSquadInventory)
        {
            return new[]
            {
                CompanionItemDestination.Companion,
                CompanionItemDestination.Squad,
                CompanionItemDestination.WorldDrop
            };
        }

        if (ownerAvailable)
        {
            return new[]
            {
                CompanionItemDestination.Companion,
                CompanionItemDestination.Owner,
                CompanionItemDestination.WorldDrop
            };
        }

        return new[]
        {
            CompanionItemDestination.Companion,
            CompanionItemDestination.WorldDrop
        };
    }
}
