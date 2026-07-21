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
    private void ProcessPendingTasks()
    {
        this.taskPathStartsRemaining = 0;
        if (this.IsGlobalSimulationBlocked())
        {
            foreach (PendingCompanionTask pausedTask in this.pendingTasks.Values)
                pausedTask.LastProcessedTick = Game1.ticks;
            return;
        }

        this.taskPathStartsRemaining = TaskPathStartBudgetPerUpdate;
        try
        {
            foreach (PendingCompanionTask task in this.pendingTasks.Values
                .OrderByDescending(task => task.Manual)
                .ThenBy(task => task.StartedTick)
                .ToList())
            {
                if (!this.pendingTasks.TryGetValue(task.NpcName, out PendingCompanionTask? currentTask)
                    || !ReferenceEquals(currentTask, task))
                {
                    continue;
                }

                try
                {
                    if (this.members.TryGetValue(task.NpcName, out SquadMemberState? taskMember)
                        && this.IsOwnerSimulationBlocked(taskMember.OwnerId, blockForMenu: true))
                    {
                        task.LastProcessedTick = Game1.ticks;
                        NPC? pausedNpc = this.GetNpcByName(task.NpcName);
                        if (pausedNpc is not null && this.IsOwnedCompanionController(pausedNpc))
                        {
                            this.StopCompanionMovement(pausedNpc);
                            // This controller was paused intentionally by game
                            // state, not rejected by pathfinding. Resume from the
                            // same stand after the menu/event closes.
                            task.HasPathStartAttempted = false;
                            task.HasLastProgressPosition = false;
                            task.NoProgressTicks = 0;
                            task.LastPathTick = 0;
                        }
                        continue;
                    }

                    int lastProcessedTick = task.LastProcessedTick <= 0 ? Game1.ticks : task.LastProcessedTick;
                    // This is an inactivity budget, not an absolute task duration.
                    // Routing progress and successful hits reset it below.
                    task.InactiveTicks += Math.Clamp(Game1.ticks - lastProcessedTick, 0, 30);
                    task.LastProcessedTick = Game1.ticks;
                    this.ProcessPendingTask(task);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log(
                        $"Companion task '{task.Kind}' for '{task.NpcName}' failed unexpectedly at {task.LocationName} ({task.TargetTile.X}, {task.TargetTile.Y}). The task was cancelled safely. {ex}",
                        LogLevel.Error);
                    if (task.Kind == CompanionTaskKind.MovingToWait
                        && this.members.TryGetValue(task.NpcName, out SquadMemberState? movementMember))
                    {
                        this.FailMoveCompanionToWait(
                            task,
                            movementMember,
                            this.GetNpcByName(task.NpcName),
                            "companion.task_failure.unexpected_error");
                    }
                    else
                    {
                        this.RemovePendingTask(task, "companion.task_failure.unexpected_error", returning: true);
                    }
                }
            }
        }
        finally
        {
            this.taskPathStartsRemaining = 0;
        }
    }

    private void ProcessPendingTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member)
            || (!task.IgnoresTaskToggle && !this.AreTasksEnabled(member.OwnerId)))
        {
            this.RemovePendingTask(task, "companion.task_failure.tasks_disabled", returning: true);
            return;
        }

        if (task.UsesFixedWorkArea
            && (!this.HasActiveWorkArea(member)
                || !string.Equals(member.WorkAreaOrderId, task.FixedWorkAreaOrderId, StringComparison.Ordinal)
                || !this.IsTaskInsideWorkArea(member, task.Kind, task.LocationName, task.TargetTile)))
        {
            this.RemovePendingTask(task, "companion.task_failure.directive_disabled");
            return;
        }

        bool taskModeDisabled = task.Kind switch
        {
            CompanionTaskKind.Lumbering => this.config.LumberingMode == TaskMode.Disabled,
            CompanionTaskKind.Mining => this.config.MiningMode == TaskMode.Disabled,
            CompanionTaskKind.Watering => this.config.WateringMode == TaskMode.Disabled,
            CompanionTaskKind.Gathering => !this.config.EnableGathering || this.config.ForagingMode == TaskMode.Disabled,
            CompanionTaskKind.Harvesting => this.config.HarvestingMode == TaskMode.Disabled,
            CompanionTaskKind.Petting => this.config.PettingMode == TaskMode.Disabled,
            _ => false
        };
        if (taskModeDisabled && !task.IgnoresTaskMode)
        {
            this.RemovePendingTask(task, "companion.task_failure.tasks_disabled", returning: true);
            return;
        }

        switch (task.Kind)
        {
            case CompanionTaskKind.MovingToWait:
                this.ProcessPendingMoveToWaitTask(task);
                break;

            case CompanionTaskKind.Lumbering:
                this.ProcessPendingLumberTask(task);
                break;

            case CompanionTaskKind.Mining:
                this.ProcessPendingMiningTask(task);
                break;

            case CompanionTaskKind.Watering:
            case CompanionTaskKind.Gathering:
            case CompanionTaskKind.Harvesting:
            case CompanionTaskKind.Petting:
                this.ProcessPendingInstantTask(task);
                break;
        }
    }

    private void ProcessPendingInstantTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member) || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (task.InactiveTicks > InstantTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout", returning: true);
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.no_valid_target");

            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner?.currentLocation is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        GameLocation location = task.UsesFixedWorkArea
            ? Game1.getLocationFromName(task.LocationName) ?? owner.currentLocation
            : owner.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        if (!this.IsExpectedContextTargetCurrent(task, location))
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost", returning: true);
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.no_valid_target");
            return;
        }

        NPC? npc = this.GetNpcByName(task.NpcName);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost", returning: true);
            return;
        }

        this.DisableNpcSchedule(npc, stopCurrentRoute: false);

        Vector2 targetTile = NormalizeTile(task.TargetTile);
        HoeDirt? dirt = null;
        SObject? forage = null;
        Crop? crop = null;
        FarmAnimal? animal = null;

        switch (task.Kind)
        {
            case CompanionTaskKind.Watering:
                dirt = location.GetHoeDirtAtTile(targetTile);
                if (dirt?.needsWatering() != true)
                {
                    this.RemovePendingTask(task, "companion.task_failure.target_lost");
                    return;
                }

                break;

            case CompanionTaskKind.Gathering:
                if (!location.Objects.TryGetValue(targetTile, out forage) || !forage.IsSpawnedObject)
                {
                    this.RemovePendingTask(task, "companion.task_failure.target_lost");
                    return;
                }

                break;

            case CompanionTaskKind.Harvesting:
                dirt = location.GetHoeDirtAtTile(targetTile);
                crop = dirt?.crop;
                if (dirt is null
                    || crop is null
                    || crop.dead.Value
                    || !dirt.readyForHarvest()
                    || crop.GetHarvestMethod() != HarvestMethod.Grab)
                {
                    this.RemovePendingTask(task, "companion.task_failure.target_lost");
                    return;
                }

                if (this.IsProtectedBeeHouseFlower(location, targetTile, crop))
                {
                    this.RemovePendingTask(task, "companion.task_failure.bee_flower_protected", returning: true);
                    if (task.Manual)
                        this.WarnForPlayer(member.OwnerId, "tasks.bee_flower_protected");

                    return;
                }

                break;

            case CompanionTaskKind.Petting:
                animal = location.Animals.Values.FirstOrDefault(candidate => candidate.myID.Value == task.TargetEntityId);
                if (animal is null || animal.wasPet.Value || animal.currentLocation != location)
                {
                    this.RemovePendingTask(task, "companion.task_failure.target_lost");
                    return;
                }

                targetTile = NormalizeTile(animal.Tile);
                this.SetCompanionTarget(member, CompanionTaskKind.Petting, targetTile);
                break;

            default:
                this.RemovePendingTask(task, "companion.task_failure.target_lost");
                return;
        }

        if (!task.UsesFixedWorkArea && !IsWithinCompanionDistance(owner.Tile, targetTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        if (!task.UsesFixedWorkArea
            && ownerDistance > Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance))
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            return;
        }

        int standRadius = task.UsesFixedWorkArea
            ? member.WorkAreaRadius + 1
            : MaxCompanionDistanceTiles;
        if (!this.TryResolveTaskStandTile(location, targetTile, npc, member, task, standRadius, out Vector2 standTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.no_safe_tile", returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        this.StopCompanionMovement(npc);
        this.FaceTile(npc, targetTile);
        if (this.ShouldWaitForCompanionWorkAnimation(task, npc, targetTile))
            return;
        task.AwaitingWorkAnimation = false;

        switch (task.Kind)
        {
            case CompanionTaskKind.Watering:
                dirt!.state.Value = HoeDirt.watered;
                this.ShowCompanionWorkSignal(npc, location, targetTile, "water");
                this.AddCompanionXp(member, 1);
                this.SetTaskResult(member, "companion.task_result.watered");
                this.QueueTaskSuccess(
                    npc,
                    member,
                    "Watering",
                    CompanionTaskKind.Watering,
                    "companion.task_result.watered",
                    task.Manual);
                if (task.Manual)
                {
                    this.InfoForPlayer(member.OwnerId, "tasks.watered", new { npc = member.DisplayName });
                }

                break;

            case CompanionTaskKind.Gathering:
                Item item = forage!.getOne();
                item.Stack = Math.Max(1, forage.Stack);
                if (!location.Objects.Remove(targetTile))
                {
                    this.RemovePendingTask(task, "companion.task_failure.target_lost");
                    return;
                }

                this.InvalidateReachabilityForLocation(location);
                this.RouteItemToOwnerInventoryOrDrop(owner, item, location, targetTile);
                this.RecordCompanionLoot(member, item, "companion.loot_source.forage");
                location.OnHarvestedForage(owner, forage);
                this.ShowCompanionWorkSignal(npc, location, targetTile, "forage");
                this.AddCompanionXp(member, 2);
                this.SetTaskResult(member, "companion.task_result.gathered");
                this.QueueTaskSuccess(
                    npc,
                    member,
                    "Foraging",
                    CompanionTaskKind.Gathering,
                    "companion.task_result.gathered",
                    task.Manual,
                    item);
                if (task.Manual)
                {
                    this.InfoForPlayer(member.OwnerId, "tasks.gathered", new { npc = member.DisplayName, item = item.DisplayName });
                }

                break;

            case CompanionTaskKind.Harvesting:
                if (member.OwnerId != Game1.player.UniqueMultiplayerID && Game1.getOnlineFarmers().Count() > 1)
                {
                    this.RemovePendingTask(task, "companion.task_failure.remote_harvest_unsupported", returning: true);
                    return;
                }

                bool removeCrop = crop!.harvest((int)targetTile.X, (int)targetTile.Y, dirt!);
                if (removeCrop)
                    dirt!.crop = null;

                if (!removeCrop && dirt!.readyForHarvest())
                {
                    this.RemovePendingTask(task, "companion.task_failure.target_lost");
                    return;
                }

                this.ShowCompanionWorkSignal(npc, location, targetTile, "harvest");
                this.AddCompanionXp(member, 3);
                this.SetTaskResult(member, "companion.task_result.harvested");
                this.InvalidateTargetPreviews();
                this.QueueTaskSuccess(
                    npc,
                    member,
                    "Harvesting",
                    CompanionTaskKind.Harvesting,
                    "companion.task_result.harvested",
                    task.Manual);
                if (task.Manual)
                {
                    this.InfoForPlayer(member.OwnerId, "tasks.harvested", new { npc = member.DisplayName });
                }

                break;

            case CompanionTaskKind.Petting:
                animal!.pet(owner);
                this.ShowCompanionWorkSignal(npc, location, targetTile, "pet");
                this.AddCompanionXp(member, 2);
                this.SetTaskResult(member, "companion.task_result.petted");
                this.QueueTaskSuccess(
                    npc,
                    member,
                    "Petting",
                    CompanionTaskKind.Petting,
                    "companion.task_result.petted",
                    task.Manual);
                if (task.Manual)
                {
                    this.InfoForPlayer(member.OwnerId, "tasks.petted", new { npc = member.DisplayName, animal = animal.displayName });
                }

                break;
        }

        this.ShowCompanionWorkSuccessAnimation(npc, task.Kind, targetTile);
        this.CompleteSharedTargetPeers(task);
        this.RemovePendingTask(task);
    }

    private void RouteItemToOwnerInventoryOrDrop(Farmer owner, Item item, GameLocation location, Vector2 tile)
    {
        Item? notAdded = this.config.UseSquadInventory
            ? this.AddToSquadInventorySafely(item, "forage gathering")
            : this.AddToFarmerInventorySafely(owner, item, "forage gathering");

        if (notAdded is not null)
            this.DropItemSafely(notAdded, location, tile, owner.FacingDirection, "forage gathering");
    }

    private void ProcessPendingLumberTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member) || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (task.InactiveTicks > LumberTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout");
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.no_valid_target");

            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner?.currentLocation is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        GameLocation location = task.UsesFixedWorkArea
            ? Game1.getLocationFromName(task.LocationName) ?? owner.currentLocation
            : owner.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        if (!this.IsExpectedContextTargetCurrent(task, location))
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost", returning: true);
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.no_valid_target");
            return;
        }

        NPC? npc = Game1.getCharacterFromName(task.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        this.DisableNpcSchedule(npc, stopCurrentRoute: false);

        if (!location.terrainFeatures.TryGetValue(task.TargetTile, out TerrainFeature? feature) || feature is not Tree tree)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        if (tree.tapped.Value)
        {
            this.RemovePendingTask(task, "companion.task_failure.protected_target", returning: true);
            return;
        }

        // Let the vanilla falling animation finish before chopping the stump.
        // Otherwise the tree can reach its terminal health while still falling
        // and the task would wait forever for a completion signal.
        if (tree.falling.Value)
            return;

        Axe? axe = owner.CurrentTool as Axe;
        if (task.RequiresPlayerTool && axe is null)
        {
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.need_axe");

            this.RemovePendingTask(task, "companion.task_failure.need_axe");
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        int returnDistance = Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance);
        if (!task.UsesFixedWorkArea && ownerDistance > returnDistance)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            return;
        }

        ResetCompanionMovementSpeed(npc);

        int workRadius = task.UsesFixedWorkArea
            ? member.WorkAreaRadius + 1
            : Math.Max(MaxCompanionDistanceTiles, task.WorkRadius);
        if (!this.TryResolveTaskStandTile(location, task.TargetTile, npc, member, task, workRadius, out Vector2 standTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.no_safe_tile", returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        if (member.CurrentActivityKey == "companion.status.stuck")
            this.SetCompanionActivity(member, "companion.status.working");

        int lumberCooldown = this.GetLumberHitCooldown(member);
        int lumberElapsed = Math.Max(0, Game1.ticks - task.LastActionTick);
        int lumberAnimationLead = WorkAnimationSwingTicks + WorkAnimationProcessingSlackTicks;
        if (!task.AwaitingWorkAnimation
            && lumberElapsed < Math.Max(0, lumberCooldown - lumberAnimationLead))
            return;

        if (this.ShouldWaitForCompanionWorkAnimation(task, npc, task.TargetTile))
            return;
        if (lumberElapsed < lumberCooldown)
            return;
        task.AwaitingWorkAnimation = false;

        bool finished = this.PerformLumberHit(location, tree, task.TargetTile, member, npc, axe, task.Manual);
        task.InactiveTicks = 0;
        task.LastActionTick = Game1.ticks;
        if (finished)
        {
            this.CompleteSharedTargetPeers(task);
            this.RemovePendingTask(task);
        }
    }

    private void ProcessPendingMiningTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member) || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (task.InactiveTicks > MiningTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout");
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner?.currentLocation is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        GameLocation location = task.UsesFixedWorkArea
            ? Game1.getLocationFromName(task.LocationName) ?? owner.currentLocation
            : owner.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        if (!this.IsExpectedContextTargetCurrent(task, location))
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost", returning: true);
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.no_valid_target");
            return;
        }

        NPC? npc = Game1.getCharacterFromName(task.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        this.DisableNpcSchedule(npc, stopCurrentRoute: false);

        if (!location.Objects.TryGetValue(task.TargetTile, out SObject? obj) || !this.IsSafeMineableObject(obj))
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        Pickaxe? pickaxe = owner.CurrentTool as Pickaxe;
        if (task.RequiresPlayerTool && pickaxe is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.need_pickaxe");
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        int returnDistance = Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance);
        if (!task.UsesFixedWorkArea && ownerDistance > returnDistance)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            return;
        }

        ResetCompanionMovementSpeed(npc);

        int workRadius = task.UsesFixedWorkArea
            ? member.WorkAreaRadius + 1
            : Math.Max(MaxCompanionDistanceTiles, task.WorkRadius);
        if (!this.TryResolveTaskStandTile(location, task.TargetTile, npc, member, task, workRadius, out Vector2 standTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.no_safe_tile", returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        if (member.CurrentActivityKey == "companion.status.stuck")
            this.SetCompanionActivity(member, "companion.status.working");

        int miningCooldown = this.GetMiningHitCooldown(member);
        int miningElapsed = Math.Max(0, Game1.ticks - task.LastActionTick);
        int miningAnimationLead = WorkAnimationSwingTicks + WorkAnimationProcessingSlackTicks;
        if (!task.AwaitingWorkAnimation
            && miningElapsed < Math.Max(0, miningCooldown - miningAnimationLead))
            return;

        if (this.ShouldWaitForCompanionWorkAnimation(task, npc, task.TargetTile))
            return;
        if (miningElapsed < miningCooldown)
            return;
        task.AwaitingWorkAnimation = false;

        bool finished = this.PerformMiningHit(location, obj, task.TargetTile, member, npc, task, pickaxe);
        task.InactiveTicks = 0;
        task.LastActionTick = Game1.ticks;
        if (finished)
        {
            this.CompleteSharedTargetPeers(task);
            this.RemovePendingTask(task);
        }
    }

    private bool TryResolveTaskStandTile(
        GameLocation location,
        Vector2 targetTile,
        NPC npc,
        SquadMemberState member,
        PendingCompanionTask task,
        int maxOwnerDistance,
        out Vector2 standTile)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        Vector2 currentStandTile = NormalizeTile(task.StandTile);
        targetTile = NormalizeTile(targetTile);
        Vector2? anchorOverride = task.UsesFixedWorkArea
            ? NormalizeTile(task.FixedWorkAreaCenter)
            : null;
        bool rejectedByMissingController = false;
        if (anchorOverride.HasValue || owner is not null && owner.currentLocation == location)
        {
            Vector2 anchorTile = anchorOverride ?? NormalizeTile(owner!.Tile);
            bool isNpcTile = currentStandTile == npcTile;
            // Keep an already-reserved destination when a farmer/animal only
            // occupies it temporarily; dynamic occupancy isn't a structural
            // reason to churn the task route.
            bool tileAvailable = isNpcTile || this.IsTileTraversable(location, currentStandTile);
            bool adjacent = Vector2.Distance(currentStandTile, targetTile) <= TaskArrivalDistance;
            bool withinOwnerRange = IsWithinOwnerDistance(anchorTile, currentStandTile, maxOwnerDistance);
            bool reservedByAnotherCompanion = this.IsStandTileReserved(
                location,
                currentStandTile,
                member.NpcName);
            bool hasExpectedController = this.HasCompanionController(
                npc,
                CompanionMovementIntent.Task,
                location,
                currentStandTile);
            rejectedByMissingController = tileAvailable
                && adjacent
                && withinOwnerRange
                && !reservedByAnotherCompanion
                && !isNpcTile
                && task.HasPathStartAttempted
                && !hasExpectedController;
            if (TaskNavigationPolicy.CanReuseStandTile(
                tileAvailable,
                adjacent,
                withinOwnerRange,
                reservedByAnotherCompanion,
                isNpcTile,
                hasExpectedController,
                task.HasPathStartAttempted))
            {
                standTile = currentStandTile;
                return true;
            }
        }

        this.ReleaseStandTile(task.NpcName, task.LocationName, task.StandTile);
        if (rejectedByMissingController)
            task.RejectedStandTiles.Add(currentStandTile);
        if (this.TryFindSafeAdjacentTile(
                location,
                targetTile,
                npc,
                member,
                maxOwnerDistance,
                out standTile,
                task.RejectedStandTiles,
                anchorOverride)
            && this.TryReserveStandTile(task.NpcName, task.LocationName, standTile))
        {
            task.StandTile = standTile;
            task.HasPathStartAttempted = false;
            task.HasLastProgressPosition = false;
            task.NoProgressTicks = 0;
            task.LastPathTick = 0;
            return true;
        }

        standTile = npcTile;
        return false;
    }

    private bool TryFindSafeAdjacentTile(
        GameLocation location,
        Vector2 targetTile,
        NPC npc,
        SquadMemberState member,
        int maxOwnerDistance,
        out Vector2 standTile,
        IReadOnlySet<Vector2>? excludedStandTiles = null,
        Vector2? ownerAnchorOverride = null)
    {
        Vector2 npcTile = NormalizeTile(npc.Tile);
        HashSet<Vector2> candidates = this.GetCandidateTaskStandTiles(
                location,
                targetTile,
                npc,
                member,
                maxOwnerDistance,
                excludedStandTiles,
                ownerAnchorOverride)
            .ToHashSet();
        if (candidates.Count == 0)
        {
            standTile = npcTile;
            return false;
        }

        ReachabilitySearchResult result = this.SearchReachableGoal(
            location,
            npcTile,
            candidates,
            candidates,
            MaxFollowReachabilitySearchTiles,
            out Vector2 reachedStandTile,
            out _);
        if (result == ReachabilitySearchResult.Reachable)
        {
            standTile = reachedStandTile;
            return true;
        }

        standTile = npcTile;
        return false;
    }

    private IReadOnlyList<Vector2> GetCandidateTaskStandTiles(
        GameLocation location,
        Vector2 targetTile,
        NPC npc,
        SquadMemberState member,
        int maxOwnerDistance,
        IReadOnlySet<Vector2>? excludedStandTiles = null,
        Vector2? ownerAnchorOverride = null)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        if (!ownerAnchorOverride.HasValue && (owner is null || owner.currentLocation != location))
            return Array.Empty<Vector2>();

        targetTile = NormalizeTile(targetTile);
        Vector2 ownerTile = NormalizeTile(ownerAnchorOverride ?? owner!.Tile);
        List<Vector2> candidates = new();
        if (Vector2.Distance(npcTile, targetTile) <= TaskArrivalDistance
            && IsWithinOwnerDistance(ownerTile, npcTile, maxOwnerDistance)
            && excludedStandTiles?.Contains(npcTile) != true
            && !this.IsStandTileReserved(location, npcTile, member.NpcName))
        {
            candidates.Add(npcTile);
        }

        Vector2[] offsets =
        {
            new(0, 1),
            new(1, 0),
            new(-1, 0),
            new(0, -1)
        };
        foreach (Vector2 candidate in offsets
            .Select(offset => NormalizeTile(targetTile + offset))
            .Where(candidate => !candidates.Contains(candidate))
            .Where(candidate => excludedStandTiles?.Contains(candidate) != true)
            .Where(candidate => IsWithinOwnerDistance(ownerTile, candidate, maxOwnerDistance))
            .Where(candidate => !this.IsStandTileReserved(location, candidate, member.NpcName))
            .Where(candidate => this.IsTileSafe(location, candidate)))
        {
            candidates.Add(candidate);
        }

        return candidates;
    }

    private bool IsNpcAtTaskTile(NPC npc, Vector2 standTile)
    {
        return NormalizeTile(npc.Tile) == NormalizeTile(standTile);
    }

    private void RouteNpcToTaskTile(NPC npc, GameLocation location, Vector2 standTile, PendingCompanionTask task, bool force)
    {
        int retryTicks = 30;
        this.members.TryGetValue(task.NpcName, out SquadMemberState? member);
        if (member is not null && this.HasSkill(member, "SKILL-UTILITY-001"))
            retryTicks = 24;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        standTile = NormalizeTile(standTile);
        Vector2 positionTile = npc.Position / 64f;
        // Straight-line distance can increase on a perfectly valid route around
        // a fence or building. Real position movement is the reliable signal
        // that the controller is still making progress.
        bool madeProgress = !task.HasLastProgressPosition
            || Vector2.Distance(positionTile, task.LastProgressPosition) > FollowPositionProgressTolerance;
        if (madeProgress)
            task.InactiveTicks = 0;

        if (!force)
        {
            if (!madeProgress)
                task.NoProgressTicks++;
            else
                task.NoProgressTicks = 0;

            if (task.NoProgressTicks >= TaskNoProgressUpdatesThreshold)
            {
                if (member is not null && task.NoProgressTicks == TaskNoProgressUpdatesThreshold)
                {
                    this.SetCompanionActivity(member, "companion.status.stuck");
                    this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                    this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
                }

                if (task.Kind != CompanionTaskKind.MovingToWait)
                {
                    // Drop the stalled controller. TryResolveTaskStandTile will
                    // exclude this rejected stand and select another side next pass.
                    this.StopCompanionMovement(npc);
                    task.LastPathTick = Game1.ticks;
                    return;
                }

                // A ground order has one exact destination rather than several
                // adjacent stands, so recovery retries that destination slowly.
                this.StopCompanionMovement(npc);
                task.NoProgressTicks = 0;
                task.LastPathTick = 0;
                force = true;
            }
        }
        else
        {
            task.NoProgressTicks = 0;
        }

        task.LastProgressPosition = positionTile;
        task.HasLastProgressPosition = true;

        bool hasExpectedController = this.HasCompanionController(
            npc,
            CompanionMovementIntent.Task,
            location,
            standTile);
        bool retryCooldownElapsed = !task.HasPathStartAttempted
            || task.NoProgressTicks == 0
            || unchecked((uint)(Game1.ticks - task.LastPathTick)) >= (uint)retryTicks;
        if (!TaskNavigationPolicy.ShouldStartPath(
            npcTile == standTile,
            force,
            hasExpectedController,
            retryCooldownElapsed,
            this.taskPathStartsRemaining > 0))
            return;

        this.taskPathStartsRemaining--;
        task.LastPathTick = Game1.ticks;
        task.HasPathStartAttempted = true;
        if (force)
            task.NoProgressTicks = 0;
        if (member is not null && member.CurrentActivityKey == "companion.status.stuck")
        {
            this.SetCompanionActivity(
                member,
                task.Kind == CompanionTaskKind.MovingToWait
                    ? "companion.status.moving_to_wait"
                    : "companion.status.working");
        }
        if (member?.LastFailureReasonKey == "companion.task_failure.path_recovery")
            this.SetTaskFailure(member, "");

        ResetCompanionMovementSpeed(npc);
        try
        {
            if (!this.TryStartCompanionPath(npc, location, standTile, CompanionMovementIntent.Task)
                && member is not null)
            {
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
            }
        }
        catch (Exception ex)
        {
            this.StopCompanionMovement(npc);
            this.Monitor.Log($"Could not route '{task.NpcName}' to task tile {standTile}: {ex.Message}", LogLevel.Warn);
            if (task.Kind == CompanionTaskKind.MovingToWait && member is not null)
            {
                this.FailMoveCompanionToWait(
                    task,
                    member,
                    npc,
                    "companion.task_failure.unexpected_error");
            }
            else
            {
                this.RemovePendingTask(task, "companion.task_failure.unexpected_error", returning: true);
            }
        }
    }

    private bool PerformLumberHit(GameLocation location, Tree tree, Vector2 tile, SquadMemberState member, NPC npc, Axe? axe, bool manual)
    {
        this.StopCompanionMovement(npc);
        this.FaceTile(npc, tile);
        tree.shake(tile, doEvenIfStillShaking: true);
        this.ShowCompanionWorkSignal(npc, location, tile, "wood");
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null)
            return false;

        Axe effectiveAxe = axe?.getOne() as Axe ?? new Axe();
        if (this.HasSkill(member, "SKILL-LUMBER-001"))
        {
            effectiveAxe.UpgradeLevel = Math.Min(Tool.iridium, effectiveAxe.UpgradeLevel + 1);
        }

        effectiveAxe.lastUser = owner;
        bool removeFeature = tree.performToolAction(effectiveAxe, 0, tile);
        this.AddCompanionXp(member, 1);

        if (removeFeature)
        {
            location.terrainFeatures.Remove(tile);
            this.InvalidateReachabilityForLocation(location);
            if (this.HasSkill(member, "SKILL-LUMBER-003") && Game1.random.NextDouble() < 0.35)
            {
                this.RouteTaskRewardOrDrop(
                    member,
                    ItemRegistry.Create("(O)388"),
                    location,
                    tile,
                    "companion.loot_source.wood");
            }

            this.AddCompanionXp(member, 8);
            this.SetTaskResult(member, "companion.task_result.lumbered");
            this.QueueTaskSuccess(
                npc,
                member,
                "Lumbering",
                CompanionTaskKind.Lumbering,
                "companion.task_result.lumbered",
                manual);
            this.ShowCompanionWorkSuccessAnimation(npc, CompanionTaskKind.Lumbering, tile);

            if (manual)
                this.InfoForPlayer(member.OwnerId, "tasks.lumbered", new { npc = member.DisplayName });

            return true;
        }

        return false;
    }

    private bool PerformMiningHit(GameLocation location, SObject obj, Vector2 tile, SquadMemberState member, NPC npc, PendingCompanionTask task, Pickaxe? pickaxe)
    {
        this.StopCompanionMovement(npc);
        this.FaceTile(npc, tile);
        this.ShowCompanionWorkSignal(npc, location, tile, "mining");
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null)
            return false;

        Pickaxe effectivePickaxe = pickaxe?.getOne() as Pickaxe ?? new Pickaxe();
        if (this.HasSkill(member, "SKILL-MINING-001"))
        {
            effectivePickaxe.UpgradeLevel = Math.Min(Tool.iridium, effectivePickaxe.UpgradeLevel + 1);
        }

        effectivePickaxe.lastUser = owner;
        bool destroyed = obj.performToolAction(effectivePickaxe);
        this.AddCompanionXp(member, 1);
        if (!destroyed)
            return false;

        location.Objects.Remove(tile);
        this.InvalidateReachabilityForLocation(location);
        location.OnStoneDestroyed(obj.ItemId, (int)tile.X, (int)tile.Y, owner);

        if (this.HasSkill(member, "SKILL-MINING-003") && Game1.random.NextDouble() < 0.25)
        {
            string? bonusOreId = this.GetOreRewardId(this.GetObjectSearchText(obj));
            if (bonusOreId is not null)
            {
                this.RouteTaskRewardOrDrop(
                    member,
                    ItemRegistry.Create(bonusOreId),
                    location,
                    tile,
                    "companion.loot_source.mining");
            }
        }

        this.AddCompanionXp(member, 6);
        this.SetTaskResult(member, "companion.task_result.mined");
        this.QueueTaskSuccess(
            npc,
            member,
            "Mining",
            CompanionTaskKind.Mining,
            "companion.task_result.mined",
            task.Manual);
        this.ShowCompanionWorkSuccessAnimation(npc, CompanionTaskKind.Mining, tile);
        return true;
    }

    private string GetObjectSearchText(SObject obj)
    {
        return string.Join(
            " ",
            obj.Name,
            obj.DisplayName,
            obj.ItemId,
            obj.QualifiedItemId,
            obj.ParentSheetIndex.ToString());
    }

    private string? GetOreRewardId(string text)
    {
        if (text.Contains("radioactive", StringComparison.OrdinalIgnoreCase) || text.Contains("909", StringComparison.OrdinalIgnoreCase))
            return "(O)909";

        if (text.Contains("iridium", StringComparison.OrdinalIgnoreCase) || text.Contains("386", StringComparison.OrdinalIgnoreCase))
            return "(O)386";

        if (text.Contains("gold", StringComparison.OrdinalIgnoreCase) || text.Contains("384", StringComparison.OrdinalIgnoreCase))
            return "(O)384";

        if (text.Contains("iron", StringComparison.OrdinalIgnoreCase) || text.Contains("380", StringComparison.OrdinalIgnoreCase))
            return "(O)380";

        if (text.Contains("copper", StringComparison.OrdinalIgnoreCase) || text.Contains("378", StringComparison.OrdinalIgnoreCase))
            return "(O)378";

        return null;
    }
}
