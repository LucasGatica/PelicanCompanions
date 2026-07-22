namespace PelicanCompanions;

/// <summary>Pure rules for aligning a cosmetic hat with an NPC sprite frame.</summary>
internal static class NpcHatRenderPolicy
{
    private const int MaxWalkingBobPixels = 2;

    public static bool IsVanillaWalkingStepFrame(
        int currentFrame,
        int framesPerAnimation,
        int spriteWidth,
        int spriteHeight)
    {
        return framesPerAnimation == 4
            && spriteWidth == 16
            && spriteHeight == 32
            && currentFrame is >= 0 and < 16
            && (currentFrame & 1) != 0;
    }

    public static int GetHeadTopDelta(int baselineTop, int currentTop)
    {
        if (baselineTop < 0 || currentTop < 0)
            return 0;

        return Math.Clamp(currentTop - baselineTop, -MaxWalkingBobPixels, MaxWalkingBobPixels);
    }

    public static int FindStableOpaqueTopRow(
        IReadOnlyList<int> opaquePixelsByRow,
        int minimumStablePixels)
    {
        int firstOpaqueRow = -1;
        for (int y = 0; y < opaquePixelsByRow.Count; y++)
        {
            int opaquePixels = opaquePixelsByRow[y];
            if (opaquePixels > 0 && firstOpaqueRow < 0)
                firstOpaqueRow = y;
            if (opaquePixels >= minimumStablePixels)
                return y;
        }

        return firstOpaqueRow;
    }

    public static float ToWorldPixels(int sourcePixelOffset, float spriteScale, int pixelZoom)
    {
        return sourcePixelOffset * Math.Max(0.2f, spriteScale) * Math.Max(1, pixelZoom);
    }
}
