namespace PelicanCompanions;

/// <summary>Pure radial hit-testing kept separate from rendering and game state.</summary>
internal static class CompanionActionWheelHitTest
{
    public static int? GetSegment(
        float offsetX,
        float offsetY,
        float innerRadius,
        float outerRadius,
        int segmentCount,
        float firstSegmentCenterAngle,
        float separatorHalfGap = 0f)
    {
        if (!float.IsFinite(offsetX)
            || !float.IsFinite(offsetY)
            || !float.IsFinite(innerRadius)
            || !float.IsFinite(outerRadius)
            || !float.IsFinite(firstSegmentCenterAngle)
            || !float.IsFinite(separatorHalfGap)
            || innerRadius < 0f
            || outerRadius <= innerRadius
            || segmentCount is < 1 or > 12
            || separatorHalfGap < 0f)
        {
            return null;
        }

        float segmentAngle = MathF.PI * 2f / segmentCount;
        if (separatorHalfGap >= segmentAngle / 2f)
            return null;

        float distanceSquared = offsetX * offsetX + offsetY * offsetY;
        if (distanceSquared <= innerRadius * innerRadius
            || distanceSquared > outerRadius * outerRadius)
        {
            return null;
        }

        float relativeAngle = NormalizeAngle(
            MathF.Atan2(offsetY, offsetX)
            - firstSegmentCenterAngle
            + segmentAngle / 2f);
        float angleWithinSegment = relativeAngle % segmentAngle;
        if (segmentCount > 1
            && separatorHalfGap > 0f
            && (angleWithinSegment <= separatorHalfGap
                || angleWithinSegment >= segmentAngle - separatorHalfGap))
        {
            return null;
        }

        return Math.Min(segmentCount - 1, (int)MathF.Floor(relativeAngle / segmentAngle));
    }

    private static float NormalizeAngle(float angle)
    {
        float fullTurn = MathF.PI * 2f;
        float normalized = angle % fullTurn;
        return normalized < 0f ? normalized + fullTurn : normalized;
    }
}
