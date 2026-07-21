using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Integrations.GenericModConfigMenu;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Crops;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int InstantTaskTimeoutTicks = 480;

    private void TryManualTask(ICursorPosition cursor)
    {
        Vector2 tile = NormalizeTile(cursor.GrabTile);
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("ManualTask", tile: tile);
            return;
        }

        this.TryManualTask(Game1.player.UniqueMultiplayerID, tile);
    }

    private void TryManualTask(long ownerId, Vector2 tile)
    {
        if (!this.AreTaskActionsSafe(ownerId))
            return;

        SquadMemberState? member = this.GetAvailableMember(ownerId);
        if (member is null)
        {
            this.Warn("commands.no_followers");
            return;
        }

        if (!this.AreTasksEnabled(ownerId))
        {
            this.Warn("tasks.disabled");
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        GameLocation? location = owner?.currentLocation;
        if (location is null)
            return;

        tile = NormalizeTile(tile);
        if (this.TryHarvestTile(location, tile, member, manual: true))
            return;

        if (this.TryPetAnimalAtTile(location, tile, member, manual: true))
            return;

        if (this.TryWaterTile(location, tile, member, manual: true))
            return;

        if (this.TryGatherTile(location, tile, member, manual: true))
            return;

        if (this.TryLumberTile(location, tile, member, manual: true))
            return;

        if (this.TryMiningTile(location, tile, member, manual: true))
            return;

        this.SetTaskFailure(member, "companion.task_failure.no_valid_target");
        this.Warn("tasks.no_valid_target");
    }

    private bool TryQueueInstantTask(
        CompanionTaskKind kind,
        GameLocation location,
        Vector2 targetTile,
        SquadMemberState member,
        bool manual,
        long targetEntityId = 0,
        bool ignoreTaskMode = false,
        string sharedTargetGroupId = "",
        bool ignoreTaskToggle = false,
        string expectedTargetToken = "",
        object? expectedTargetInstance = null,
        Vector2? preparedStandTile = null)
    {
        if (this.pendingTasks.ContainsKey(member.NpcName)
            || this.activeRecallTargets.ContainsKey(member.NpcName)
            || member.Mode != CompanionMode.Following)
            return false;

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        targetTile = NormalizeTile(targetTile);
        if (npc is null
            || owner is null
            || npc.currentLocation != location
            || owner.currentLocation != location
            || !IsWithinCompanionDistance(owner.Tile, targetTile))
        {
            return false;
        }

        if (!IsWithinCompanionDistance(owner.Tile, npc.Tile))
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            this.SetCompanionActivity(member, "companion.status.returning");
            return false;
        }

        Vector2 standTile;
        if (preparedStandTile is Vector2 prepared)
            standTile = NormalizeTile(prepared);
        else if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, MaxCompanionDistanceTiles, out standTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            return false;
        }

        bool targetReserved = string.IsNullOrWhiteSpace(sharedTargetGroupId)
            ? this.TryReserveWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile)
            : this.TryReserveSharedWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile, sharedTargetGroupId);
        if (!targetReserved)
        {
            this.SetTaskFailure(member, "companion.task_failure.target_reserved");
            return false;
        }

        if (!this.TryReserveStandTile(member.NpcName, location.NameOrUniqueName, standTile))
        {
            this.ReleaseWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile);
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = kind,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            TargetEntityId = targetEntityId,
            Manual = manual,
            UsesConfiguredAutonomy = !manual && this.IsConfiguredAutonomousTaskKind(kind),
            IgnoresTaskMode = ignoreTaskMode,
            IgnoresTaskToggle = ignoreTaskToggle,
            ExpectedTargetToken = expectedTargetToken,
            ExpectedTargetInstance = expectedTargetInstance,
            SharedTargetGroupId = sharedTargetGroupId,
            WorkRadius = MaxCompanionDistanceTiles,
            ReturnDistance = MaxCompanionDistanceTiles,
            StartedTick = Game1.ticks,
            LastProcessedTick = Game1.ticks,
            StandTile = standTile
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.working");
        this.SetCompanionTarget(member, kind, targetTile);

        return true;
    }

    private bool IsPendingAnimalTarget(long animalId)
    {
        return this.pendingTasks.Values.Any(task => task.Kind == CompanionTaskKind.Petting && task.TargetEntityId == animalId);
    }

    private bool IsConfiguredAutonomousTaskKind(CompanionTaskKind kind)
    {
        return kind switch
        {
            CompanionTaskKind.Watering => this.config.WateringMode == TaskMode.Autonomous,
            CompanionTaskKind.Gathering => this.config.ForagingMode == TaskMode.Autonomous,
            CompanionTaskKind.Harvesting => this.config.HarvestingMode == TaskMode.Autonomous,
            CompanionTaskKind.Petting => this.config.PettingMode == TaskMode.Autonomous,
            _ => false
        };
    }

    private void TryMimicToolUse(ICursorPosition cursor)
    {
        Vector2 cursorTile = NormalizeTile(cursor.GrabTile);
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("MimicToolUse", tile: cursorTile);
            return;
        }

        this.TryMimicToolUse(Game1.player.UniqueMultiplayerID, cursorTile);
    }

    private void TryMimicToolUse(long ownerId, Vector2 cursorTile)
    {
        if (!this.AreTasksEnabled(ownerId) || !this.AreTaskActionsSafe(ownerId))
            return;

        SquadMemberState? member = this.GetAvailableMember(ownerId);
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        GameLocation? location = owner?.currentLocation;
        if (member is null || owner is null || location is null)
            return;

        if (owner.CurrentTool is Axe && this.config.LumberingMode == TaskMode.Mimicking)
        {
            Vector2 aimedTile = NormalizeTile(cursorTile);
            if (!this.IsValidWoodTarget(location, aimedTile))
                aimedTile = NormalizeTile(this.GetFacingTile(owner));

            if (!this.IsValidWoodTarget(location, aimedTile))
                return;

            foreach (Vector2 nearbyTile in location.terrainFeatures.Keys
                .Where(candidate => NormalizeTile(candidate) != aimedTile)
                .Where(candidate => IsWithinCompanionDistance(owner.Tile, candidate))
                .Where(candidate => this.IsValidWoodTarget(location, candidate))
                .OrderBy(candidate => Vector2.Distance(aimedTile, NormalizeTile(candidate))))
            {
                if (this.TryLumberTile(location, nearbyTile, member, manual: false))
                    return;
            }

            return;
        }

        if (owner.CurrentTool is Pickaxe && this.config.MiningMode == TaskMode.Mimicking)
        {
            Vector2 aimedTile = NormalizeTile(cursorTile);
            if (!this.IsValidMiningTarget(location, aimedTile))
                aimedTile = NormalizeTile(this.GetFacingTile(owner));

            if (!this.IsValidMiningTarget(location, aimedTile))
                return;

            foreach (Vector2 nearbyTile in location.Objects.Keys
                .Where(candidate => NormalizeTile(candidate) != aimedTile)
                .Where(candidate => IsWithinCompanionDistance(owner.Tile, candidate))
                .Where(candidate => this.IsValidMiningTarget(location, candidate))
                .OrderBy(candidate => Vector2.Distance(aimedTile, NormalizeTile(candidate))))
            {
                if (this.TryMiningTile(location, nearbyTile, member, manual: false))
                    return;
            }

            return;
        }

        if (owner.CurrentTool is WateringCan && this.config.WateringMode == TaskMode.Mimicking)
        {
            Vector2 aimedTile = NormalizeTile(cursorTile);
            HoeDirt? aimedDirt = location.GetHoeDirtAtTile(aimedTile);
            if (aimedDirt?.needsWatering() != true)
                return;

            foreach (Vector2 nearbyTile in this.GetNearbyTiles(owner.Tile, MaxCompanionDistanceTiles)
                .Where(candidate => NormalizeTile(candidate) != aimedTile))
            {
                if (this.TryWaterTile(location, nearbyTile, member, manual: false))
                    return;
            }
        }
    }

    private void UpdateAutonomousTasks()
    {
        if (!Context.IsMainPlayer || this.IsGlobalSimulationBlocked())
            return;

        foreach (string expiredTarget in this.workTargetRetryAfterTicks
            .Where(entry => Game1.ticks >= entry.Value)
            .Select(entry => entry.Key)
            .ToList())
        {
            this.workTargetRetryAfterTicks.Remove(expiredTarget);
        }

        List<SquadMemberState> eligibleMembers = this.members.Values
            .Where(p => p.Mode == CompanionMode.Following
                && !this.pendingTasks.ContainsKey(p.NpcName)
                && !this.activeRecallTargets.ContainsKey(p.NpcName)
                && p.CurrentActivityKey != "companion.status.returning")
            .Where(member =>
            {
                Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
                return owner?.currentLocation is not null
                    && !this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: true)
                    && this.AreTasksEnabled(member.OwnerId);
            })
            .ToList();
        this.priorityTaskPlanningMembers.RemoveWhere(
            npcName => !this.members.TryGetValue(npcName, out SquadMemberState? priorityMember)
                || !this.HasActiveWorkDirective(priorityMember));
        IReadOnlyList<string> selectedNames = TaskPlanningPolicy.SelectMembers(
            eligibleMembers.Select(member => member.NpcName),
            this.priorityTaskPlanningMembers,
            this.taskPlanningCursor,
            TaskPlanningBudgetPerScan,
            out this.taskPlanningCursor);
        foreach (string npcName in selectedNames)
        {
            if (!this.members.TryGetValue(npcName, out SquadMemberState? member))
                continue;

            try
            {
                Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
                GameLocation? location = owner?.currentLocation;
                if (owner is null
                    || location is null
                    || this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: true)
                    || !this.AreTasksEnabled(member.OwnerId))
                    continue;

                this.priorityTaskPlanningMembers.Remove(member.NpcName);
                if (this.HasActiveWorkDirective(member))
                {
                    this.TryAssignWorkDirectiveTask(member);
                    continue;
                }

                if (this.TryAssignConfiguredAutonomousTask(member))
                    continue;

                if (this.config.PettingMode == TaskMode.Autonomous
                    && this.TryPetNearestAnimal(location, member))
                {
                    continue;
                }

                if (this.config.HarvestingMode == TaskMode.Autonomous)
                {
                    foreach (Vector2 tile in this.GetNearbyTiles(owner.Tile, radius: MaxCompanionDistanceTiles))
                    {
                        if (this.TryHarvestTile(location, tile, member, manual: false))
                            break;
                    }

                    if (this.pendingTasks.ContainsKey(member.NpcName))
                        continue;
                }

                if (this.config.EnableGathering && this.config.ForagingMode == TaskMode.Autonomous)
                {
                    foreach (Vector2 tile in this.GetNearbyTiles(owner.Tile, radius: MaxCompanionDistanceTiles))
                    {
                        if (this.TryGatherTile(location, tile, member, manual: false))
                            break;
                    }

                    if (this.pendingTasks.ContainsKey(member.NpcName))
                        continue;
                }

                if (this.config.WateringMode == TaskMode.Autonomous)
                {
                    foreach (Vector2 tile in this.GetNearbyTiles(owner.Tile, radius: MaxCompanionDistanceTiles))
                    {
                        if (this.TryWaterTile(location, tile, member, manual: false))
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                this.SetTaskFailure(member, "companion.task_failure.unexpected_error");
                this.Monitor.Log($"Autonomous task planning failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
            }
        }
    }

    private bool TryWaterTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe(member.OwnerId) || location is null)
            return false;

        if (this.config.WateringMode == TaskMode.Disabled)
            return false;

        tile = NormalizeTile(tile);
        if (!this.IsTileWithinOwnerRange(member, location, tile))
            return false;

        HoeDirt? dirt = location.GetHoeDirtAtTile(tile);
        if (dirt is null || !dirt.needsWatering())
            return false;

        return this.TryQueueInstantTask(CompanionTaskKind.Watering, location, tile, member, manual);
    }

    private bool TryGatherTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe(member.OwnerId) || location is null)
            return false;

        if (!this.config.EnableGathering || this.config.ForagingMode == TaskMode.Disabled)
            return false;

        tile = NormalizeTile(tile);
        if (!this.IsTileWithinOwnerRange(member, location, tile))
            return false;

        if (!location.Objects.TryGetValue(tile, out SObject? obj) || !obj.IsSpawnedObject)
            return false;

        return this.TryQueueInstantTask(CompanionTaskKind.Gathering, location, tile, member, manual);
    }

    private bool TryLumberTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe(member.OwnerId) || location is null)
            return false;

        if (this.pendingTasks.ContainsKey(member.NpcName)
            || this.activeRecallTargets.ContainsKey(member.NpcName)
            || member.Mode != CompanionMode.Following)
        {
            return false;
        }

        if (this.config.LumberingMode == TaskMode.Disabled)
            return false;

        Vector2 targetTile = new((int)tile.X, (int)tile.Y);
        if (!location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not Tree
            || !this.IsValidWoodTarget(location, targetTile))
            return false;

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.currentLocation != location)
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_unavailable");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (owner.CurrentTool is not Axe)
        {
            this.SetTaskFailure(member, "companion.task_failure.need_axe");
            if (manual)
                this.Warn("tasks.need_axe");

            return false;
        }

        NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.SetTaskFailure(member, "companion.task_failure.no_valid_target");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!IsWithinCompanionDistance(owner.Tile, npc.Tile))
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            this.SetCompanionActivity(member, "companion.status.returning");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, MaxCompanionDistanceTiles, out Vector2 standTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!this.TryReserveWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_reserved");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!this.TryReserveStandTile(member.NpcName, location.NameOrUniqueName, standTile))
        {
            this.ReleaseWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile);
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            if (manual)
                this.Warn("tasks.no_valid_target");
            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.Lumbering,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            Manual = manual,
            RequiresPlayerTool = true,
            WorkRadius = MaxCompanionDistanceTiles,
            ReturnDistance = MaxCompanionDistanceTiles,
            StartedTick = Game1.ticks,
            LastProcessedTick = Game1.ticks,
            StandTile = standTile
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.working");
        this.SetCompanionTarget(member, CompanionTaskKind.Lumbering, targetTile);
        this.ShowCompanionWorkSignal(npc, location, targetTile, "target");
        this.Say(npc, "Lumbering", force: false);

        return true;
    }

    private bool TryMiningTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe(member.OwnerId) || this.config.MiningMode == TaskMode.Disabled)
            return false;

        if (this.pendingTasks.ContainsKey(member.NpcName)
            || this.activeRecallTargets.ContainsKey(member.NpcName)
            || member.Mode != CompanionMode.Following)
        {
            return false;
        }

        Vector2 targetTile = NormalizeTile(tile);
        if (!location.Objects.TryGetValue(targetTile, out SObject? obj) || !this.IsSafeMineableObject(obj))
            return false;

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null || npc.currentLocation != location || owner.currentLocation != location)
            return false;

        if (owner.CurrentTool is not Pickaxe)
        {
            this.SetTaskFailure(member, "companion.task_failure.need_pickaxe");
            if (manual)
                this.Warn("tasks.need_pickaxe");

            return false;
        }

        if (!IsWithinCompanionDistance(owner.Tile, npc.Tile))
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            this.SetCompanionActivity(member, "companion.status.returning");
            return false;
        }

        bool queued = this.TryQueueDirectiveMiningTask(location, targetTile, member, npc, MaxCompanionDistanceTiles);
        if (!queued || !this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task))
            return false;

        task.Manual = manual;
        task.UsesWorkDirective = false;
        task.RequiresPlayerTool = true;
        task.WorkRadius = MaxCompanionDistanceTiles;
        task.ReturnDistance = MaxCompanionDistanceTiles;
        return true;
    }

    private int GetLumberHitCooldown(SquadMemberState member)
    {
        int cooldown = LumberHitCooldownTicks;
        if (this.HasSkill(member, "SKILL-LUMBER-002"))
            cooldown = (int)MathF.Round(cooldown * 0.9f);

        return Math.Max(20, cooldown);
    }

    private int GetMiningHitCooldown(SquadMemberState member)
    {
        int cooldown = MiningHitCooldownTicks;
        if (this.HasSkill(member, "SKILL-MINING-002"))
            cooldown = (int)MathF.Round(cooldown * 0.9f);

        return Math.Max(20, cooldown);
    }

    private int GetCompanionWorkRadius(SquadMemberState member)
    {
        int radius = this.GetConfiguredWorkRadius();
        if (this.HasSkill(member, "SKILL-UTILITY-002"))
            radius++;

        return Math.Max(MaxCompanionDistanceTiles, radius);
    }

    private int GetCompanionReturnDistance(SquadMemberState member)
    {
        int distance = this.config.CompanionWorkReturnDistance;
        if (this.HasSkill(member, "SKILL-UTILITY-003"))
            distance = Math.Max(this.config.CompanionWorkRadius, distance - 1);

        return Math.Max(MaxCompanionDistanceTiles, distance);
    }

    private void FaceTile(NPC npc, Vector2 tile)
    {
        Vector2 delta = tile - npc.Tile;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
            npc.faceDirection(delta.X > 0 ? 1 : 3);
        else
            npc.faceDirection(delta.Y > 0 ? 2 : 0);
    }

    private void ToggleTasks(Farmer player)
    {
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "SetTasksEnabled",
                desiredEnabled: !this.AreTasksEnabled(player.UniqueMultiplayerID));
            return;
        }

        this.ToggleTasks(player.UniqueMultiplayerID);
    }

    private void ToggleTasks(long ownerId)
    {
        this.SetTasksEnabled(ownerId, !this.AreTasksEnabled(ownerId));
    }

    private void SetTasksEnabled(long ownerId, bool enabled)
    {
        if (this.AreTasksEnabled(ownerId) == enabled)
            return;

        this.taskToggles[ownerId] = enabled;
        if (!enabled)
        {
            this.CancelPendingTasksForOwner(
                ownerId,
                "companion.task_failure.tasks_disabled",
                includeMovementOrders: false);
            foreach (SquadMemberState member in this.members.Values.Where(member => member.OwnerId == ownerId))
            {
                this.UpdateTargetPreview(
                    member,
                    new TargetPreview(false, "", -1, -1, "companion.preview.tasks_disabled"));
                if (member.Mode == CompanionMode.Following && this.HasActiveWorkArea(member))
                {
                    this.SetCompanionActivity(member, "companion.status.work_area_paused");
                    this.SetTaskFailure(member, "companion.task_failure.tasks_disabled");
                }
            }
        }
        else
        {
            foreach (SquadMemberState member in this.members.Values.Where(member => member.OwnerId == ownerId))
            {
                bool follows = member.Mode == CompanionMode.Following;
                bool hasDirective = follows && this.HasActiveWorkDirective(member);
                bool workAreaRecoveryPending = follows
                    && this.HasActiveWorkArea(member)
                    && this.HasPendingWorkAreaRecovery(member);
                if (member.LastFailureReasonKey == "companion.task_failure.tasks_disabled")
                    this.SetTaskFailure(member, "");
                this.UpdateTargetPreview(
                    member,
                    new TargetPreview(
                        false,
                        "",
                        -1,
                        -1,
                        !follows
                            ? "companion.preview.not_following"
                            : workAreaRecoveryPending
                                ? "companion.task_failure.work_area_unavailable"
                                : hasDirective
                                    ? "companion.preview.planning"
                                    : "companion.preview.inactive"));
                if (follows && this.HasActiveWorkArea(member))
                {
                    this.SetCompanionActivity(
                        member,
                        workAreaRecoveryPending
                            ? "companion.status.work_area_paused"
                            : "companion.status.work_area");
                    if (workAreaRecoveryPending)
                        this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
                    this.priorityTaskPlanningMembers.Add(member.NpcName);
                }
            }

            int deferredScanTick = Game1.ticks + 1;
            this.nextTaskScanTick = this.nextTaskScanTick <= Game1.ticks
                ? deferredScanTick
                : Math.Min(this.nextTaskScanTick, deferredScanTick);
        }

        this.InvalidateTargetPreviews();
        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
            this.Info(enabled ? "tasks.enabled" : "tasks.disabled");
    }

    private bool AreTasksEnabled(long playerId)
    {
        return !this.taskToggles.TryGetValue(playerId, out bool enabled) || enabled;
    }
}
