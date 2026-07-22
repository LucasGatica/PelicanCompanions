namespace PelicanCompanions;

/// <summary>Pure validation and geometry for fixed companion work areas.</summary>
internal static class CompanionWorkAreaPolicy
{
    public const int MinimumRadius = 3;
    public const int MaximumRadius = 20;
    public const int MinimumSquareSize = 3;
    public const int MaximumSquareSize = 41;

    public static int NormalizeRadius(int radius)
    {
        return Math.Clamp(radius, MinimumRadius, MaximumRadius);
    }

    public static int ClampRadiusToMaximum(int radius, int maximumRadius)
    {
        return Math.Min(NormalizeRadius(radius), NormalizeRadius(maximumRadius));
    }

    public static int NormalizeSquareSize(int size)
    {
        return Math.Clamp(size, MinimumSquareSize, MaximumSquareSize);
    }

    public static bool Contains(int centerX, int centerY, int radius, int tileX, int tileY, int padding = 0)
    {
        long normalizedRadius = (long)NormalizeRadius(radius) + Math.Max(0L, padding);
        long deltaX = Math.Abs((long)tileX - centerX);
        long deltaY = Math.Abs((long)tileY - centerY);
        if (deltaX > normalizedRadius || deltaY > normalizedRadius)
            return false;

        return deltaX * deltaX
            <= normalizedRadius * normalizedRadius - deltaY * deltaY;
    }

    /// <summary>Whether a tile is inside an exact half-open square, optionally expanded for a stand tile.</summary>
    public static bool ContainsSquare(
        int minX,
        int minY,
        int size,
        int tileX,
        int tileY,
        int padding = 0)
    {
        if (!IsSquareGeometryValid(minX, minY, size))
            return false;

        long normalizedPadding = Math.Max(0, padding);
        long left = (long)minX - normalizedPadding;
        long top = (long)minY - normalizedPadding;
        long rightExclusive = (long)minX + size + normalizedPadding;
        long bottomExclusive = (long)minY + size + normalizedPadding;
        return tileX >= left
            && tileX < rightExclusive
            && tileY >= top
            && tileY < bottomExclusive;
    }

    /// <summary>Whether a tile belongs to a map-wide region with trusted positive dimensions.</summary>
    public static bool ContainsFarmWide(int mapWidth, int mapHeight, int tileX, int tileY)
    {
        return mapWidth > 0
            && mapHeight > 0
            && tileX >= 0
            && tileX < mapWidth
            && tileY >= 0
            && tileY < mapHeight;
    }

    /// <summary>Whether the persisted geometry is structurally valid without requiring its map to be loaded.</summary>
    public static bool IsRegionGeometryValid(
        CompanionWorkRegionKind regionKind,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size)
    {
        return regionKind switch
        {
            CompanionWorkRegionKind.Circle => centerX >= 0
                && centerY >= 0
                && radius is >= MinimumRadius and <= MaximumRadius,
            CompanionWorkRegionKind.DelimitedSquare => IsSquareGeometryValid(minX, minY, size),
            CompanionWorkRegionKind.FarmWide => true,
            _ => false
        };
    }

    /// <summary>Validates a routine preset without requiring its location asset to be loaded.</summary>
    public static bool IsRoutineRegionValid(
        CompanionWorkRegionKind regionKind,
        string? locationName,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size)
    {
        return !string.IsNullOrWhiteSpace(locationName)
            && Enum.IsDefined(regionKind)
            && IsRegionGeometryValid(
                regionKind,
                centerX,
                centerY,
                radius,
                minX,
                minY,
                size);
    }

    /// <summary>Whether a region is valid for a concrete map with the supplied dimensions.</summary>
    public static bool IsRegionGeometryValid(
        CompanionWorkRegionKind regionKind,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size,
        int mapWidth,
        int mapHeight)
    {
        if (mapWidth <= 0 || mapHeight <= 0)
            return false;

        return regionKind switch
        {
            CompanionWorkRegionKind.Circle => IsRegionGeometryValid(
                regionKind,
                centerX,
                centerY,
                radius,
                minX,
                minY,
                size),
            CompanionWorkRegionKind.DelimitedSquare => IsSquareGeometryValid(
                minX,
                minY,
                size,
                mapWidth,
                mapHeight),
            CompanionWorkRegionKind.FarmWide => true,
            _ => false
        };
    }

    /// <summary>Tests a tile against one of the persisted region geometries.</summary>
    public static bool Contains(
        CompanionWorkRegionKind regionKind,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size,
        int tileX,
        int tileY,
        int mapWidth,
        int mapHeight,
        int padding = 0)
    {
        return ContainsRegion(
            regionKind,
            centerX,
            centerY,
            radius,
            minX,
            minY,
            size,
            tileX,
            tileY,
            mapWidth,
            mapHeight,
            padding);
    }

