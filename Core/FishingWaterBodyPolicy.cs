using System.Globalization;

namespace PelicanCompanions;

/// <summary>A map tile used by the pure fishing geometry rules.</summary>
internal readonly record struct FishingTile(int X, int Y);

/// <summary>
/// A cardinally connected water component and the non-water tiles which touch
/// it. Collections are sorted by Y and then X so callers get stable results.
/// </summary>
internal sealed record FishingWaterBody(
    string Token,
    FishingTile Anchor,
    IReadOnlyList<FishingTile> WaterTiles,
    IReadOnlyList<FishingTile> ShoreTiles,
    int MapWidth,
    int MapHeight);

/// <summary>A selected cast destination and its geometry-derived metadata.</summary>
internal readonly record struct FishingBobberSelection(
    FishingTile Tile,
    int CastDistance,
    int ApproximateDepth);

/// <summary>
/// Pure discovery and casting rules for a clicked body of water. Runtime map
/// passability and pathfinding remain separate concerns.
/// </summary>
internal static class FishingWaterBodyPolicy
{
    private static readonly FishingTile[] CardinalOffsets =
    {
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    };

    /// <summary>
    /// Discover the complete cardinal component containing <paramref name="clickedTile"/>.
    /// The operation fails closed when the map/start is invalid or the component
    /// would exceed <paramref name="maximumComponentTiles"/>; a partial body is
    /// never returned.
    /// </summary>
    public static bool TryDiscover(
        FishingTile clickedTile,
        int mapWidth,
        int mapHeight,
        Func<FishingTile, bool> isWater,
        int maximumComponentTiles,
        out FishingWaterBody waterBody)
    {
        ArgumentNullException.ThrowIfNull(isWater);
        waterBody = null!;
        if (mapWidth <= 0
            || mapHeight <= 0
            || maximumComponentTiles <= 0
            || !IsInsideMap(clickedTile, mapWidth, mapHeight)
            || !isWater(clickedTile))
        {
            return false;
        }

        HashSet<FishingTile> component = new() { clickedTile };
        Queue<FishingTile> pending = new();
        pending.Enqueue(clickedTile);
        while (pending.TryDequeue(out FishingTile current))
        {
            foreach (FishingTile offset in CardinalOffsets)
            {
                FishingTile neighbor = Add(current, offset);
                if (!IsInsideMap(neighbor, mapWidth, mapHeight)
                    || component.Contains(neighbor)
                    || !isWater(neighbor))
                {
                    continue;
                }

                if (component.Count >= maximumComponentTiles)
                    return false;

                component.Add(neighbor);
                pending.Enqueue(neighbor);
            }
        }

        FishingTile[] waterTiles = SortTiles(component);
        HashSet<FishingTile> shore = new();
        foreach (FishingTile waterTile in waterTiles)
        {
            foreach (FishingTile offset in CardinalOffsets)
            {
                FishingTile neighbor = Add(waterTile, offset);
                if (IsInsideMap(neighbor, mapWidth, mapHeight) && !component.Contains(neighbor))
                    shore.Add(neighbor);
            }
        }

        FishingTile anchor = waterTiles[0];
        waterBody = new FishingWaterBody(
            CreateStableToken(anchor, waterTiles),
            anchor,
            Array.AsReadOnly(waterTiles),
            Array.AsReadOnly(SortTiles(shore)),
            mapWidth,
            mapHeight);
        return true;
    }

    /// <summary>
    /// Select a bobber along a straight cardinal ray from a shore stand. Every
    /// traversed tile must belong to the discovered component. Deeper water wins,
    /// then the longer cast, then the fixed North/East/South/West order.
    /// </summary>
    public static bool TrySelectBobber(
        FishingWaterBody waterBody,
        FishingTile standTile,
        int maximumCastDistance,
        out FishingBobberSelection selection)
    {
        return TrySelectBobber(
            waterBody,
            standTile,
            maximumCastDistance,
            static _ => true,
            out selection);
    }

