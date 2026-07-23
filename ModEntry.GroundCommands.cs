using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private bool HasBlockingVisualFeatureUnderCursor(ICursorPosition cursor)
    {
        GameLocation? location = Game1.currentLocation;
        if (location is null)
            return true;

        Point worldPoint = new((int)MathF.Round(cursor.AbsolutePixels.X), (int)MathF.Round(cursor.AbsolutePixels.Y));
        return location.terrainFeatures.Keys.Any(tile =>
        {
            TerrainFeature feature = location.terrainFeatures[tile];
            if (feature is Flooring)
                return false;
            if (feature is HoeDirt dirt && dirt.crop is null)
                return false;

            return feature.getRenderBounds().Contains(worldPoint);
        });
    }

    private bool IsLocalGroundCommandContextValid(string locationName, Vector2 rawTile)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        Farmer owner = Game1.player;
        GameLocation? location = owner.currentLocation;
        Vector2 tile = NormalizeTile(rawTile);
        bool sameLocation = location is not null
            && string.Equals(location.NameOrUniqueName, locationName, StringComparison.Ordinal);
        return GroundCommandPolicy.CanOpen(
            this.members.Values.Any(member => member.OwnerId == ownerId),
            sameLocation,
            location is not null && this.IsGroundCommandTileAvailable(location, tile));
    }

    private IEnumerable<SquadMemberState> GetGroundCommandMembers(string locationName, Vector2 targetTile)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        return this.members.Values
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => GroundCommandPolicy.CanListMember(
                candidate.Member.OwnerId == ownerId,
                candidate.Member.Mode == CompanionMode.ParkedForDisconnect,
                string.Equals(
                    candidate.Npc?.currentLocation?.NameOrUniqueName,
                    locationName,
                    StringComparison.Ordinal)))
            .OrderBy(candidate => Vector2.DistanceSquared(NormalizeTile(candidate.Npc!.Tile), NormalizeTile(targetTile)))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member);
    }

    private bool IsGroundCommandTileAvailable(GameLocation location, Vector2 rawTile)
    {
        Vector2 tile = NormalizeTile(rawTile);
        return this.IsGroundCommandTileStructurallyValid(location, tile)
            && this.IsTileSafe(location, tile);
    }

    private bool IsGroundCommandTileStructurallyValid(GameLocation location, Vector2 rawTile)
    {
        Vector2 tile = NormalizeTile(rawTile);
        if (!this.IsTileTraversable(location, tile)
            || location.Objects.ContainsKey(tile)
            || location.GetHoeDirtAtTile(tile)?.crop is not null)
        {
            return false;
        }

        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is not Flooring
            && (feature is not HoeDirt dirt || dirt.crop is not null))
        {
            return false;
        }

        int x = (int)tile.X;
        int y = (int)tile.Y;
        return !location.warps.Any(warp => warp.X == x && warp.Y == y)
            && string.IsNullOrWhiteSpace(location.doesTileHavePropertyNoNull(x, y, "Action", "Buildings"))
            && string.IsNullOrWhiteSpace(location.doesTileHavePropertyNoNull(x, y, "TouchAction", "Back"));
    }

    private void RequestMoveCompanionToWait(string npcName, string locationName, Vector2 rawTile)
    {
        Vector2 tile = NormalizeTile(rawTile);
        if (!this.IsLocalGroundCommandContextValid(locationName, tile))
        {
            this.Warn("wheel.no_safe_ground");
            return;
        }

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "MoveToWait",
                npcName,
                tile: tile,
                expectedLocationName: locationName);
            return;
        }

        this.TryMoveCompanionToWait(
            Game1.player.UniqueMultiplayerID,
            npcName,
            locationName,
            tile);
    }

    private void TryMoveCompanionToWait(long ownerId, string npcName, string locationName, Vector2 rawTile)
    {
        if (!this.AreTaskActionsSafe(ownerId)
            || !this.members.TryGetValue(npcName, out SquadMemberState? member)
            || !this.CanOwnerMutate(member, ownerId)
            || member.Mode == CompanionMode.ParkedForDisconnect)
        {
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        NPC? npc = this.GetNpcByName(npcName);
        GameLocation? location = owner?.currentLocation;
        Vector2 tile = NormalizeTile(rawTile);
        if (owner is null
            || npc is null
            || location is null
            || npc.currentLocation != location
            || !string.Equals(location.NameOrUniqueName, locationName, StringComparison.Ordinal)
            || !this.IsGroundCommandTileAvailable(location, tile)
            || this.IsStandTileReserved(location, tile, member.NpcName))
        {
            this.WarnForPlayer(ownerId, "wheel.no_safe_ground");
            return;
        }

        Vector2 npcTile = NormalizeTile(npc.Tile);
        ReachabilitySearchResult reachability = this.SearchGroundDestination(
            location,
            npcTile,
            tile,
            MaxFollowReachabilitySearchTiles);
        if (reachability == ReachabilitySearchResult.Unreachable)
        {
            this.WarnForPlayer(ownerId, "wheel.no_safe_ground");
            return;
        }

        // Everything above is a read-only prepare phase. Replacing the old
        // order starts only after definite disconnection has been ruled out;
        // a bounded inconclusive result is left to the real path controller.
        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.RemovePendingTask(member.NpcName);
        this.ResumeFollowing(member.NpcName, ownerId, showMessage: false);
        if (!this.IsGroundCommandTileAvailable(location, tile)
            || this.IsStandTileReserved(location, tile, member.NpcName)
            || !this.TryReserveStandTile(member.NpcName, location.NameOrUniqueName, tile))
        {
            this.WarnForPlayer(ownerId, "wheel.no_safe_ground");
            return;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.MovingToWait,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = tile,
            Manual = true,
            IgnoresTaskMode = true,
            IgnoresTaskToggle = true,
            WorkRadius = MaxCompanionDistanceTiles,
            ReturnDistance = MaxCompanionDistanceTiles,
            StartedTick = Game1.ticks,
            LastProcessedTick = Game1.ticks,
            StandTile = tile
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetTaskFailure(member, "");
        this.SetCompanionActivity(member, "companion.status.moving_to_wait");
        this.SetCompanionTarget(member, CompanionTaskKind.MovingToWait, tile);
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.inactive"));

        this.MarkStateDirty();
        this.InfoForPlayer(ownerId, "wheel.sent_to_wait", new { npc = member.DisplayName });
    }

    private ReachabilitySearchResult SearchGroundDestination(
        GameLocation location,
        Vector2 rawOriginTile,
        Vector2 rawDestinationTile,
        int maxVisitedTiles)
    {
        Vector2 originTile = NormalizeTile(rawOriginTile);
        Vector2 destinationTile = NormalizeTile(rawDestinationTile);
        if (originTile == destinationTile)
            return ReachabilitySearchResult.Reachable;

        if (!this.IsTileInsideMap(location, originTile)
            || !this.IsTileInsideMap(location, destinationTile))
        {
            return ReachabilitySearchResult.Unreachable;
        }

        int visitLimit = Math.Max(1, maxVisitedTiles);
        Dictionary<Vector2, int> distances = new()
        {
            [originTile] = 0
        };
        PriorityQueue<Vector2, int> open = new();
        open.Enqueue(originTile, GetTileManhattanDistance(originTile, destinationTile));
        bool searchTruncated = false;
        while (open.TryDequeue(out Vector2 current, out _))
        {
            int nextDistance = distances[current] + 1;
            foreach (Vector2 offset in CardinalTileOffsets)
            {
                Vector2 next = NormalizeTile(current + offset);
                if (!this.IsTileTraversable(location, next))
                    continue;

                if (next == destinationTile)
                    return ReachabilitySearchResult.Reachable;

                if (distances.TryGetValue(next, out int knownDistance)
                    && knownDistance <= nextDistance)
                {
                    continue;
                }

                if (!distances.ContainsKey(next) && distances.Count >= visitLimit)
                {
                    searchTruncated = true;
                    continue;
                }

                distances[next] = nextDistance;
                int priority = nextDistance + GetTileManhattanDistance(next, destinationTile);
                open.Enqueue(next, priority);
            }
        }

        return searchTruncated
            ? ReachabilitySearchResult.Uncertain
            : ReachabilitySearchResult.Unreachable;
    }

    private static int GetTileManhattanDistance(Vector2 first, Vector2 second)
    {
        return Math.Abs((int)first.X - (int)second.X)
            + Math.Abs((int)first.Y - (int)second.Y);
    }

    private void ProcessPendingMoveToWaitTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member)
            || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        NPC? npc = this.GetNpcByName(task.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        GameLocation? location = owner?.currentLocation;
        if (npc is null
            || owner is null
            || location is null
            || !string.Equals(location.NameOrUniqueName, task.LocationName, StringComparison.Ordinal)
            || npc.currentLocation != location)
        {
            this.FailMoveCompanionToWait(task, member, npc, "companion.task_failure.location_changed");
            return;
        }

        Vector2 targetTile = NormalizeTile(task.TargetTile);
        if (this.IsNpcAtTaskTile(npc, targetTile))
        {
            this.CompleteMoveCompanionToWait(task, member, npc);
            return;
        }

        if (owner.currentLocation != location
            || task.InactiveTicks > InstantTaskTimeoutTicks
            || !this.IsGroundCommandTileStructurallyValid(location, targetTile))
        {
            this.FailMoveCompanionToWait(task, member, npc, "companion.task_failure.path_recovery");
            return;
        }

        this.RouteNpcToTaskTile(npc, location, targetTile, task, force: false);
    }

    private void CompleteMoveCompanionToWait(
        PendingCompanionTask task,
        SquadMemberState member,
        NPC npc)
    {
        member.Mode = CompanionMode.Waiting;
        member.RoutinePausedByPlayer = true;
        member.ParkedAtUtcTicks = 0;
        this.StoreWaitingPosition(member, npc);
        this.SetTaskResult(member, "companion.task_result.waiting_at_point");
        this.RemovePendingTask(task);
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
        this.ClearFollowState(member.NpcName);
        this.DisableNpcSchedule(npc, stopCurrentRoute: true);
        this.MarkStateDirty();
        this.InfoForPlayer(member.OwnerId, "wheel.arrived_to_wait", new { npc = member.DisplayName });
    }

    private void FailMoveCompanionToWait(
        PendingCompanionTask task,
        SquadMemberState member,
        NPC? npc,
        string failureKey)
    {
        bool canWaitWhereStopped = npc?.currentLocation is not null;
        if (canWaitWhereStopped)
        {
            member.Mode = CompanionMode.Waiting;
            member.RoutinePausedByPlayer = true;
            member.ParkedAtUtcTicks = 0;
            this.StoreWaitingPosition(member, npc!);
        }

        this.RemovePendingTask(task, failureKey);
        this.UpdateTargetPreview(
            member,
            new TargetPreview(
                false,
                "",
                -1,
                -1,
                canWaitWhereStopped ? "companion.preview.not_following" : "companion.preview.inactive"));
        this.ClearFollowState(member.NpcName);
        if (npc is not null)
            this.DisableNpcSchedule(npc, stopCurrentRoute: true);
        this.MarkStateDirty();
        this.WarnForPlayer(
            member.OwnerId,
            canWaitWhereStopped ? "wheel.wait_move_failed" : "wheel.wait_move_cancelled",
            new { npc = member.DisplayName });
    }
}
