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
    private const int DisconnectedRecoveryObservationThreshold = 3;
    private const int DisconnectedRecoveryNoProgressThreshold = 2;
    private readonly Dictionary<string, DisconnectedFollowRecoveryState> disconnectedFollowRecovery = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DisconnectedFollowBackoffState> disconnectedFollowBackoffs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FollowPathTargetKey, int> failedFollowPathTargets = new();

    private enum ReachabilitySearchResult
    {
        Reachable,
        Unreachable,
        Uncertain
    }

    private readonly record struct DisconnectedFollowRecoveryState(
        int UnreachableUpdates,
        float BestOwnerDistance,
        int NoOwnerProgressUpdates,
        int LastObservedTick);

    private readonly record struct DisconnectedFollowBackoffState(
        string LocationName,
        Vector2 OwnerTile,
        Vector2 NpcTile,
        int StartedTick);

    private readonly record struct FollowPathTargetKey(
        string NpcName,
        string LocationName,
        int X,
        int Y);

    private void UpdateFollowers()
    {
        if (this.IsGlobalSimulationBlocked())
            return;

        this.followPathStartsRemaining = FollowPathStartBudgetPerUpdate;
        this.followDestinationsThisUpdate.Clear();
        this.planningFollowDestinations = true;
        try
        {
            HashSet<string> activeFollowerNames = this.members.Values
            .Where(member => member.Mode == CompanionMode.Following && !this.HasActiveWorkArea(member))
            .Select(member => member.NpcName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string staleName in this.disconnectedFollowRecovery.Keys.Where(name => !activeFollowerNames.Contains(name)).ToList())
                this.disconnectedFollowRecovery.Remove(staleName);
            foreach (string staleName in this.disconnectedFollowBackoffs.Keys.Where(name => !activeFollowerNames.Contains(name)).ToList())
                this.disconnectedFollowBackoffs.Remove(staleName);

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
                    bool tasksEnabled = this.AreTasksEnabled(member.OwnerId);
                    this.UpdateTargetPreview(
                        member,
                        new TargetPreview(
                            false,
                            "",
                            -1,
                            -1,
                            !tasksEnabled
                                ? "companion.preview.tasks_disabled"
                                : this.HasActiveWorkDirective(member)
                                    ? "companion.preview.planning"
                                    : "companion.preview.inactive"));
                    if (tasksEnabled && this.HasActiveWorkArea(member))
                    {
                        this.priorityTaskPlanningMembers.Add(member.NpcName);
                        this.nextTaskScanTick = Game1.ticks + 1;
                    }
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

                if (this.HasActiveWorkArea(member))
                {
                    if (owner is null)
                    {
                        this.StoreWaitingPosition(member, npc);
                        member.Mode = CompanionMode.ParkedForDisconnect;
                        member.ParkedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
                        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
                        this.SetCompanionActivity(member, "companion.status.parked");
                        if (this.IsOwnedCompanionController(npc))
                            this.StopCompanionMovement(npc);
                        this.MarkStateDirty();
                        continue;
                    }

                    if (this.IsOwnedCompanionController(npc))
                        this.StopCompanionMovement(npc);
                    if (!this.AreTasksEnabled(member.OwnerId))
                    {
                        this.SetCompanionActivity(member, "companion.status.work_area_paused");
                        this.SetTaskFailure(member, "companion.task_failure.tasks_disabled");
                        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.tasks_disabled"));
                    }
                    else if (this.HasPendingWorkAreaRecovery(member))
                    {
                        this.SetCompanionActivity(member, "companion.status.work_area_paused");
                        this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
                        this.UpdateTargetPreview(
                            member,
                            new TargetPreview(false, "", -1, -1, "companion.task_failure.work_area_unavailable"));
                    }
                    else if (member.CurrentActivityKey is "companion.status.following"
                        or "companion.status.returning"
                        or "companion.status.parked"
                        || (member.CurrentActivityKey == "companion.status.work_area_paused"
                            && member.LastFailureReasonKey == "companion.task_failure.tasks_disabled"))
                    {
                        if (member.LastFailureReasonKey == "companion.task_failure.tasks_disabled")
                            this.SetTaskFailure(member, "");
                        this.SetCompanionActivity(member, "companion.status.work_area");
                        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.planning"));
                    }
                    continue;
                }

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
                        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
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
                    this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
                    this.SetCompanionActivity(member, "companion.status.stuck");
                    this.SetTaskFailure(member, "companion.task_failure.unexpected_error");
                    this.Monitor.Log($"Follower update failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
                }
            }
        }
        finally
        {
            this.planningFollowDestinations = false;
            this.followPathStartsRemaining = 0;
        }
    }

    private void UpdateFollower(SquadMemberState member, NPC npc, Farmer owner, bool forceCatchUp)
    {
        this.DisableNpcSchedule(npc, stopCurrentRoute: false);
        this.RecordOwnerTrailPoint(owner);
        ResetCompanionMovementSpeed(npc);

        if (this.activeRecallActivatedTicks.TryGetValue(member.NpcName, out int recallActivatedTick))
        {
            if (recallActivatedTick == Game1.ticks)
            {
                this.SetCompanionActivity(member, "companion.status.returning");
                return;
            }

            this.activeRecallActivatedTicks.Remove(member.NpcName);
        }

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
            member.NpcName,
            useOwnerTrail,
            originTile: sameLocation ? npcTile : null);
        float distance = sameLocation ? GetFollowDistance(npc, desiredTile) : 99f;

        if (forceCatchUp)
        {
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
            this.activeRecallTargets.Remove(member.NpcName);
            this.activeRecallActivatedTicks.Remove(member.NpcName);
            this.recoveredFollowTargets.Remove(member.NpcName);
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
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.recoveredFollowTargets.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
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
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
            if (member.LastFailureReasonKey == "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");
            recallActive = false;
        }

        if (recallActive)
        {
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
        }
        else if (this.ShouldHoldDisconnectedFollower(member, npc, ownerLocation, ownerTile, npcTile, ownerDistance))
        {
            return;
        }

        bool usingRecoveredFollowTarget = false;
        if (recallActive)
        {
            this.recoveredFollowTargets.Remove(member.NpcName);
            Vector2 activeRecallTarget = NormalizeTile(this.activeRecallTargets[member.NpcName]);
            bool canReuseRecallTarget = activeRecallTarget != npcTile
                && Vector2.Distance(ownerTile, activeRecallTarget) <= RecallArrivalDistance
                && this.IsTileSafe(ownerLocation, activeRecallTarget)
                && !this.IsFollowDestinationReserved(ownerLocation, activeRecallTarget)
                && !this.IsFollowPathTargetBackedOff(member.NpcName, ownerLocation, activeRecallTarget);
            if (canReuseRecallTarget
                || this.TryFindRecallTargetTile(member.NpcName, ownerLocation, ownerTile, npcTile, out activeRecallTarget))
            {
                desiredTile = activeRecallTarget;
                this.activeRecallTargets[member.NpcName] = activeRecallTarget;
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
        else if (this.recoveredFollowTargets.TryGetValue(member.NpcName, out Vector2 recoveredFollowTarget))
        {
            recoveredFollowTarget = NormalizeTile(recoveredFollowTarget);
            bool canReuseRecoveredTarget = IsWithinCompanionDistance(ownerTile, recoveredFollowTarget)
                && (recoveredFollowTarget == npcTile || this.IsTileSafe(ownerLocation, recoveredFollowTarget))
                && !this.IsFollowDestinationReserved(ownerLocation, recoveredFollowTarget)
                && !this.IsFollowPathTargetBackedOff(member.NpcName, ownerLocation, recoveredFollowTarget);
            if (canReuseRecoveredTarget)
            {
                desiredTile = recoveredFollowTarget;
                distance = GetFollowDistance(npc, desiredTile);
                usingRecoveredFollowTarget = true;
            }
            else
            {
                this.recoveredFollowTargets.Remove(member.NpcName);
            }
        }

        float recoveryArrivalRadius = recallActive ? RecallArrivalDistance : MaxCompanionDistanceTiles;

        if (!recallActive && ownerStationary && ownerDistance <= MaxCompanionDistanceTiles && distance <= StartPathingDistance)
        {
            this.ReserveFollowDestination(ownerLocation, npcTile, member.NpcName);
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.recoveredFollowTargets.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
            if (npc.controller is not null || this.IsOwnedCompanionController(npc))
                this.StopCompanionMovement(npc);
            this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks.Remove(member.NpcName);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
            this.lastFollowTargets[member.NpcName] = npcTile;
            this.lastFollowTargetDistances[member.NpcName] = ownerDistance;
            if (member.LastFailureReasonKey == "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");

            if (member.CurrentActivityKey == "companion.status.returning")
                this.SetCompanionActivity(member, "companion.status.following");
            else if (member.CurrentActivityKey == "companion.status.stuck")
                this.SetCompanionActivity(member, "companion.status.following");

            return;
        }

        if (!recallActive && !usingRecoveredFollowTarget)
            desiredTile = this.GetStableFollowTarget(member, npc, ownerLocation, ownerTile, npcTile, desiredTile, forceCatchUp);

        distance = GetFollowDistance(npc, desiredTile);
        bool targetChanged = !this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 lastTarget) || lastTarget != desiredTile;
        bool desiredTileIsCurrentTile = desiredTile == npcTile;
        bool shouldMove = recallActive
            || distance > StartPathingDistance
            || (ownerDistance > 1.5f && !desiredTileIsCurrentTile);
        this.UpdateFollowProgressCounter(member, npc, shouldMove);

        bool probedConnectivityThisUpdate = false;
        if (!shouldMove)
        {
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
        }
        else if (FollowNavigationPolicy.ShouldProbeConnectivity(
            shouldMove,
            this.followNoProgressTicks.TryGetValue(member.NpcName, out int stalledForProbe) ? stalledForProbe : 0,
            FollowNoProgressUpdatesThreshold,
            Game1.ticks,
            this.lastDisconnectedProbeTicks.TryGetValue(member.NpcName, out int lastProbeTick) ? lastProbeTick : null,
            FollowDisconnectedProbeCooldownTicks))
        {
            probedConnectivityThisUpdate = true;
            this.lastDisconnectedProbeTicks[member.NpcName] = Game1.ticks;
            if (this.TryRecoverDisconnectedFollower(
                member,
                npc,
                owner,
                ownerLocation,
                ownerTile,
                npcTile,
                ownerDistance,
                recoveryArrivalRadius,
                allowReposition: recallActive,
                out Vector2? reachableTarget))
            {
                return;
            }

            if (reachableTarget.HasValue)
            {
                desiredTile = NormalizeTile(reachableTarget.Value);
                distance = GetFollowDistance(npc, desiredTile);
            }
        }

        if (!this.followRecoveryUntilTick.ContainsKey(member.NpcName)
            && this.followNoProgressTicks.TryGetValue(member.NpcName, out int stalledTicks)
            && stalledTicks >= FollowNoProgressUpdatesThreshold)
        {
            this.followNoProgressTicks[member.NpcName] = 0;
            this.lastFollowPathTicks.Remove(member.NpcName);
            if (npc.controller is not null || this.IsOwnedCompanionController(npc))
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
            desiredTile = this.FindSafeCompanionTileNearOwner(
                ownerLocation,
                ownerTile,
                ownerTile,
                npcTile,
                member.NpcName);
        }

        CompanionMovementIntent movementIntent = recallActive
            ? CompanionMovementIntent.Recall
            : CompanionMovementIntent.Follow;
        float controllerTargetRadius = recallActive ? RecallArrivalDistance : MaxCompanionDistanceTiles;
        if (this.TryGetCompanionControllerTarget(npc, movementIntent, ownerLocation, out Vector2 controllerTarget)
            && Vector2.Distance(ownerTile, controllerTarget) <= controllerTargetRadius
            && (controllerTarget == npcTile || this.IsTileTraversable(ownerLocation, controllerTarget))
            && !this.IsFollowDestinationReserved(ownerLocation, controllerTarget)
            && !this.IsFollowPathTargetBackedOff(member.NpcName, ownerLocation, controllerTarget))
        {
            desiredTile = controllerTarget;
        }

        distance = GetFollowDistance(npc, desiredTile);
        this.ReserveFollowDestination(ownerLocation, desiredTile, member.NpcName, force: recallActive);
        targetChanged = !this.lastFollowTargets.TryGetValue(member.NpcName, out lastTarget) || lastTarget != desiredTile;

        desiredTileIsCurrentTile = desiredTile == npcTile;
        shouldMove = recallActive
            || distance > StartPathingDistance
            || (ownerDistance > 1.5f && !desiredTileIsCurrentTile);
        int repathCooldown = isRecovery ? FollowRecoveryRepathCooldownTicks : FollowRepathCooldownTicks;
        bool pathCooldownElapsed = !this.lastFollowPathTicks.TryGetValue(member.NpcName, out int lastPathTick)
            || Game1.ticks - lastPathTick >= repathCooldown;
        bool hasExpectedController = this.HasCompanionController(npc, movementIntent, ownerLocation, desiredTile);
        bool hasPathStartBudget = forceCatchUp || this.followPathStartsRemaining > 0;
        bool needsRepath = FollowNavigationPolicy.ShouldStartPath(
            shouldMove,
            desiredTile == npcTile,
            probedConnectivityThisUpdate,
            hasExpectedController,
            pathCooldownElapsed,
            hasPathStartBudget);
        this.lastFollowTargetDistances[member.NpcName] = distance;

        if (needsRepath)
        {
            if (!forceCatchUp)
                this.followPathStartsRemaining--;

            if (isRecovery && distance <= StartPathingDistance && !targetChanged)
                this.followRecoveryUntilTick.Remove(member.NpcName);

            // Consume both the global budget and this companion's cooldown
            // before path construction so even an exception can't starve later
            // companions on every follower pass.
            this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
            bool pathStarted;
            try
            {
                pathStarted = this.TryStartCompanionPath(npc, ownerLocation, desiredTile, movementIntent);
            }
            catch
            {
                this.RecordFollowPathTargetFailure(member.NpcName, ownerLocation, desiredTile);
                this.lastFollowTargets.Remove(member.NpcName);
                this.recoveredFollowTargets.Remove(member.NpcName);
                if (recallActive)
                    this.activeRecallTargets[member.NpcName] = npcTile;
                throw;
            }

            if (!pathStarted)
            {
                this.RecordFollowPathTargetFailure(member.NpcName, ownerLocation, desiredTile);
                this.lastFollowTargets.Remove(member.NpcName);
                this.recoveredFollowTargets.Remove(member.NpcName);
                if (recallActive)
                    this.activeRecallTargets[member.NpcName] = npcTile;
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                return;
            }
            this.ClearFollowPathTargetFailure(member.NpcName, ownerLocation, desiredTile);
            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.recoveredFollowTargets.Remove(member.NpcName);
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
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.recoveredFollowTargets.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
            if (isRecovery)
                this.followRecoveryUntilTick.Remove(member.NpcName);
            if (member.LastFailureReasonKey == "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");
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
        bool allowReposition,
        out Vector2? reachableTarget)
    {
        reachableTarget = null;
        if (this.CanReachOwnerNeighborhood(
            member.NpcName,
            location,
            npcTile,
            ownerTile,
            arrivalRadius,
            out bool reachabilityUncertain,
            out Vector2 reachableArrivalTile,
            out bool hasSafeReachableTarget))
        {
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.followNoProgressTicks[member.NpcName] = 0;
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
            if (hasSafeReachableTarget)
            {
                if (npc.controller is not null || this.IsOwnedCompanionController(npc))
                    this.StopCompanionMovement(npc);
                reachableTarget = NormalizeTile(reachableArrivalTile);
                if (allowReposition)
                    this.activeRecallTargets[member.NpcName] = reachableTarget.Value;
                else
                    this.recoveredFollowTargets[member.NpcName] = reachableTarget.Value;
            }
            if (member.LastFailureReasonKey == "companion.task_failure.path_recovery")
                this.SetTaskFailure(member, "");
            return false;
        }

        if (reachabilityUncertain)
        {
            // A bounded scan that exhausted its budget is not proof that two
            // areas are disconnected. Never reposition from an uncertain result.
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            return false;
        }

        int unreachableUpdates = 1;
        float bestOwnerDistance = ownerDistance;
        int noOwnerProgressUpdates = 0;
        if (this.disconnectedFollowRecovery.TryGetValue(member.NpcName, out DisconnectedFollowRecoveryState previous)
            && unchecked((uint)(Game1.ticks - previous.LastObservedTick))
                <= (uint)(FollowDisconnectedProbeCooldownTicks * 3))
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
            || noOwnerProgressUpdates < DisconnectedRecoveryNoProgressThreshold)
        {
            return false;
        }

        if (!allowReposition)
        {
            // A transient character in a doorway or the bounded connectivity
            // scan must never make ordinary follow teleport on the same map.
            if (npc.controller is not null || this.IsOwnedCompanionController(npc))
                this.StopCompanionMovement(npc);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.recoveredFollowTargets.Remove(member.NpcName);
            this.disconnectedFollowBackoffs[member.NpcName] = new DisconnectedFollowBackoffState(
                location.NameOrUniqueName,
                NormalizeTile(ownerTile),
                NormalizeTile(npcTile),
                Game1.ticks);
            this.SetCompanionActivity(member, "companion.status.stuck");
            this.SetTaskFailure(member, "companion.task_failure.path_recovery");
            this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
            return true;
        }

        if (!this.TryFindConservativeRecoveryTile(location, owner, this.GetOwnerSlot(member), arrivalRadius, out Vector2 recoveryTile))
            return false;

        this.ClearFollowNavigationState(member.NpcName);
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

    private bool ShouldHoldDisconnectedFollower(
        SquadMemberState member,
        NPC npc,
        GameLocation location,
        Vector2 ownerTile,
        Vector2 npcTile,
        float ownerDistance)
    {
        if (!this.disconnectedFollowBackoffs.TryGetValue(member.NpcName, out DisconnectedFollowBackoffState backoff))
            return false;

        ownerTile = NormalizeTile(ownerTile);
        npcTile = NormalizeTile(npcTile);
        bool contextChanged = !string.Equals(backoff.LocationName, location.NameOrUniqueName, StringComparison.Ordinal)
            || backoff.OwnerTile != ownerTile
            || backoff.NpcTile != npcTile;
        bool expired = unchecked((uint)(Game1.ticks - backoff.StartedTick)) >= FollowDisconnectedBackoffTicks;
        if (contextChanged || expired)
        {
            this.disconnectedFollowBackoffs.Remove(member.NpcName);
            this.disconnectedFollowRecovery.Remove(member.NpcName);
            this.lastDisconnectedProbeTicks.Remove(member.NpcName);
            this.followNoProgressTicks[member.NpcName] = 0;
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            return false;
        }

        if (npc.controller is not null || this.IsOwnedCompanionController(npc))
            this.StopCompanionMovement(npc);
        this.ReserveFollowDestination(location, npcTile, member.NpcName);
        this.lastFollowTargets[member.NpcName] = npcTile;
        this.lastFollowTargetDistances[member.NpcName] = ownerDistance;
        this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
        this.SetCompanionActivity(member, "companion.status.stuck");
        this.SetTaskFailure(member, "companion.task_failure.path_recovery");
        return true;
    }

    private bool IsFollowPathTargetBackedOff(string npcName, GameLocation location, Vector2 targetTile)
    {
        FollowPathTargetKey key = CreateFollowPathTargetKey(npcName, location, targetTile);
        if (!this.failedFollowPathTargets.TryGetValue(key, out int failedTick))
            return false;

        if (unchecked((uint)(Game1.ticks - failedTick)) < FollowTargetFailureBackoffTicks)
            return true;

        this.failedFollowPathTargets.Remove(key);
        return false;
    }

    private void RecordFollowPathTargetFailure(string npcName, GameLocation location, Vector2 targetTile)
    {
        if (this.failedFollowPathTargets.Count > 256)
        {
            foreach (KeyValuePair<FollowPathTargetKey, int> entry in this.failedFollowPathTargets
                .Where(entry => unchecked((uint)(Game1.ticks - entry.Value)) >= FollowTargetFailureBackoffTicks)
                .ToList())
            {
                this.failedFollowPathTargets.Remove(entry.Key);
            }
        }

        this.failedFollowPathTargets[CreateFollowPathTargetKey(npcName, location, targetTile)] = Game1.ticks;
    }

    private void ClearFollowPathTargetFailure(string npcName, GameLocation location, Vector2 targetTile)
    {
        this.failedFollowPathTargets.Remove(CreateFollowPathTargetKey(npcName, location, targetTile));
    }

    private static FollowPathTargetKey CreateFollowPathTargetKey(string npcName, GameLocation location, Vector2 targetTile)
    {
        targetTile = NormalizeTile(targetTile);
        return new FollowPathTargetKey(
            npcName,
            location.NameOrUniqueName,
            (int)targetTile.X,
            (int)targetTile.Y);
    }

    private bool CanReachOwnerNeighborhood(
        string npcName,
        GameLocation location,
        Vector2 npcTile,
        Vector2 ownerTile,
        float arrivalRadius,
        out bool reachabilityUncertain,
        out Vector2 reachableTarget,
        out bool hasSafeReachableTarget)
    {
        npcTile = NormalizeTile(npcTile);
        ownerTile = NormalizeTile(ownerTile);
        reachableTarget = npcTile;
        int maximumArrivalSteps = Math.Max(1, (int)MathF.Ceiling(arrivalRadius * 2f));
        HashSet<Vector2> ownerNeighborhood = this.GetTraversableTilesWithinSteps(
            location,
            ownerTile,
            maximumArrivalSteps);
        HashSet<Vector2> arrivalTiles = ownerNeighborhood
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => Vector2.Distance(ownerTile, candidate) <= arrivalRadius)
            .ToHashSet();
        HashSet<Vector2> safeArrivalTiles = arrivalTiles
            .Where(candidate => candidate == npcTile
                || (this.IsTileSafe(location, candidate)
                    && !this.IsFollowDestinationReserved(location, candidate)
                    && !this.IsFollowPathTargetBackedOff(npcName, location, candidate)))
            .ToHashSet();
        ReachabilitySearchResult result = this.SearchReachableGoal(
            location,
            npcTile,
            arrivalTiles,
            safeArrivalTiles,
            MaxFollowReachabilitySearchTiles,
            out reachableTarget,
            out hasSafeReachableTarget);
        reachabilityUncertain = result == ReachabilitySearchResult.Uncertain;
        return result == ReachabilitySearchResult.Reachable;
    }

    private bool HasRecallArrived(
        GameLocation location,
        Vector2 ownerTile,
        Vector2 npcTile,
        float ownerDistance)
    {
        if (ownerDistance > RecallArrivalDistance)
            return false;

        int maximumArrivalSteps = Math.Max(1, (int)MathF.Ceiling(RecallArrivalDistance * 2f));
        ReachabilitySearchResult result = this.SearchReachableGoal(
            location,
            ownerTile,
            new HashSet<Vector2> { NormalizeTile(npcTile) },
            preferredGoalTiles: null,
            maxVisitedTiles: 64,
            reachedGoal: out _,
            reachedPreferredGoal: out _,
            maxSteps: maximumArrivalSteps);
        return result == ReachabilitySearchResult.Reachable;
    }

    private bool TryFindConservativeRecoveryTile(GameLocation location, Farmer owner, int slot, float arrivalRadius, out Vector2 recoveryTile)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 preferredTile = NormalizeTile(this.GetFormationPreferredTile(owner, slot));
        int searchRadius = Math.Max(1, (int)MathF.Ceiling(arrivalRadius));
        int maximumArrivalSteps = Math.Max(1, (int)MathF.Ceiling(arrivalRadius * 2f));
        HashSet<Vector2> ownerComponent = this.GetTraversableTilesWithinSteps(location, ownerTile, maximumArrivalSteps);
        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, searchRadius)
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => Vector2.Distance(ownerTile, NormalizeTile(candidate)) <= arrivalRadius)
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => ownerComponent.Contains(NormalizeTile(candidate)))
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

    private ReachabilitySearchResult SearchReachableGoal(
        GameLocation location,
        Vector2 originTile,
        HashSet<Vector2> goalTiles,
        HashSet<Vector2>? preferredGoalTiles,
        int maxVisitedTiles,
        out Vector2 reachedGoal,
        out bool reachedPreferredGoal,
        int? maxSteps = null)
    {
        originTile = NormalizeTile(originTile);
        reachedGoal = originTile;
        reachedPreferredGoal = false;
        Vector2? firstReachedGoal = null;
        if (goalTiles.Contains(originTile))
        {
            bool originIsPreferred = preferredGoalTiles is null || preferredGoalTiles.Contains(originTile);
            if (originIsPreferred)
            {
                reachedPreferredGoal = true;
                return ReachabilitySearchResult.Reachable;
            }

            firstReachedGoal = originTile;
            if (preferredGoalTiles is { Count: 0 })
                return ReachabilitySearchResult.Reachable;
        }

        if (goalTiles.Count == 0 || !this.IsTileInsideMap(location, originTile))
            return ReachabilitySearchResult.Unreachable;

        int visitLimit = Math.Max(1, maxVisitedTiles);
        HashSet<Vector2> visited = new() { originTile };
        Queue<(Vector2 Tile, int Steps)> open = new();
        open.Enqueue((originTile, 0));
        bool searchTruncated = false;

        while (open.Count > 0)
        {
            (Vector2 current, int steps) = open.Dequeue();
            if (maxSteps.HasValue && steps >= maxSteps.Value)
                continue;

            foreach (Vector2 offset in CardinalTileOffsets)
            {
                Vector2 next = current + offset;
                if (visited.Contains(next) || !this.IsTileTraversable(location, next))
                    continue;

                if (visited.Count >= visitLimit)
                {
                    searchTruncated = true;
                    continue;
                }

                visited.Add(next);
                if (goalTiles.Contains(next))
                {
                    bool isPreferred = preferredGoalTiles is null || preferredGoalTiles.Contains(next);
                    if (isPreferred)
                    {
                        reachedGoal = next;
                        reachedPreferredGoal = true;
                        return ReachabilitySearchResult.Reachable;
                    }

                    firstReachedGoal ??= next;
                    if (preferredGoalTiles is { Count: 0 })
                    {
                        reachedGoal = next;
                        return ReachabilitySearchResult.Reachable;
                    }
                }

                open.Enqueue((next, steps + 1));
            }
        }

        if (firstReachedGoal.HasValue)
        {
            reachedGoal = firstReachedGoal.Value;
            return ReachabilitySearchResult.Reachable;
        }

        return searchTruncated
            ? ReachabilitySearchResult.Uncertain
            : ReachabilitySearchResult.Unreachable;
    }

    private HashSet<Vector2> GetTraversableTilesWithinSteps(GameLocation location, Vector2 originTile, int maxSteps)
    {
        originTile = NormalizeTile(originTile);
        HashSet<Vector2> visited = new();
        if (!this.IsTileInsideMap(location, originTile))
            return visited;

        visited.Add(originTile);
        Queue<(Vector2 Tile, int Steps)> open = new();
        open.Enqueue((originTile, 0));
        int stepLimit = Math.Max(0, maxSteps);

        while (open.Count > 0)
        {
            (Vector2 current, int steps) = open.Dequeue();
            if (steps >= stepLimit)
                continue;

            foreach (Vector2 offset in CardinalTileOffsets)
            {
                Vector2 next = current + offset;
                if (visited.Contains(next) || !this.IsTileTraversable(location, next))
                    continue;

                visited.Add(next);
                open.Enqueue((next, steps + 1));
            }
        }

        return visited;
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
            || this.IsFollowDestinationReserved(location, activeTarget)
            || this.IsFollowPathTargetBackedOff(member.NpcName, location, activeTarget))
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

    private Vector2 FindCompanionTile(
        GameLocation location,
        Farmer owner,
        int slot,
        string npcName,
        bool useOwnerTrail = true,
        Vector2? originTile = null)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        if (this.config.CompanionFormationMode is CompanionFormationMode.Behind or CompanionFormationMode.Adaptive
            && useOwnerTrail
            && this.TryGetTrailTarget(location, owner, slot, npcName, out Vector2 trailTarget)
            && Vector2.Distance(ownerTile, trailTarget) <= FollowTrailMaxOwnerDistance)
        {
            return trailTarget;
        }

        Vector2 preferred = this.GetFormationPreferredTile(owner, slot);
        return this.FindSafeCompanionTileNearOwner(location, ownerTile, preferred, originTile, npcName);
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

    private Vector2 FindSafeCompanionTileNearOwner(
        GameLocation location,
        Vector2 ownerTile,
        Vector2 preferredTile,
        Vector2? originTile = null,
        string? npcName = null)
    {
        ownerTile = NormalizeTile(ownerTile);
        preferredTile = NormalizeTile(preferredTile);
        originTile = originTile.HasValue ? NormalizeTile(originTile.Value) : null;

        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, MaxCompanionDistanceTiles)
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => IsWithinCompanionDistance(ownerTile, candidate))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => !this.IsFollowDestinationReserved(location, candidate))
            .Where(candidate => npcName is null || !this.IsFollowPathTargetBackedOff(npcName, location, candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, preferredTile))
            .ThenBy(candidate => Vector2.Distance(candidate, ownerTile)))
        {
            return candidate;
        }

        return originTile ?? ownerTile;
    }

    private bool TryGetTrailTarget(GameLocation location, Farmer owner, int slot, string npcName, out Vector2 target)
    {
        target = default;

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
                && !this.IsFollowPathTargetBackedOff(npcName, location, point.Tile))
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
