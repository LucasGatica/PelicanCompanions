namespace PelicanCompanions;

/// <summary>Pure validation and geometry for fixed companion work areas.</summary>
internal static class CompanionWorkAreaPolicy
{
    public const int MinimumRadius = 3;
    public const int MaximumRadius = 20;

    public static int NormalizeRadius(int radius)
    {
        return Math.Clamp(radius, MinimumRadius, MaximumRadius);
    }

    public static int ClampRadiusToMaximum(int radius, int maximumRadius)
    {
        return Math.Min(NormalizeRadius(radius), NormalizeRadius(maximumRadius));
    }

    public static bool Contains(int centerX, int centerY, int radius, int tileX, int tileY, int padding = 0)
    {
        int normalizedRadius = NormalizeRadius(radius) + Math.Max(0, padding);
        long deltaX = (long)tileX - centerX;
        long deltaY = (long)tileY - centerY;
        return deltaX * deltaX + deltaY * deltaY <= (long)normalizedRadius * normalizedRadius;
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
}
