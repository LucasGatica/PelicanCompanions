namespace PelicanCompanions;

/// <summary>Pure weighted, anti-repeat selection for already eligible dialogue lines.</summary>
internal static class CompanionDialogueSelectionPolicy
{
    public static string GetIdentity(CompanionDialogueLine line)
    {
        return string.IsNullOrWhiteSpace(line.Id) ? line.TextKey : line.Id;
    }

    public static CompanionDialogueLine? Select(
        IReadOnlyList<CompanionDialogueLine> candidates,
        IReadOnlyCollection<string>? recentKeys,
        int weightedRoll)
    {
        if (candidates is null || candidates.Count == 0)
            return null;

        List<string> recent = (recentKeys ?? Array.Empty<string>()).ToList();
        CompanionDialogueLine[] fresh = candidates
            .Where(line => line is not null
                && !string.IsNullOrWhiteSpace(line.TextKey)
                && !recent.Contains(GetIdentity(line), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        CompanionDialogueLine[] pool;
        if (fresh.Length > 0)
        {
            pool = fresh;
        }
        else
        {
            CompanionDialogueLine[] valid = candidates
                .Where(line => line is not null && !string.IsNullOrWhiteSpace(line.TextKey))
                .ToArray();
            int oldestUse = valid
                .Select(line => FindRecentIndex(recent, GetIdentity(line)))
                .DefaultIfEmpty(-1)
                .Max();
            pool = valid
                .Where(line => FindRecentIndex(recent, GetIdentity(line)) == oldestUse)
                .ToArray();
        }
        if (pool.Length == 0)
            return null;

        int totalWeight = pool.Sum(line => Math.Clamp(line.Weight, 1, 1000));
        int roll = Math.Abs(weightedRoll == int.MinValue ? 0 : weightedRoll) % totalWeight;
        foreach (CompanionDialogueLine line in pool)
        {
            roll -= Math.Clamp(line.Weight, 1, 1000);
            if (roll < 0)
                return line;
        }

        return pool[^1];
    }

    private static int FindRecentIndex(IReadOnlyList<string> recent, string identity)
    {
        for (int i = 0; i < recent.Count; i++)
        {
            if (string.Equals(recent[i], identity, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