    /// <summary>
    /// Select a bobber while only accepting destination tiles allowed by the
    /// caller. Disallowed water may still be crossed by the straight cast, but
    /// can never become the bobber destination.
    /// </summary>
    public static bool TrySelectBobber(
        FishingWaterBody waterBody,
        FishingTile standTile,
        int maximumCastDistance,
        Func<FishingTile, bool> isCastTileAllowed,
        out FishingBobberSelection selection)
    {
        ArgumentNullException.ThrowIfNull(waterBody);
        ArgumentNullException.ThrowIfNull(isCastTileAllowed);
        selection = default;
        if (maximumCastDistance <= 0 || waterBody.WaterTiles.Count == 0)
            return false;

        HashSet<FishingTile> water = waterBody.WaterTiles.ToHashSet();
        bool found = false;
        int bestDirectionIndex = int.MaxValue;
        for (int directionIndex = 0; directionIndex < CardinalOffsets.Length; directionIndex++)
        {
            FishingTile offset = CardinalOffsets[directionIndex];
            for (int distance = 1; distance <= maximumCastDistance; distance++)
            {
                FishingTile candidate = new(
                    standTile.X + offset.X * distance,
                    standTile.Y + offset.Y * distance);
                if (!water.Contains(candidate))
                    break;
                if (!isCastTileAllowed(candidate))
                    continue;

                int depth = GetApproximateDepth(waterBody, candidate);
                if (!found
                    || depth > selection.ApproximateDepth
                    || (depth == selection.ApproximateDepth && distance > selection.CastDistance)
                    || (depth == selection.ApproximateDepth
                        && distance == selection.CastDistance
                        && directionIndex < bestDirectionIndex))
                {
                    selection = new FishingBobberSelection(candidate, distance, depth);
                    bestDirectionIndex = directionIndex;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Estimate depth as the cardinal distance to the nearest in-map shore or
    /// map edge. A shoreline water tile has depth 1. The result is zero for a
    /// tile outside this component and can be capped for gameplay balancing.
    /// </summary>
    public static int GetApproximateDepth(
        FishingWaterBody waterBody,
        FishingTile waterTile,
        int maximumDepth = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(waterBody);
        if (maximumDepth <= 0 || !waterBody.WaterTiles.Contains(waterTile))
            return 0;

        int nearestBoundary = Math.Min(
            Math.Min(waterTile.X + 1, waterBody.MapWidth - waterTile.X),
            Math.Min(waterTile.Y + 1, waterBody.MapHeight - waterTile.Y));
        int depth = nearestBoundary;
        foreach (FishingTile shoreTile in waterBody.ShoreTiles)
        {
            int distance = Math.Abs(waterTile.X - shoreTile.X) + Math.Abs(waterTile.Y - shoreTile.Y);
            if (distance < depth)
                depth = distance;
        }

        return Math.Clamp(depth, 1, maximumDepth);
    }

    private static bool IsInsideMap(FishingTile tile, int mapWidth, int mapHeight)
    {
        return tile.X >= 0 && tile.Y >= 0 && tile.X < mapWidth && tile.Y < mapHeight;
    }

    private static FishingTile Add(FishingTile tile, FishingTile offset)
    {
        return new FishingTile(tile.X + offset.X, tile.Y + offset.Y);
    }

    private static FishingTile[] SortTiles(IEnumerable<FishingTile> tiles)
    {
        return tiles
            .OrderBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .ToArray();
    }

    private static string CreateStableToken(FishingTile anchor, IReadOnlyList<FishingTile> waterTiles)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        unchecked
        {
            foreach (FishingTile tile in waterTiles)
            {
                hash ^= (uint)tile.X;
                hash *= prime;
                hash ^= (uint)tile.Y;
                hash *= prime;
            }
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "water|{0},{1}|{2}|{3:X16}",
            anchor.X,
            anchor.Y,
            waterTiles.Count,
            hash);
    }
}
