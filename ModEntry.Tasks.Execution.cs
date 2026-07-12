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
        if (this.IsBlockedGameState(blockForMenu: true))
        {
            foreach (PendingCompanionTask pausedTask in this.pendingTasks.Values)
                pausedTask.LastProcessedTick = Game1.ticks;
            return;
        }

        foreach (PendingCompanionTask task in this.pendingTasks.Values.ToList())
        {
            try
            {
                int lastProcessedTick = task.LastProcessedTick <= 0 ? Game1.ticks : task.LastProcessedTick;
                task.ActiveTicks += Math.Clamp(Game1.ticks - lastProcessedTick, 0, 30);
                task.LastProcessedTick = Game1.ticks;
                this.ProcessPendingTask(task);
            }
            catch (Exception ex)
            {
                this.Monitor.Log(
                    $"Companion task '{task.Kind}' for '{task.NpcName}' failed unexpectedly at {task.LocationName} ({task.TargetTile.X}, {task.TargetTile.Y}). The task was cancelled safely. {ex}",
                    LogLevel.Error);
                this.RemovePendingTask(task, "companion.task_failure.unexpected_error", returning: true);
            }
        }
    }

    private void ProcessPendingTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member)
            || !this.AreTasksEnabled(member.OwnerId))
        {
            this.RemovePendingTask(task, "companion.task_failure.tasks_disabled", returning: true);
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
        if (taskModeDisabled)
        {
            this.RemovePendingTask(task, "companion.task_failure.tasks_disabled", returning: true);
            return;
        }

        switch (task.Kind)
        {
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

        if (task.ActiveTicks > InstantTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout", returning: true);
            if (task.Manual)
                this.Warn("tasks.no_valid_target");

            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner?.currentLocation is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        GameLocation location = owner.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        NPC? npc = this.GetNpcByName(task.NpcName);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost", returning: true);
            return;
        }

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
                        this.Warn("tasks.bee_flower_protected");

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

        if (!IsWithinCompanionDistance(owner.Tile, targetTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        if (ownerDistance > Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance))
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            return;
        }

        if (!this.TryResolveTaskStandTile(location, targetTile, npc, member, task, MaxCompanionDistanceTiles, out Vector2 standTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.no_safe_tile", returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
        this.FaceTile(npc, targetTile);

        switch (task.Kind)
        {
            case CompanionTaskKind.Watering:
                dirt!.state.Value = HoeDirt.watered;
                this.ShowCompanionWorkSignal(npc, location, targetTile, "water");
                this.AddCompanionXp(member, 1);
                this.SetTaskResult(member, "companion.task_result.watered");
                if (task.Manual)
                {
                    this.Say(npc, "Watering", force: false);
                    this.Info("tasks.watered", new { npc = member.DisplayName });
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
                if (task.Manual)
                {
                    this.Say(npc, "Foraging", force: false);
                    this.Info("tasks.gathered", new { npc = member.DisplayName, item = item.DisplayName });
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
                if (task.Manual)
                {
                    this.Say(npc, "Harvesting", force: false);
                    this.Info("tasks.harvested", new { npc = member.DisplayName });
                }

                break;

            case CompanionTaskKind.Petting:
                animal!.pet(owner);
                this.ShowCompanionWorkSignal(npc, location, targetTile, "pet");
                this.AddCompanionXp(member, 2);
                this.SetTaskResult(member, "companion.task_result.petted");
                if (task.Manual)
                {
                    this.Say(npc, "Petting", force: false);
                    this.Info("tasks.petted", new { npc = member.DisplayName, animal = animal.displayName });
                }

                break;
        }

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

        if (task.ActiveTicks > LumberTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout");
            if (task.Manual)
                this.Warn("tasks.no_valid_target");

            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner?.currentLocation is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        GameLocation location = owner.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        NPC? npc = Game1.getCharacterFromName(task.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

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
                this.Warn("tasks.need_axe");

            this.RemovePendingTask(task, "companion.task_failure.need_axe");
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        int returnDistance = Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance);
        if (ownerDistance > returnDistance)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            if (!task.UsesWorkDirective)
                this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            return;
        }

        ResetCompanionMovementSpeed(npc);

        int workRadius = Math.Max(MaxCompanionDistanceTiles, task.WorkRadius);
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

        if (Game1.ticks - task.LastActionTick < this.GetLumberHitCooldown(member))
            return;

        bool finished = this.PerformLumberHit(location, tree, task.TargetTile, member, npc, axe, task.Manual);
        task.LastActionTick = Game1.ticks;
        if (finished)
            this.RemovePendingTask(task);
    }

    private void ProcessPendingMiningTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member) || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (task.ActiveTicks > MiningTaskTimeoutTicks)
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

        GameLocation location = owner.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        NPC? npc = Game1.getCharacterFromName(task.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

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
        if (ownerDistance > returnDistance)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            if (!task.UsesWorkDirective)
                this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            return;
        }

        ResetCompanionMovementSpeed(npc);

        int workRadius = Math.Max(MaxCompanionDistanceTiles, task.WorkRadius);
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

        if (Game1.ticks - task.LastActionTick < this.GetMiningHitCooldown(member))
            return;

        bool finished = this.PerformMiningHit(location, obj, task.TargetTile, member, npc, task, pickaxe);
        task.LastActionTick = Game1.ticks;
        if (finished)
            this.RemovePendingTask(task);
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
        if (owner is not null && owner.currentLocation == location)
        {
            bool isNpcTile = currentStandTile == npcTile;
            bool tileAvailable = isNpcTile || this.IsTileSafe(location, currentStandTile);
            bool adjacent = Vector2.Distance(currentStandTile, targetTile) <= TaskArrivalDistance;
            bool withinOwnerRange = IsWithinOwnerDistance(owner.Tile, currentStandTile, maxOwnerDistance);
            bool reachable = this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles).ContainsKey(currentStandTile);
            if (tileAvailable
                && adjacent
                && withinOwnerRange
                && reachable
                && !this.IsStandTileReserved(location, currentStandTile, member.NpcName))
            {
                standTile = currentStandTile;
                return true;
            }
        }

        this.ReleaseStandTile(task.NpcName, task.LocationName, task.StandTile);
        if (this.TryFindSafeAdjacentTile(location, targetTile, npc, member, maxOwnerDistance, out standTile)
            && this.TryReserveStandTile(task.NpcName, task.LocationName, standTile))
        {
            task.StandTile = standTile;
            return true;
        }

        standTile = npcTile;
        return false;
    }

    private bool TryFindSafeAdjacentTile(GameLocation location, Vector2 targetTile, NPC npc, SquadMemberState member, int maxOwnerDistance, out Vector2 standTile)
    {
        Vector2[] offsets =
        {
            new(0, 1),
            new(1, 0),
            new(-1, 0),
            new(0, -1)
        };

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        if (owner is null || owner.currentLocation != location)
        {
            standTile = npcTile;
            return false;
        }

        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles);
        if (Vector2.Distance(npcTile, targetTile) <= TaskArrivalDistance
            && IsWithinOwnerDistance(ownerTile, npcTile, maxOwnerDistance)
            && !this.IsStandTileReserved(location, npcTile, member.NpcName))
        {
            standTile = npcTile;
            return true;
        }

        foreach (Vector2 candidate in offsets
            .Select(offset => NormalizeTile(targetTile + offset))
            .Where(candidate => IsWithinOwnerDistance(ownerTile, candidate, maxOwnerDistance))
            .Where(candidate => !this.IsStandTileReserved(location, candidate, member.NpcName))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => reachableDistances.ContainsKey(candidate))
            .OrderBy(candidate => reachableDistances[candidate])
            .ThenBy(candidate => Vector2.Distance(candidate, npcTile)))
        {
            standTile = candidate;
            return true;
        }

        standTile = npcTile;
        return false;
    }

    private bool IsNpcAtTaskTile(NPC npc, Vector2 standTile)
    {
        return Vector2.Distance(npc.Tile, standTile) <= TaskArrivalDistance;
    }

    private void RouteNpcToTaskTile(NPC npc, GameLocation location, Vector2 standTile, PendingCompanionTask task, bool force)
    {
        int retryTicks = 30;
        this.members.TryGetValue(task.NpcName, out SquadMemberState? member);
        if (member is not null && this.HasSkill(member, "SKILL-UTILITY-001"))
            retryTicks = 24;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        standTile = NormalizeTile(standTile);
        float distance = Vector2.Distance(npcTile, standTile);
        if (!force)
        {
            if (task.LastDistanceToStandTile >= 0f && distance >= task.LastDistanceToStandTile - FollowProgressTolerance)
                task.NoProgressTicks++;
            else
                task.NoProgressTicks = 0;

            if (task.NoProgressTicks >= TaskNoProgressUpdatesThreshold && member is not null)
            {
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
                task.NoProgressTicks = 0;
                task.LastPathTick = 0;
                force = true;
            }
        }
        else
        {
            task.NoProgressTicks = 0;
        }

        task.LastDistanceToStandTile = distance;

        if (!force && Game1.ticks - task.LastPathTick < retryTicks)
            return;

        if (!this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles).ContainsKey(standTile))
        {
            if (member is not null)
            {
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
            }

            return;
        }

        task.LastPathTick = Game1.ticks;
        if (member is not null && member.CurrentActivityKey == "companion.status.stuck")
            this.SetCompanionActivity(member, "companion.status.working");

        ResetCompanionMovementSpeed(npc);
        try
        {
            npc.controller = new StardewValley.Pathfinding.PathFindController(npc, location, new Point((int)standTile.X, (int)standTile.Y), -1);
        }
        catch (Exception ex)
        {
            npc.controller = null;
            this.Monitor.Log($"Could not route '{task.NpcName}' to task tile {standTile}: {ex.Message}", LogLevel.Warn);
            this.RemovePendingTask(task, "companion.task_failure.unexpected_error", returning: true);
        }
    }

    private bool PerformLumberHit(GameLocation location, Tree tree, Vector2 tile, SquadMemberState member, NPC npc, Axe? axe, bool manual)
    {
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
        this.FaceTile(npc, tile);
        npc.Sprite.Animate(Game1.currentGameTime, npc.Sprite.currentFrame, 2, 80f);
        npc.jumpWithoutSound(4f);
        npc.shake(150);
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

            if (manual)
                this.Info("tasks.lumbered", new { npc = member.DisplayName });

            return true;
        }

        return false;
    }

    private bool PerformMiningHit(GameLocation location, SObject obj, Vector2 tile, SquadMemberState member, NPC npc, PendingCompanionTask task, Pickaxe? pickaxe)
    {
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
        this.FaceTile(npc, tile);
        npc.Sprite.Animate(Game1.currentGameTime, npc.Sprite.currentFrame, 2, 80f);
        npc.jumpWithoutSound(4f);
        npc.shake(150);
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
