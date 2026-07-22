using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace PelicanCompanions;

/// <summary>Observes the exact vanilla tick which finishes a companion-felled tree.</summary>
internal static class CompanionTaskDropPatches
{
    public static bool IsCaptureActive { get; set; }

    public static Func<Tree, FallingTreeTickCapture>? BeforeTrackedTreeTick { get; set; }

    public static Action<Tree, FallingTreeTickCapture, bool>? AfterTrackedTreeTick { get; set; }

    [HarmonyPatch(typeof(Tree), nameof(Tree.tickUpdate), typeof(GameTime))]
    private static class TreeTickUpdatePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Tree __instance, out FallingTreeTickCapture __state)
        {
            __state = default;
            if (!IsCaptureActive)
                return;

            try
            {
                if (BeforeTrackedTreeTick is not null)
                    __state = BeforeTrackedTreeTick(__instance);
            }
            catch
            {
                // A compatibility guard must never interrupt the vanilla tree tick.
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Tree __instance, FallingTreeTickCapture __state, bool __result)
        {
            if (!IsCaptureActive && !__state.IsActive)
                return;

            try
            {
                AfterTrackedTreeTick?.Invoke(__instance, __state, __result);
            }
            catch
            {
                // Leave any unclaimed debris in the world and let vanilla continue.
            }
        }
    }
}

internal readonly record struct FallingTreeTickCapture(
    GameLocation? Location,
    IReadOnlySet<Debris>? DebrisBefore)
{
    public bool IsActive => this.Location is not null && this.DebrisBefore is not null;
}
