namespace PelicanCompanions;

internal enum CompanionIdleAnimationKind
{
    LookAround,
    Happy,
    Jump,
    Shake
}

/// <summary>Validates data-driven idle animation names without executing arbitrary sprite commands.</summary>
internal static class CompanionIdleAnimationPolicy
{
    public static CompanionIdleAnimationKind? Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "look" or "look-around" => CompanionIdleAnimationKind.LookAround,
            "happy" or "emote" => CompanionIdleAnimationKind.Happy,
            "jump" => CompanionIdleAnimationKind.Jump,
            "shake" => CompanionIdleAnimationKind.Shake,
            _ => null
        };
    }

    public static int NormalizeIntervalSeconds(int value)
    {
        return Math.Clamp(value, 5, 300);
    }

    public static int NormalizeInteractionCooldownSeconds(int value)
    {
        return Math.Clamp(value, 15, 1800);
    }
}
