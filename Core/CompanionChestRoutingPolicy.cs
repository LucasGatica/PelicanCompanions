namespace PelicanCompanions;

/// <summary>Pure selection rules for individual and owner-wide chest destinations.</summary>
internal static class CompanionChestRoutingPolicy
{
    public static CompanionChestDestinationState? Select(
        CompanionChestDestinationState? individual,
        CompanionChestDestinationState? ownerDefault)
    {
        return IsValid(individual) ? individual : IsValid(ownerDefault) ? ownerDefault : null;
    }

    public static bool IsValid(CompanionChestDestinationState? destination)
    {
        return destination is not null
            && !string.IsNullOrWhiteSpace(destination.LocationName)
            && destination.TileX >= 0
            && destination.TileY >= 0
            && Guid.TryParse(destination.ChestId, out _);
    }

    public static bool RefersTo(
        CompanionChestDestinationState? destination,
        string locationName,
        int tileX,
        int tileY)
    {
        return IsValid(destination)
            && string.Equals(destination!.LocationName, locationName, StringComparison.Ordinal)
            && destination.TileX == tileX
            && destination.TileY == tileY;
    }

    public static bool RefersToChestId(
        CompanionChestDestinationState? destination,
        string? chestId)
    {
        return IsValid(destination)
            && TryNormalizeChestId(chestId, out string normalizedChestId)
            && TryNormalizeChestId(destination!.ChestId, out string normalizedDestinationId)
            && string.Equals(normalizedDestinationId, normalizedChestId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Match a client expectation to the host identity. Missing or malformed
    /// expectations deliberately fail closed instead of acting as wildcards.
    /// </summary>
    public static bool MatchesExpectedIdentity(string? expectedChestId, string? currentChestId)
    {
        return TryNormalizeChestId(expectedChestId, out string normalizedExpected)
            && TryNormalizeChestId(currentChestId, out string normalizedCurrent)
            && string.Equals(normalizedExpected, normalizedCurrent, StringComparison.Ordinal);
    }

    /// <summary>Select the only matching candidate, failing closed for missing or duplicated identities.</summary>
    public static T? SelectUnique<T>(IEnumerable<T> candidates, Func<T, string?> getChestId)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(getChestId);

        T? selected = null;
        string? selectedId = null;
        foreach (T candidate in candidates)
        {
            if (!TryNormalizeChestId(getChestId(candidate), out string candidateId))
                continue;

            if (selected is not null)
            {
                // The resolver supplies candidates for one requested GUID. Still
                // reject a mixed set here so this policy remains safe in isolation.
                if (!string.Equals(selectedId, candidateId, StringComparison.Ordinal)
                    || !ReferenceEquals(selected, candidate))
                {
                    return null;
                }

                continue;
            }

            selected = candidate;
            selectedId = candidateId;
        }

        return selected;
    }

    public static bool TryNormalizeChestId(string? chestId, out string normalized)
    {
        if (Guid.TryParse(chestId, out Guid parsed))
        {
            normalized = parsed.ToString("N");
            return true;
        }

        normalized = "";
        return false;
    }
}
