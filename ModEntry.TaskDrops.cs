using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private readonly Dictionary<Tree, TrackedFallingTree> trackedFallingTrees = new(ReferenceEqualityComparer.Instance);

    private void TrackFallingTreeDrops(Tree tree, SquadMemberState member, GameLocation location, Vector2 tile)
    {
        this.trackedFallingTrees[tree] = new TrackedFallingTree(
            member.NpcName,
            location.NameOrUniqueName,
            NormalizeTile(tile),
            member.WorkAreaActive,
            member.WorkAreaOrderId);
        CompanionTaskDropPatches.IsCaptureActive = true;
    }

    /// <summary>
    /// Advances only trees felled by a routine in an unobserved location.
    /// The captured order identity deliberately survives task or block cleanup.
    /// </summary>
    private void AdvanceTrackedFallingTreesOffscreen()
    {
        foreach ((Tree tree, TrackedFallingTree tracked) in this.trackedFallingTrees.ToList())
        {
            GameLocation? location = tree.Location;
            if (!tree.falling.Value
                || location is null
                || !string.Equals(location.NameOrUniqueName, tracked.LocationName, StringComparison.Ordinal)
                || NormalizeTile(tree.Tile) != tracked.Tile)
            {
                this.StopTrackingFallingTree(tree);
                continue;
            }

            if (!TaskNavigationPolicy.ShouldAdvanceRoutineOffscreen(
                    tracked.UsesFixedWorkArea,
                    tracked.WorkAreaOrderId,
                    location.farmers.Any()))
            {
                continue;
            }

            bool removeTree = tree.tickUpdate(Game1.currentGameTime);
            if (!removeTree)
                continue;

            location.terrainFeatures.Remove(tracked.Tile);
            this.InvalidateReachabilityForLocation(location);
        }
    }

    private FallingTreeTickCapture BeforeTrackedTreeTick(Tree tree)
    {
        if (!Context.IsOnHostComputer
            || !Context.IsWorldReady
            || !this.trackedFallingTrees.TryGetValue(tree, out TrackedFallingTree? tracked)
            || tracked is null)
        {
            return default;
        }

        GameLocation? location = tree.Location;
        if (!tree.falling.Value
            || location is null
            || !string.Equals(location.NameOrUniqueName, tracked.LocationName, StringComparison.Ordinal)
            || NormalizeTile(tree.Tile) != tracked.Tile)
        {
            this.StopTrackingFallingTree(tree);
            return default;
        }

        try
        {
            return new FallingTreeTickCapture(location, SnapshotLocationDebris(location));
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't snapshot falling-tree debris for '{tracked.NpcName}'; vanilla drops will remain in the world. {ex.Message}", LogLevel.Warn);
            return default;
        }
    }

    private void AfterTrackedTreeTick(Tree tree, FallingTreeTickCapture capture, bool removeTree)
    {
        if (!this.trackedFallingTrees.TryGetValue(tree, out TrackedFallingTree? tracked)
            || tracked is null)
            return;

        try
        {
            if (capture.IsActive
                && capture.Location is not null
                && capture.DebrisBefore is not null
                && this.members.TryGetValue(tracked.NpcName, out SquadMemberState? member))
            {
                this.RouteNewTaskDebris(
                    member,
                    capture.Location,
                    tracked.Tile,
                    capture.DebrisBefore,
                    "companion.loot_source.wood");
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't route falling-tree drops for '{tracked.NpcName}'; remaining vanilla debris was left in the world. {ex.Message}", LogLevel.Warn);
        }
        finally
        {
            if (removeTree
                || !tree.falling.Value
                || tree.Location is null
                || !string.Equals(tree.Location.NameOrUniqueName, tracked.LocationName, StringComparison.Ordinal))
            {
                this.StopTrackingFallingTree(tree);
            }
        }
    }

    private void PruneTrackedFallingTrees()
    {
        foreach ((Tree tree, TrackedFallingTree tracked) in this.trackedFallingTrees.ToList())
        {
            if (!tree.falling.Value
                || tree.Location is null
                || !string.Equals(tree.Location.NameOrUniqueName, tracked.LocationName, StringComparison.Ordinal)
                || NormalizeTile(tree.Tile) != tracked.Tile)
            {
                this.trackedFallingTrees.Remove(tree);
            }
        }

        CompanionTaskDropPatches.IsCaptureActive = this.trackedFallingTrees.Count > 0;
    }

    private void StopTrackingFallingTree(Tree tree)
    {
        this.trackedFallingTrees.Remove(tree);
        CompanionTaskDropPatches.IsCaptureActive = this.trackedFallingTrees.Count > 0;
    }

    private sealed record TrackedFallingTree(
        string NpcName,
        string LocationName,
        Vector2 Tile,
        bool UsesFixedWorkArea,
        string? WorkAreaOrderId);
}
