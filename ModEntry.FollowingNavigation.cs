using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Integrations.GenericModConfigMenu;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int DisconnectedRecoveryObservationThreshold = FollowNoProgressUpdatesThreshold * 3;
    private readonly Dictionary<string, DisconnectedFollowRecoveryState> disconnectedFollowRecovery = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct DisconnectedFollowRecoveryState(
        int UnreachableUpdates,
        float BestOwnerDistance,
        int NoOwnerProgressUpdates,
        int LastObservedTick);

    private void UpdateFollowers()
    {
        if (this.IsGlobalSimulationBlocked())
            return;

        this.followDestinationsThisUpdate.Clear();
        this.planningFollowDestinations = true;
        try
        {
            HashSet<string> activeFollowerNames = this.members.Values
            .Where(member => member.Mode == CompanionMode.Following)
            .Select(member => member.NpcName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string staleName in this.disconnectedFollowRecovery.Keys.Where(name => !activeFollowerNames.Contains(name)).ToList())
                this.disconnectedFollowRecovery.Remove(staleName);

            foreach (SquadMemberState member in this.members.Values.ToList())
            {
                if (member.Mode == CompanionMode.ParkedForDisconnect)
                {
                    Farmer? reconnectedOwner = this.GetOwnerFarmer(member.OwnerId);
                    if (reconnectedOwner is null)
                        continue;

                    member.Mode = CompanionMode.Following;
                    member.ParkedAtUtcTicks = 0;
                    member.WaitingLocationName = null;
                    this.SetCompanionActivity(member, "companion.status.returning");
                    this.ClearFollowState(member.NpcName);
                    this.MarkStateDirty();
                }

                if (member.Mode != CompanionMode.Following)
                    continue;

                if (this.pendingTasks.ContainsKey(member.NpcName))
                    continue;

                NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
                Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
                if (npc is null)
                {
                    if (member.LastFailureReasonKey != "companion.task_failure.npc_missing")
                        this.Monitor.Log($"Companion '{member.NpcName}' isn't currently available; its persistent state was kept.", LogLevel.Warn);
                    this.SetTaskFailure(member, "companion.task_failure.npc_missing");
                    continue;
                }

                if (member.LastFailureReasonKey == "companion.task_failure.npc_missing")
                    this.SetTaskFailure(member, "");

                if (HasIndependentNpcMovementSystem(npc))
                {
                    try
                    {
                        this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
                        this.Monitor.Log($"Companion '{member.NpcName}' was dismissed because its NPC type has an independent movement system.", LogLevel.Warn);
                    }
                    catch (Exception ex)
                    {
                        this.SetTaskFailure(member, "companion.task_failure.unexpected_error");
                        this.Monitor.Log($"Could not safely dismiss unsupported companion '{member.NpcName}': {ex}", LogLevel.Error);
                    }
                    continue;
                }

                if (owner is null)
                {
                    try
                    {
                        this.RemovePendingTask(member.NpcName);
                        this.StoreWaitingPosition(member, npc);
                        member.Mode = CompanionMode.ParkedForDisconnect;
                        member.ParkedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
                        this.SetCompanionActivity(member, "companion.status.parked");
                        this.StopCompanionMovement(npc);
                        this.MarkStateDirty();
                    }
                    catch (Exception ex)
                    {
                        this.SetTaskFailure(member, "companion.task_failure.unexpected_error");
                        this.Monitor.Log($"Parking disconnected companion '{member.NpcName}' failed and was isolated: {ex}", LogLevel.Error);
                    }
                    continue;
                }

                if (this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: true))
                {
                    // PathFindController continues updating behind menus in
                    // multiplayer. Detach the local owner's route so it can't
                    // walk toward a stale target while the arbiter is paused.
                    if (this.IsOwnedCompanionController(npc))
                        this.StopCompanionMovement(npc);
                    continue;
                }

                try
                {
                    this.UpdateFollower(member, npc, owner, forceCatchUp: false);
                }
                catch (Exception ex)
                {
                    try
                    {
                        this.StopCompanionMovement(npc);
                    }
                    catch (Exception stopError)
                    {
                        this.Monitor.Log($"Follower '{member.NpcName}' also failed while stopping its controller: {stopError.Message}", LogLevel.Warn);
                    }
                    this.SetCompanionActivity(member, "companion.status.stuck");
                    this.SetTaskFailure(member, "companion.task_failure.unexpected_error");
                    this.Monitor.Log($"Follower update failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
                }
            }
        }
        finally
        {
            this.planningFollowDestinations = false;
        }
    }

    private void UpdateFollower(SquadMemberState member, NPC npc, Farmer owner, bool forceCatchUp)
    {
        this.DisableNpcSchedule(npc, stopCurrentRoute: false);
        this.RecordOwnerTrailPoint(owner);

        GameLocation ownerLocation = owner.currentLocation;
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        bool sameLocation = npc.currentLocation == ownerLocation;
        bool ownerStationary = sameLocation && this.IsOwnerStationary(owner);
        float ownerDistance = sameLocation ? Vector2.Distance(npcTile, ownerTile) : 99f;
        bool useOwnerTrail = !forceCatchUp
            && !ownerStationary
            && ownerDistance <= FollowTrailMaxOwnerDistance;
        Vector2 desiredTile = this.FindCompanionTile(
            ownerLocation,
            owner,
            this.GetOwnerSlot(member),
            useOwnerTrail,
            originTile: sameLocation ? npcTile : null);
        float distance = sameLocation ? GetFollowDistance(npc, desiredTile) : 99f;

        ResetCompanionMovementSpeed(npc);

        if (forceCatchUp)
        {
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks.Remove(member.NpcName);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions.Remove(member.NpcName);
        }

        if (this.followRecoveryUntilTick.TryGetValue(member.NpcName, out int recoveryUntilTick))
        {
            if (Game1.ticks >= recoveryUntilTick)
            {
                this.followRecoveryUntilTick.Remove(member.NpcName);
                if (member.CurrentActivityKey == "companion.status.stuck")
                    this.SetCompanionActivity(member, "companion.status.returning");
            }
        }

        if (!sameLocation)
        {
            bool recallAcrossLocations = this.activeRecallTargets.ContainsKey(member.NpcName);
            float placementRadius = recallAcrossLocations
                ? RecallArrivalDistance
                : MaxCompanionDistanceTiles;
            if (!this.TryFindConservativeRecoveryTile(
                ownerLocation,
                owner,
                this.GetOwnerSlot(member),
                placementRadius,
                out Vector2 transferTile))
            {
                this.StopCompanionMovement(npc);
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
                return;
            }
            desiredTile = transferTile;

            this.disconnectedFollowRecovery.Remove(member.NpcName);
            if (!recallAcrossLocations)
                this.activeRecallTargets.Remove(member.NpcName);
            this.ReserveFollowDestination(ownerLocation, desiredTile, member.NpcName, force: recallAcrossLocations);
            if (!this.PlaceNpc(npc, ownerLocation, desiredTile))
            {
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
                return;
            }
            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.lastFollowTargetDistances[member.NpcName] = 0f;
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions.Remove(member.NpcName);
            if (member.LastFailureReasonKey is "companion.task_failure.no_safe_tile" or "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");
            bool recallCompleted = recallAcrossLocations
                && Vector2.Distance(NormalizeTile(npc.Tile), ownerTile) <= RecallArrivalDistance;
            if (recallCompleted)
                this.activeRecallTargets.Remove(member.NpcName);
            else if (recallAcrossLocations)
                this.activeRecallTargets[member.NpcName] = NormalizeTile(npc.Tile);

            this.SetCompanionActivity(
                member,
                recallAcrossLocations && !recallCompleted
                    ? "companion.status.returning"
                    : "companion.status.following");
            this.ShowMovementDebugNotice(member, "companion.movement_debug.map_repositioned", new { npc = member.DisplayName });
            return;
        }

        bool recallActive = this.activeRecallTargets.ContainsKey(member.NpcName);
        if (recallActive && this.HasRecallArrived(ownerLocation, ownerTile, npcTile, ownerDistance))
        {
            this.activeRecallTargets.Remove(member.NpcName);
            recallActive = false;
        }

        if (recallActive)
        {
            if (this.TryFindRecallTargetTile(ownerLocation, ownerTile, npcTile, out Vector2 recallTarget))
            {
                desiredTile = recallTarget;
                this.activeRecallTargets[member.NpcName] = recallTarget;
                distance = GetFollowDistance(npc, desiredTile);
            }
            else
            {
                // Keep recall active so disconnected-path recovery can use the
                // real arrival radius instead of silently degrading to follow.
                desiredTile = npcTile;
                distance = 0f;
            }
        }

        float recoveryArrivalRadius = recallActive ? RecallArrivalDistance : MaxCompanionDistanceTiles;
        if (this.TryRecoverDisconnectedFollower(
            member,
            npc,
            owner,
            ownerLocation,
            ownerTile,
            npcTile,
            ownerDistance,
            recoveryArrivalRadius,
            allowReposition: recallActive))
            return;

        if (!recallActive && ownerStationary && ownerDistance <= MaxCompanionDistanceTiles && distance <= StartPathingDistance)
        {
            this.ReserveFollowDestination(ownerLocation, npcTile, member.NpcName);
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.StopCompanionMovement(npc);
            this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks.Remove(member.NpcName);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
            this.lastFollowTargets[member.NpcName] = npcTile;
            this.lastFollowTargetDistances[member.NpcName] = ownerDistance;

            if (member.CurrentActivityKey == "companion.status.returning")
                this.SetCompanionActivity(member, "companion.status.following");
            else if (member.CurrentActivityKey == "companion.status.stuck")
                this.SetCompanionActivity(member, "companion.status.following");

            return;
        }

        if (!recallActive)
            desiredTile = this.GetStableFollowTarget(member, npc, ownerLocation, ownerTile, npcTile, desiredTile, forceCatchUp);

        distance = GetFollowDistance(npc, desiredTile);
        bool targetChanged = !this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 lastTarget) || lastTarget != desiredTile;
        bool desiredTileIsCurrentTile = desiredTile == npcTile;
        bool shouldMove = distance > StartPathingDistance || (ownerDistance > 1.5f && !desiredTileIsCurrentTile);
        this.UpdateFollowProgressCounter(member, npc, shouldMove);

        if (!this.followRecoveryUntilTick.ContainsKey(member.NpcName)
            && this.followNoProgressTicks.TryGetValue(member.NpcName, out int stalledTicks)
            && stalledTicks >= FollowNoProgressUpdatesThreshold)
        {
            this.followNoProgressTicks[member.NpcName] = 0;
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.StopCompanionMovement(npc);
            if (!recallActive)
            {
                this.followRecoveryUntilTick[member.NpcName] = Game1.ticks + FollowRecoveryDurationTicks;
                this.activeRecallTargets.Remove(member.NpcName);
            }
            this.SetCompanionActivity(member, "companion.status.stuck");
            this.SetTaskFailure(member, "companion.task_failure.path_recovery");
            this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
        }

        bool isRecovery = this.followRecoveryUntilTick.TryGetValue(member.NpcName, out int activeRecoveryUntilTick) && Game1.ticks < activeRecoveryUntilTick;
        if (isRecovery)
        {
            this.activeRecallTargets.Remove(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.stuck");
            desiredTile = this.FindSafeCompanionTileNearOwner(ownerLocation, ownerTile, ownerTile, npcTile);
        }

        distance = GetFollowDistance(npc, desiredTile);
        this.ReserveFollowDestination(ownerLocation, desiredTile, member.NpcName, force: recallActive);
        targetChanged = !this.lastFollowTargets.TryGetValue(member.NpcName, out lastTarget) || lastTarget != desiredTile;

        desiredTileIsCurrentTile = desiredTile == npcTile;
        shouldMove = distance > StartPathingDistance || (ownerDistance > 1.5f && !desiredTileIsCurrentTile);
        int repathCooldown = isRecovery ? FollowRecoveryRepathCooldownTicks : FollowRepathCooldownTicks;
        bool pathCooldownElapsed = !this.lastFollowPathTicks.TryGetValue(member.NpcName, out int lastPathTick)
            || Game1.ticks - lastPathTick >= repathCooldown;
        CompanionMovementIntent movementIntent = recallActive
            ? CompanionMovementIntent.Recall
            : CompanionMovementIntent.Follow;
        bool hasExpectedController = this.HasCompanionController(npc, movementIntent, ownerLocation, desiredTile);
        bool needsRepath = shouldMove
            && ((!hasExpectedController && pathCooldownElapsed)
                || (hasExpectedController && targetChanged && pathCooldownElapsed));
        this.lastFollowTargetDistances[member.NpcName] = distance;

        if (needsRepath)
        {
            if (isRecovery && distance <= StartPathingDistance && !targetChanged)
                this.followRecoveryUntilTick.Remove(member.NpcName);

            Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(ownerLocation, npcTile, MaxFollowReachabilitySearchTiles);
            if (!IsReachableOrUncertain(reachableDistances, desiredTile, MaxFollowReachabilitySearchTiles))
            {
                this.StopCompanionMovement(npc);
                if (!recallActive)
                    this.activeRecallTargets.Remove(member.NpcName);
                this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
                return;
            }

            if (!this.TryStartCompanionPath(npc, ownerLocation, desiredTile, movementIntent))
            {
                this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                return;
            }
            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
            if (member.LastFailureReasonKey == "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");
            if (member.CurrentActivityKey == "companion.status.stuck")
                this.SetCompanionActivity(member, "companion.status.returning");
        }
        else if (!shouldMove)
        {
            if (npc.controller is not null || this.IsOwnedCompanionController(npc))
                this.StopCompanionMovement(npc);

            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
            if (!recallActive)
                this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks[member.NpcName] = 0;
            if (isRecovery)
                this.followRecoveryUntilTick.Remove(member.NpcName);
        }

        if (!shouldMove
            && !recallActive
            && (member.CurrentActivityKey == "companion.status.returning" || member.CurrentActivityKey == "companion.status.stuck"))
            this.SetCompanionActivity(member, "companion.status.following");
    }

    private bool TryRecoverDisconnectedFollower(
        SquadMemberState member,
        NPC npc,
        Farmer owner,
        GameLocation location,
        Vector2 ownerTile,
        Vector2 npcTile,
        float ownerDistance,
        float arrivalRadius,
        bool allowReposition)
    {
        if (this.CanReachOwnerNeighborhood(location, npcTile, ownerTile, arrivalRadius, out bool reachabilityUncertain))
        {
            bool recovered = this.disconnectedFollowRecovery.Remove(member.NpcName);
            if (recovered && member.LastFailureReasonKey == "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");
            return false;
        }

        int unreachableUpdates = 1;
        float bestOwnerDistance = ownerDistance;
        int noOwnerProgressUpdates = 0;
        if (this.disconnectedFollowRecovery.TryGetValue(member.NpcName, out DisconnectedFollowRecoveryState previous)
            && Game1.ticks >= previous.LastObservedTick
            && Game1.ticks - previous.LastObservedTick <= FollowUpdateIntervalTicks * 3)
        {
            unreachableUpdates = previous.UnreachableUpdates + 1;
            bool movedMeaningfullyCloser = ownerDistance < previous.BestOwnerDistance - FollowProgressTolerance;
            bestOwnerDistance = movedMeaningfullyCloser ? ownerDistance : previous.BestOwnerDistance;
            noOwnerProgressUpdates = movedMeaningfullyCloser
                ? 0
                : previous.NoOwnerProgressUpdates + 1;
        }

        this.disconnectedFollowRecovery[member.NpcName] = new DisconnectedFollowRecoveryState(
            unreachableUpdates,
            bestOwnerDistance,
            noOwnerProgressUpdates,
            Game1.ticks);

        if (unreachableUpdates < DisconnectedRecoveryObservationThreshold
            || noOwnerProgressUpdates < FollowNoProgressUpdatesThreshold)
        {
            return false;
        }

        if (!allowReposition)
        {
            if (reachabilityUncertain)
                return false;

            // A transient character in a doorway or the bounded connectivity
            // scan must never make ordinary follow teleport on the same map.
            this.StopCompanionMovement(npc);
            this.SetCompanionActivity(member, "companion.status.stuck");
            this.SetTaskFailure(member, "companion.task_failure.path_recovery");
            this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
            return true;
        }

        if (!this.TryFindConservativeRecoveryTile(location, owner, this.GetOwnerSlot(member), arrivalRadius, out Vector2 recoveryTile))
            return false;

        this.ClearFollowState(member.NpcName);
        this.disconnectedFollowRecovery.Remove(member.NpcName);
        this.ReserveFollowDestination(location, recoveryTile, member.NpcName);
        if (!this.PlaceNpc(npc, location, recoveryTile))
        {
            this.SetCompanionActivity(member, "companion.status.stuck");
            return true;
        }
        this.lastFollowTargets[member.NpcName] = recoveryTile;
        this.lastFollowTargetDistances[member.NpcName] = 0f;
        this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
        this.SetCompanionActivity(member, "companion.status.following");
        if (member.LastFailureReasonKey == "companion.task_failure.path_recovery")
            this.SetTaskFailure(member, "");
        this.ShowMovementDebugNotice(member, "companion.movement_debug.map_repositioned", new { npc = member.DisplayName });
        return true;
    }

    private bool CanReachOwnerNeighborhood(
        GameLocation location,
        Vector2 npcTile,
        Vector2 ownerTile,
        float arrivalRadius,
        out bool reachabilityUncertain)
    {
        Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles);
        Dictionary<Vector2, int> ownerDistances = this.GetReachableTileDistances(location, ownerTile, MaxFollowReachabilitySearchTiles);
        int searchRadius = Math.Max(1, (int)MathF.Ceiling(arrivalRadius));
        int maximumArrivalSteps = Math.Max(1, (int)MathF.Ceiling(arrivalRadius * 2f));
        bool reachable = this.GetNearbyTiles(ownerTile, searchRadius)
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => Vector2.Distance(NormalizeTile(ownerTile), NormalizeTile(candidate)) <= arrivalRadius)
            .Any(candidate => ownerDistances.TryGetValue(NormalizeTile(candidate), out int ownerPathDistance)
                && ownerPathDistance <= maximumArrivalSteps
                && reachableDistances.ContainsKey(NormalizeTile(candidate))
                && this.IsTileTraversable(location, candidate));
        reachabilityUncertain = !reachable
            && (reachableDistances.Count >= MaxFollowReachabilitySearchTiles
                || ownerDistances.Count >= MaxFollowReachabilitySearchTiles);
        return reachable;
    }

    private bool HasRecallArrived(
        GameLocation location,
        Vector2 ownerTile,
        Vector2 npcTile,
        float ownerDistance)
    {
        if (ownerDistance > RecallArrivalDistance)
            return false;

        Dictionary<Vector2, int> ownerDistances = this.GetReachableTileDistances(
            location,
            ownerTile,
            MaxFollowReachabilitySearchTiles);
        int maximumArrivalSteps = Math.Max(1, (int)MathF.Ceiling(RecallArrivalDistance * 2f));
        return ownerDistances.TryGetValue(NormalizeTile(npcTile), out int pathDistance)
            && pathDistance <= maximumArrivalSteps;
    }

    private bool TryFindConservativeRecoveryTile(GameLocation location, Farmer owner, int slot, float arrivalRadius, out Vector2 recoveryTile)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 preferredTile = NormalizeTile(this.GetFormationPreferredTile(owner, slot));
        int searchRadius = Math.Max(1, (int)MathF.Ceiling(arrivalRadius));
        Dictionary<Vector2, int> ownerComponent = this.GetReachableTileDistances(location, ownerTile, MaxFollowReachabilitySearchTiles);
        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, searchRadius)
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => Vector2.Distance(ownerTile, NormalizeTile(candidate)) <= arrivalRadius)
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => ownerComponent.ContainsKey(NormalizeTile(candidate)))
            .Where(candidate => !this.IsFollowDestinationReserved(location, candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, preferredTile))
            .ThenBy(candidate => Vector2.Distance(candidate, ownerTile)))
        {
            recoveryTile = NormalizeTile(candidate);
            return true;
        }

        recoveryTile = ownerTile;
        return false;
    }

    private bool IsFollowDestinationReserved(GameLocation location, Vector2 tile)
    {
        string key = this.GetWorkTargetKey(location.NameOrUniqueName, tile);
        return this.workStandReservations.ContainsKey(key)
            || this.followDestinationsThisUpdate.ContainsKey(key);
    }

    private void ReserveFollowDestination(GameLocation location, Vector2 tile, string npcName, bool force = false)
    {
        if (this.planningFollowDestinations || force)
            this.followDestinationsThisUpdate.TryAdd(this.GetWorkTargetKey(location.NameOrUniqueName, tile), npcName);
    }

    private static float GetFollowDistance(NPC npc, Vector2 desiredTile)
    {
        Vector2 npcPositionTile = npc.Position / 64f;
        return Vector2.Distance(npcPositionTile, NormalizeTile(desiredTile));
    }

    private Vector2 GetStableFollowTarget(
        SquadMemberState member,
        NPC npc,
        GameLocation location,
        Vector2 ownerTile,
        Vector2 npcTile,
        Vector2 proposedTile,
        bool forceCatchUp)
    {
        proposedTile = NormalizeTile(proposedTile);
        if (forceCatchUp || !this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 activeTarget))
            return proposedTile;

        activeTarget = NormalizeTile(activeTarget);
        if (activeTarget == proposedTile || activeTarget == npcTile)
            return proposedTile;

        if (!IsWithinCompanionDistance(ownerTile, activeTarget)
            || !this.IsTileSafe(location, activeTarget)
            || this.IsFollowDestinationReserved(location, activeTarget))
            return proposedTile;

        float activeDistance = GetFollowDistance(npc, activeTarget);
        if (activeDistance <= StartPathingDistance)
            return proposedTile;

        bool pathCooldownElapsed = !this.lastFollowPathTicks.TryGetValue(member.NpcName, out int lastPathTick)
            || Game1.ticks - lastPathTick >= FollowRepathCooldownTicks;
        bool targetMovedFarEnough = Vector2.Distance(activeTarget, proposedTile) >= FollowRetargetDistanceThreshold;
        if (pathCooldownElapsed && targetMovedFarEnough)
            return proposedTile;

        return activeTarget;
    }

    private void UpdateFollowProgressCounter(SquadMemberState member, NPC npc, bool shouldMove)
    {
        Vector2 positionTile = npc.Position / 64f;
        if (!shouldMove)
        {
            this.lastFollowProgressPositions[member.NpcName] = positionTile;
            this.followNoProgressTicks[member.NpcName] = 0;
            return;
        }

        if (this.lastFollowProgressPositions.TryGetValue(member.NpcName, out Vector2 lastPosition)
            && Vector2.Distance(positionTile, lastPosition) <= FollowPositionProgressTolerance)
        {
            this.followNoProgressTicks[member.NpcName] = this.followNoProgressTicks.TryGetValue(member.NpcName, out int stalled) ? stalled + 1 : 1;
        }
        else
        {
            this.followNoProgressTicks[member.NpcName] = 0;
        }

        this.lastFollowProgressPositions[member.NpcName] = positionTile;
    }

    private static void ResetCompanionMovementSpeed(NPC npc)
    {
        npc.speed = DefaultCompanionMovementSpeed;
        npc.addedSpeed = 0;
    }

    private void UpdateOwnerTrails()
    {
        HashSet<long> activeOwners = this.members.Values
            .Where(p => p.Mode == CompanionMode.Following)
            .Select(p => p.OwnerId)
            .ToHashSet();

        foreach (long ownerId in activeOwners)
        {
            Farmer? owner = this.GetOwnerFarmer(ownerId);
            if (owner is not null)
                this.RecordOwnerTrailPoint(owner);
        }

        foreach (long ownerId in this.ownerTrails.Keys.Where(p => !activeOwners.Contains(p)).ToList())
            this.ownerTrails.Remove(ownerId);

        foreach (long ownerId in this.ownerMovementSnapshots.Keys.Where(p => !activeOwners.Contains(p)).ToList())
            this.ownerMovementSnapshots.Remove(ownerId);
    }

    private bool IsOwnerStationary(Farmer owner)
    {
        if (owner.currentLocation is null)
            return false;

        long ownerId = owner.UniqueMultiplayerID;
        string locationName = owner.currentLocation.NameOrUniqueName;
        Vector2 position = owner.Position;

        if (!this.ownerMovementSnapshots.TryGetValue(ownerId, out OwnerMovementSnapshot snapshot))
        {
            this.ownerMovementSnapshots[ownerId] = new OwnerMovementSnapshot(locationName, position, Game1.ticks, Game1.ticks, false);
            return false;
        }

        if (snapshot.LastObservedTick == Game1.ticks)
            return snapshot.IsStationary;

        bool moved = snapshot.LocationName != locationName || Vector2.DistanceSquared(snapshot.Position, position) > 1f;
        int lastMoveTick = moved ? Game1.ticks : snapshot.LastMoveTick;
        bool isStationary = !moved && Game1.ticks - lastMoveTick >= OwnerStationaryThresholdTicks;

        this.ownerMovementSnapshots[ownerId] = new OwnerMovementSnapshot(locationName, position, lastMoveTick, Game1.ticks, isStationary);
        return isStationary;
    }

    private void RecordOwnerTrailPoint(Farmer owner)
    {
        if (owner.currentLocation is null)
            return;

        string locationName = owner.currentLocation.NameOrUniqueName;
        Vector2 tile = new((int)owner.Tile.X, (int)owner.Tile.Y);
        if (!this.ownerTrails.TryGetValue(owner.UniqueMultiplayerID, out List<FollowTrailPoint>? trail))
        {
            trail = new List<FollowTrailPoint>();
            this.ownerTrails[owner.UniqueMultiplayerID] = trail;
        }

        if (trail.Count > 0)
        {
            FollowTrailPoint last = trail[^1];
            if (last.LocationName != locationName || Vector2.Distance(last.Tile, tile) > 8f)
                trail.Clear();
            else if (last.Tile == tile)
                return;
        }

        trail.Add(new FollowTrailPoint(locationName, tile, Game1.ticks));
        if (trail.Count > MaxTrailPointsPerOwner)
            trail.RemoveRange(0, trail.Count - MaxTrailPointsPerOwner);
    }

    private Vector2 FindCompanionTile(GameLocation location, Farmer owner, int slot, bool useOwnerTrail = true, Vector2? originTile = null)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        if (this.config.CompanionFormationMode is CompanionFormationMode.Behind or CompanionFormationMode.Adaptive
            && useOwnerTrail
            && this.TryGetTrailTarget(location, owner, slot, originTile, out Vector2 trailTarget)
            && Vector2.Distance(ownerTile, trailTarget) <= FollowTrailMaxOwnerDistance)
        {
            return trailTarget;
        }

        Vector2 preferred = this.GetFormationPreferredTile(owner, slot);
        return this.FindSafeCompanionTileNearOwner(location, ownerTile, preferred, originTile);
    }

    private Vector2 GetFormationPreferredTile(Farmer owner, int slot)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 direction = owner.FacingDirection switch
        {
            0 => new Vector2(0, 1),
            1 => new Vector2(-1, 0),
            2 => new Vector2(0, -1),
            3 => new Vector2(1, 0),
            _ => new Vector2(0, 1)
        };

        Vector2 side = new(-direction.Y, direction.X);
        if (this.config.CompanionFormationMode == CompanionFormationMode.Adaptive)
        {
            // While moving, Adaptive uses the breadcrumb trail above. When the
            // owner stops (or a trail tile isn't safe), companions settle into a
            // readable crescent instead of stacking on the same follow tile.
            Vector2[] adaptiveOffsets =
            {
                direction,
                side,
                -side,
                direction + side,
                direction - side,
                -direction,
                direction * 2,
                side * 2,
                -side * 2,
                direction * 2 + side,
                direction * 2 - side,
                -direction + side
            };

            return ownerTile + adaptiveOffsets[Math.Clamp(slot, 0, adaptiveOffsets.Length - 1)];
        }

        if (this.config.CompanionFormationMode == CompanionFormationMode.Compact)
        {
            Vector2[] compactOffsets =
            {
                direction,
                side,
                -side,
                direction + side,
                direction - side,
                direction * 2,
                direction * 2 + side,
                direction * 2 - side,
                side * 2,
                -side * 2,
                direction * 3,
                direction * 2 + side * 2
            };

            return ownerTile + compactOffsets[Math.Clamp(slot, 0, compactOffsets.Length - 1)];
        }

        int row = slot / 3 + 1;
        int column = slot % 3 - 1;
        return ownerTile + direction * row + side * column;
    }

    private Vector2 FindSafeCompanionTileNearOwner(GameLocation location, Vector2 ownerTile, Vector2 preferredTile, Vector2? originTile = null)
    {
        ownerTile = NormalizeTile(ownerTile);
        preferredTile = NormalizeTile(preferredTile);
        originTile = originTile.HasValue ? NormalizeTile(originTile.Value) : null;
        Dictionary<Vector2, int>? reachableDistances = originTile.HasValue
            ? this.GetReachableTileDistances(location, originTile.Value, MaxFollowReachabilitySearchTiles)
            : null;

        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, MaxCompanionDistanceTiles)
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => IsWithinCompanionDistance(ownerTile, candidate))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => !this.IsFollowDestinationReserved(location, candidate))
            .Where(candidate => reachableDistances is null
                || IsReachableOrUncertain(reachableDistances, candidate, MaxFollowReachabilitySearchTiles))
            .OrderBy(candidate => Vector2.Distance(candidate, preferredTile))
            .ThenBy(candidate => reachableDistances is not null && reachableDistances.TryGetValue(candidate, out int pathDistance) ? pathDistance : 0)
            .ThenBy(candidate => Vector2.Distance(candidate, ownerTile)))
        {
            return candidate;
        }

        return originTile ?? ownerTile;
    }

    private bool TryGetTrailTarget(GameLocation location, Farmer owner, int slot, Vector2? originTile, out Vector2 target)
    {
        target = default;
        originTile = originTile.HasValue ? NormalizeTile(originTile.Value) : null;
        Dictionary<Vector2, int>? reachableDistances = originTile.HasValue
            ? this.GetReachableTileDistances(location, originTile.Value, MaxFollowReachabilitySearchTiles)
            : null;

        if (!this.ownerTrails.TryGetValue(owner.UniqueMultiplayerID, out List<FollowTrailPoint>? trail) || trail.Count < 2)
            return false;

        string locationName = location.NameOrUniqueName;
        FollowTrailPoint latest = trail[^1];
        if (latest.LocationName != locationName || Game1.ticks - latest.Tick > StationaryTrailExpiryTicks)
            return false;

        int lag = Math.Min(trail.Count - 1, BaseTrailLag + slot * TrailLagPerFollower);
        int targetIndex = Math.Max(0, trail.Count - 1 - lag);

        for (int i = targetIndex; i >= 0; i--)
        {
            FollowTrailPoint point = trail[i];
            if (point.LocationName == locationName
                && this.IsTileSafe(location, point.Tile)
                && !this.IsFollowDestinationReserved(location, point.Tile)
                && (reachableDistances is null
                    || IsReachableOrUncertain(reachableDistances, point.Tile, MaxFollowReachabilitySearchTiles)))
            {
                target = point.Tile;
                return true;
            }
        }

        return false;
    }

    private Dictionary<Vector2, int> GetReachableTileDistances(GameLocation location, Vector2 originTile, int maxVisitedTiles)
    {
        originTile = NormalizeTile(originTile);
        ReachabilityCacheKey cacheKey = new(
            location.NameOrUniqueName,
            (int)originTile.X,
            (int)originTile.Y,
            maxVisitedTiles);
        if (this.reachabilityCache.TryGetValue(cacheKey, out ReachabilityCacheEntry cached)
            && unchecked((uint)(Game1.ticks - cached.Tick)) <= ReachabilityCacheTtlTicks)
        {
            return cached.Distances;
        }

        if (this.reachabilityCache.Count > 128)
        {
            foreach (ReachabilityCacheKey staleKey in this.reachabilityCache
                .Where(p => unchecked((uint)(Game1.ticks - p.Value.Tick)) > ReachabilityCacheTtlTicks)
                .Select(p => p.Key)
                .ToList())
            {
                this.reachabilityCache.Remove(staleKey);
            }
        }

        Dictionary<Vector2, int> distances = new()
        {
            [originTile] = 0
        };

        if (!this.IsTileInsideMap(location, originTile))
        {
            this.reachabilityCache[cacheKey] = new ReachabilityCacheEntry(Game1.ticks, distances);
            return distances;
        }

        Queue<Vector2> open = new();
        open.Enqueue(originTile);
        while (open.Count > 0 && distances.Count < maxVisitedTiles)
        {
            Vector2 current = open.Dequeue();
            int nextDistance = distances[current] + 1;
            foreach (Vector2 offset in CardinalTileOffsets)
            {
                Vector2 next = current + offset;
                if (distances.ContainsKey(next) || !this.IsTileTraversable(location, next))
                    continue;

                distances[next] = nextDistance;
                open.Enqueue(next);
                if (distances.Count >= maxVisitedTiles)
                    break;
            }
        }

        this.reachabilityCache[cacheKey] = new ReachabilityCacheEntry(Game1.ticks, distances);
        return distances;
    }

    private static bool IsReachableOrUncertain(
        Dictionary<Vector2, int> reachableDistances,
        Vector2 tile,
        int maxVisitedTiles)
    {
        return reachableDistances.ContainsKey(NormalizeTile(tile))
            || reachableDistances.Count >= maxVisitedTiles;
    }

    private void InvalidateReachabilityForLocation(GameLocation location)
    {
        string locationName = location.NameOrUniqueName;
        foreach (ReachabilityCacheKey key in this.reachabilityCache.Keys
            .Where(key => string.Equals(key.LocationName, locationName, StringComparison.Ordinal))
            .ToList())
        {
            this.reachabilityCache.Remove(key);
        }
    }

    private IEnumerable<Vector2> GetNearbyTiles(Vector2 center, int radius)
    {
        yield return new Vector2((int)center.X, (int)center.Y);
        for (int distance = 1; distance <= radius; distance++)
        {
            for (int x = -distance; x <= distance; x++)
            {
                for (int y = -distance; y <= distance; y++)
                {
                    if (Math.Abs(x) != distance && Math.Abs(y) != distance)
                        continue;

                    yield return new Vector2((int)center.X + x, (int)center.Y + y);
                }
            }
        }
    }

    private bool IsTileSafe(GameLocation location, Vector2 tile)
    {
        return this.IsTileTraversable(location, tile)
            && !location.IsTileOccupiedBy(tile, CollisionMask.Farmers)
            && !location.IsTileOccupiedBy(
                tile,
                CollisionMask.Characters,
                CollisionMask.Characters);
    }

    private bool IsTileTraversable(GameLocation location, Vector2 tile)
    {
        tile = NormalizeTile(tile);
        if (!this.IsTileInsideMap(location, tile))
            return false;

        int x = (int)tile.X;
        int y = (int)tile.Y;
        bool explicitlyPassable = this.HasTilePropertyOnLayer(location, x, y, "NPCPassable", "Buildings")
            || this.HasTilePropertyOnLayer(location, x, y, "Passable", "Buildings")
            || this.HasPassableAction(location, x, y);
        bool blockedByBackLayer = this.HasTilePropertyOnLayer(location, x, y, "Passable", "Back");
        bool layerPassable = location.isTilePassable(tile) || explicitlyPassable;
        CollisionMask structuralMask = ~(CollisionMask.Characters | CollisionMask.Farmers);
        return !blockedByBackLayer
            && (!location.isWaterTile(x, y) || explicitlyPassable)
            && !this.HasBlockingTileProperty(location, x, y)
            && layerPassable
            && !location.IsTileOccupiedBy(tile, structuralMask, structuralMask);
    }

    private bool HasBlockingTileProperty(GameLocation location, int x, int y)
    {
        return this.HasTileProperty(location, x, y, "NPCBarrier")
            || this.HasTileProperty(location, x, y, "TemporaryBarrier")
            || this.HasTileProperty(location, x, y, "NoPath")
            || this.HasTileProperty(location, x, y, "NoPathing");
    }

    private bool HasTileProperty(GameLocation location, int x, int y, string propertyName)
    {
        foreach (string layer in TilePropertyLayers)
        {
            if (!string.IsNullOrWhiteSpace(location.doesTileHavePropertyNoNull(x, y, propertyName, layer)))
                return true;
        }

        return false;
    }

    private bool HasPassableAction(GameLocation location, int x, int y)
    {
        string action = location.doesTileHavePropertyNoNull(x, y, "Action", "Buildings");
        return !string.IsNullOrWhiteSpace(action)
            && !action.StartsWith("LockedDoorWarp", StringComparison.OrdinalIgnoreCase)
            && (action.Contains("Door", StringComparison.OrdinalIgnoreCase)
                || action.Contains("Passable", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasTilePropertyOnLayer(
        GameLocation location,
        int x,
        int y,
        string propertyName,
        string layer)
    {
        return !string.IsNullOrWhiteSpace(location.doesTileHavePropertyNoNull(x, y, propertyName, layer));
    }

    private bool IsTileInsideMap(GameLocation location, Vector2 tile)
    {
        if (location.Map is null || location.Map.Layers.Count == 0)
            return false;

        int x = (int)tile.X;
        int y = (int)tile.Y;
        if (x < 0 || y < 0)
            return false;

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;

        return x < width && y < height;
    }

    private bool IsTileWithinOwnerRange(SquadMemberState member, GameLocation location, Vector2 tile)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        return owner is not null
            && owner.currentLocation == location
            && IsWithinCompanionDistance(owner.Tile, tile);
    }

    private static bool IsWithinCompanionDistance(Vector2 ownerTile, Vector2 candidateTile)
    {
        return Vector2.Distance(NormalizeTile(ownerTile), NormalizeTile(candidateTile)) <= MaxCompanionDistanceTiles;
    }

    private static bool IsWithinOwnerDistance(Vector2 ownerTile, Vector2 candidateTile, int maxDistance)
    {
        return Vector2.Distance(NormalizeTile(ownerTile), NormalizeTile(candidateTile)) <= maxDistance;
    }

    private static Vector2 NormalizeTile(Vector2 tile)
    {
        return new Vector2((int)tile.X, (int)tile.Y);
    }

    private bool PlaceNpc(
        NPC npc,
        GameLocation location,
        Vector2 tile,
        bool maintainCompanionControl = true,
        bool suppressVanillaArrival = false,
        bool allowOccupiedExactTile = false)
    {
        tile = NormalizeTile(tile);
        bool alreadyAtTile = npc.currentLocation == location && NormalizeTile(npc.Tile) == tile;
        if (!alreadyAtTile && allowOccupiedExactTile && !this.IsTileInsideMap(location, tile))
            return false;
        if (!alreadyAtTile
            && !allowOccupiedExactTile
            && !this.IsTileSafe(location, tile)
            && !this.TryFindNearestSafeTile(location, tile, SafePlacementSearchRadius, out tile))
        {
            return false;
        }

        this.StopCompanionMovement(npc);

        GameLocation? oldLocation = npc.currentLocation;
        if (oldLocation != location)
        {
            bool suppressArrival = maintainCompanionControl || suppressVanillaArrival;
            if (suppressArrival)
                this.BeginSuppressingVanillaArrival(npc);
            try
            {
                Game1.warpCharacter(npc, location, tile);
            }
            finally
            {
                if (suppressArrival)
                    this.EndSuppressingVanillaArrival(npc);
            }
        }
        else
        {
            npc.Position = tile * 64f;
        }

        if (maintainCompanionControl)
        {
            this.DisableNpcSchedule(npc, stopCurrentRoute: false);
            // FarmHouse arrival hooks can move spouses to the entry/kitchen and
            // install a vanilla controller synchronously. Reassert the exact
            // companion destination after neutralizing those hooks.
            npc.Position = tile * 64f;
        }

        npc.position.Field.CancelInterpolation();

        return true;
    }

    private void BeginSuppressingVanillaArrival(NPC npc)
    {
        this.suppressedVanillaArrivals[npc] = this.suppressedVanillaArrivals.TryGetValue(npc, out int depth)
            ? depth + 1
            : 1;
    }

    private bool IsVanillaMovementAllowed(NPC npc)
    {
        return this.vanillaMovementAllowances.ContainsKey(npc);
    }

    private void BeginAllowingVanillaMovement(NPC npc)
    {
        this.vanillaMovementAllowances[npc] = this.vanillaMovementAllowances.TryGetValue(npc, out int depth)
            ? depth + 1
            : 1;
    }

    private void EndAllowingVanillaMovement(NPC npc)
    {
        if (!this.vanillaMovementAllowances.TryGetValue(npc, out int depth) || depth <= 1)
            this.vanillaMovementAllowances.Remove(npc);
        else
            this.vanillaMovementAllowances[npc] = depth - 1;
    }

    private void EndSuppressingVanillaArrival(NPC npc)
    {
        if (!this.suppressedVanillaArrivals.TryGetValue(npc, out int depth) || depth <= 1)
            this.suppressedVanillaArrivals.Remove(npc);
        else
            this.suppressedVanillaArrivals[npc] = depth - 1;
    }

    private bool TryFindNearestSafeTile(GameLocation location, Vector2 centerTile, int radius, out Vector2 safeTile)
    {
        centerTile = NormalizeTile(centerTile);
        foreach (Vector2 candidate in this.GetNearbyTiles(centerTile, radius)
            .Where(candidate => this.IsTileSafe(location, candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, centerTile)))
        {
            safeTile = candidate;
            return true;
        }

        safeTile = centerTile;
        return false;
    }
}
