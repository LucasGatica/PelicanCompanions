namespace PelicanCompanions;

/// <summary>Pure fair-selection policy for bounded autonomous task planning.</summary>
internal static class TaskPlanningPolicy
{
    public static IReadOnlyList<string> SelectMembers(
        IEnumerable<string> eligibleNames,
        IReadOnlySet<string>? priorityNames,
        int cursor,
        int budget,
        out int nextCursor)
    {
        List<string> eligible = eligibleNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int limit = Math.Max(0, budget);
        if (eligible.Count == 0 || limit == 0)
        {
            nextCursor = 0;
            return Array.Empty<string>();
        }

        List<string> priority = eligible
            .Where(name => priorityNames?.Contains(name) == true)
            .ToList();
        List<string> regular = eligible
            .Where(name => priorityNames?.Contains(name) != true)
            .ToList();
        int priorityLimit = regular.Count > 0 && limit > 1
            ? limit - 1
            : limit;
        List<string> selected = priority.Take(priorityLimit).ToList();
        if (selected.Count >= limit)
        {
            nextCursor = NormalizeCursor(cursor, eligible.Count);
            return selected;
        }

        if (regular.Count == 0)
        {
            nextCursor = 0;
            return selected;
        }

        int start = NormalizeCursor(cursor, regular.Count);
        int regularCount = Math.Min(limit - selected.Count, regular.Count);
        for (int offset = 0; offset < regularCount; offset++)
            selected.Add(regular[(start + offset) % regular.Count]);

        nextCursor = (start + regularCount) % regular.Count;
        return selected;
    }

    private static int NormalizeCursor(int cursor, int count)
    {
        if (count <= 0)
            return 0;

        int normalized = cursor % count;
        return normalized < 0 ? normalized + count : normalized;
    }
}