    public static bool ContainsRegion(
        CompanionWorkRegionKind regionKind,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size,
        int tileX,
        int tileY,
        int mapWidth,
        int mapHeight,
        int padding = 0)
    {
        if (!IsRegionGeometryValid(
                regionKind,
                centerX,
                centerY,
                radius,
                minX,
                minY,
                size,
                mapWidth,
                mapHeight))
        {
            return false;
        }

        return regionKind switch
        {
            CompanionWorkRegionKind.Circle => Contains(
                centerX,
                centerY,
                radius,
                tileX,
                tileY,
                padding),
            CompanionWorkRegionKind.DelimitedSquare => ContainsFarmWide(
                    mapWidth,
                    mapHeight,
                    tileX,
                    tileY)
                && ContainsSquare(minX, minY, size, tileX, tileY, padding),
            CompanionWorkRegionKind.FarmWide => ContainsFarmWide(
                mapWidth,
                mapHeight,
                tileX,
                tileY),
            _ => false
        };
    }

    public static bool IsSquareGeometryValid(int minX, int minY, int size)
    {
        return minX >= 0
            && minY >= 0
            && size is >= MinimumSquareSize and <= MaximumSquareSize;
    }

    public static bool IsSquareGeometryValid(
        int minX,
        int minY,
        int size,
        int mapWidth,
        int mapHeight)
    {
        if (!IsSquareGeometryValid(minX, minY, size)
            || mapWidth <= 0
            || mapHeight <= 0)
        {
            return false;
        }

        return (long)minX + size <= mapWidth
            && (long)minY + size <= mapHeight;
    }

    public static bool IsDelimitedSquareInsideMap(
        int minX,
        int minY,
        int size,
        int mapWidth,
        int mapHeight)
    {
        return IsSquareGeometryValid(minX, minY, size, mapWidth, mapHeight);
    }

    public static bool Allows(CompanionWorkSpecialty specialty, CompanionTaskKind kind)
    {
        return specialty switch
        {
            CompanionWorkSpecialty.Wood => kind == CompanionTaskKind.Lumbering,
            CompanionWorkSpecialty.Mining => kind == CompanionTaskKind.Mining,
            CompanionWorkSpecialty.Watering => kind == CompanionTaskKind.Watering,
            CompanionWorkSpecialty.ClearArea => kind is CompanionTaskKind.Lumbering or CompanionTaskKind.Mining,
            _ => false
        };
    }

    public static bool IsPersistedStateValid(
        bool active,
        string? orderId,
        string? locationName,
        int centerX,
        int centerY,
        int radius,
        CompanionWorkSpecialty specialty)
    {
        if (!active)
            return true;

        return !string.IsNullOrWhiteSpace(orderId)
            && !string.IsNullOrWhiteSpace(locationName)
            && centerX >= 0
            && centerY >= 0
            && radius is >= MinimumRadius and <= MaximumRadius
            && Enum.IsDefined(specialty);
    }

    public static bool IsPersistedStateValid(
        bool active,
        string? orderId,
        string? locationName,
        CompanionWorkRegionKind regionKind,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size,
        CompanionWorkSpecialty specialty)
    {
        if (!active)
            return true;

        return !string.IsNullOrWhiteSpace(orderId)
            && !string.IsNullOrWhiteSpace(locationName)
            && Enum.IsDefined(regionKind)
            && IsRegionGeometryValid(
                regionKind,
                centerX,
                centerY,
                radius,
                minX,
                minY,
                size)
            && Enum.IsDefined(specialty);
    }

    public static bool IsActiveStateValid(
        bool active,
        string? orderId,
        string? locationName,
        int centerX,
        int centerY,
        int radius,
        CompanionWorkSpecialty specialty)
    {
        return active && IsPersistedStateValid(
            active,
            orderId,
            locationName,
            centerX,
            centerY,
            radius,
            specialty);
    }

    public static bool IsActiveStateValid(
        bool active,
        string? orderId,
        string? locationName,
        CompanionWorkRegionKind regionKind,
        int centerX,
        int centerY,
        int radius,
        int minX,
        int minY,
        int size,
        CompanionWorkSpecialty specialty)
    {
        return active && IsPersistedStateValid(
            active,
            orderId,
            locationName,
            regionKind,
            centerX,
            centerY,
            radius,
            minX,
            minY,
            size,
            specialty);
    }
}
