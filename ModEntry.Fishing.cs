using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int MaximumFishingWaterBodyTiles = 65536;

    private readonly record struct FishingWorkerPlan(
        SquadMemberState Member,
        NPC Npc,
        Vector2 StandTile,
        Vector2 CastTile,
        int WaterDepth);

    private IEnumerable<SquadMemberState> GetFishingContextCommandMembers(
        string locationName,
        Vector2 targetTile)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        return this.members.Values
            .Where(member => member.OwnerId == ownerId
                && member.Mode != CompanionMode.ParkedForDisconnect
                && this.HasCompanionFishingRod(member))
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => string.Equals(
                candidate.Npc?.currentLocation?.NameOrUniqueName,
                locationName,
                StringComparison.Ordinal))
            .OrderBy(candidate => Vector2.DistanceSquared(
                NormalizeTile(candidate.Npc!.Tile),
                NormalizeTile(targetTile)))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member);
    }

    private bool HasCompanionFishingRod(SquadMemberState member)
    {
        return this.TryGetCompanionFishingRod(member, out _);
    }

    private bool TryGetCompanionFishingRod(SquadMemberState member, out FishingRod rod)
    {
        rod = null!;
        FishingRod? best = null;
        foreach (SavedItemStack saved in member.Inventory)
        {
            if (!saved.QualifiedItemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
                continue;

            if (this.TryCreateItem(saved) is not FishingRod candidate)
                continue;

            if (best is null || candidate.UpgradeLevel > best.UpgradeLevel)
                best = candidate;
        }

        if (best is null)
            return false;

        rod = best;
        return true;
    }

    private void TryAssignFishingContextTask(
        long ownerId,
        string? requestedNpcName,
        GameLocation location,
        Vector2 rawWaterTile,
        string expectedTargetToken)
    {
        Vector2 waterTile = NormalizeTile(rawWaterTile);
        if (!this.IsFishingWaterTile(location, waterTile)
            || !string.Equals(GetContextWaterToken(waterTile), expectedTargetToken, StringComparison.Ordinal)
            || !this.TryDiscoverFishingWaterBody(location, waterTile, out FishingWaterBody waterBody))
        {
            this.WarnForPlayer(ownerId, "multiplayer.command_stale");
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner?.currentLocation != location)
        {
            this.WarnForPlayer(ownerId, "multiplayer.command_stale");
            return;
        }

        List<SquadMemberState> candidates = this.members.Values
            .Where(member => member.OwnerId == ownerId
                && member.Mode != CompanionMode.ParkedForDisconnect)
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => candidate.Npc?.currentLocation == location)
            .Where(candidate => string.IsNullOrWhiteSpace(requestedNpcName)
                || string.Equals(candidate.Member.NpcName, requestedNpcName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => Vector2.DistanceSquared(
                NormalizeTile(candidate.Npc!.Tile),
                waterTile))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member)
            .Take(12)
            .ToList();
        if (candidates.Count == 0)
        {
            this.WarnForPlayer(ownerId, "commands.no_followers");
            return;
        }

        List<SquadMemberState> rodHolders = candidates
            .Where(this.HasCompanionFishingRod)
            .ToList();
        if (rodHolders.Count == 0)
        {
            this.WarnForPlayer(ownerId, "fishing.no_rod");
            return;
        }

        Dictionary<string, (SquadMemberState Member, NPC Npc)> workersByName = rodHolders
            .Select(candidate => (Member: candidate, Npc: this.GetNpcByName(candidate.NpcName)))
            .Where(candidate => candidate.Npc is not null)
            .ToDictionary(
                candidate => candidate.Member.NpcName,
                candidate => (candidate.Member, candidate.Npc!),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FishingWorkerPlan> plansByNpc = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<Vector2, string> workerByStandTile = new();

        bool TryAugmentFishingAssignment(
            SquadMemberState candidate,
            NPC npc,
            HashSet<string> visitedWorkers,
            HashSet<Vector2> visitedStandTiles)
        {
            if (!visitedWorkers.Add(candidate.NpcName))
                return false;

            while (this.TryPlanFishingWorker(
                    location,
                    waterBody,
                    candidate,
                    npc,
                    new HashSet<Vector2>(),
                    visitedStandTiles,
                    out FishingWorkerPlan plan))
            {
                if (!visitedStandTiles.Add(plan.StandTile))
                    continue;

                bool standAvailable = !workerByStandTile.TryGetValue(
                    plan.StandTile,
                    out string? occupyingNpcName);
                if (!standAvailable
                    && occupyingNpcName is not null
                    && workersByName.TryGetValue(
                        occupyingNpcName,
                        out (SquadMemberState Member, NPC Npc) occupyingWorker))
                {
                    standAvailable = TryAugmentFishingAssignment(
                        occupyingWorker.Member,
                        occupyingWorker.Npc,
                        visitedWorkers,
                        visitedStandTiles);
                }

                if (!standAvailable)
                    continue;

                if (plansByNpc.TryGetValue(candidate.NpcName, out FishingWorkerPlan previousPlan)
                    && workerByStandTile.TryGetValue(previousPlan.StandTile, out string? previousOccupant)
                    && string.Equals(previousOccupant, candidate.NpcName, StringComparison.OrdinalIgnoreCase))
                {
                    workerByStandTile.Remove(previousPlan.StandTile);
                }

                plansByNpc[candidate.NpcName] = plan;
                workerByStandTile[plan.StandTile] = candidate.NpcName;
                return true;
            }

            return false;
        }

        foreach ((SquadMemberState member, NPC npc) in workersByName.Values)
        {
            TryAugmentFishingAssignment(
                member,
                npc,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<Vector2>());
        }

        List<FishingWorkerPlan> plans = rodHolders
            .Where(member => plansByNpc.ContainsKey(member.NpcName))
            .Select(member => plansByNpc[member.NpcName])
            .ToList();

        if (plans.Count == 0)
        {
            this.WarnForPlayer(ownerId, "fishing.no_safe_shore");
            return;
        }

        int assigned = 0;
        foreach (FishingWorkerPlan plan in plans)
        {
            // Planning above is read-only. Only workers with a complete,
            // reachable shore/cast pair have their previous order replaced.
            this.ClearCompanionWorkArea(plan.Member, cancelPendingAreaTask: true);
            this.RemovePendingTask(plan.Member.NpcName);
            this.ResumeFollowing(plan.Member.NpcName, ownerId, showMessage: false);
            this.SetTaskFailure(plan.Member, "");

            if (this.TryQueueFishingTask(location, waterBody, waterTile, plan))
                assigned++;
        }

        if (assigned == 0)
        {
            this.WarnForPlayer(ownerId, "fishing.no_safe_shore");
            return;
        }

        this.MarkStateDirty();
        string targetName = this.Tr("wheel.target_name.water");
        if (assigned == 1 && rodHolders.Count == 1)
        {
            this.InfoForPlayer(ownerId, "wheel.sent_one", new
            {
                npc = plans[0].Member.DisplayName,
                target = targetName
            });
        }
        else
        {
            this.InfoForPlayer(
                ownerId,
                assigned == rodHolders.Count ? "wheel.sent_many" : "fishing.sent_partial",
                new { count = assigned, total = rodHolders.Count, target = targetName });
        }
    }

    private bool TryPlanFishingWorker(
        GameLocation location,
        FishingWaterBody waterBody,
        SquadMemberState member,
        NPC npc,
        ISet<Vector2> assignedStandTiles,
        IReadOnlySet<Vector2>? excludedStandTiles,
        out FishingWorkerPlan plan)
    {
        plan = default;
        if (!this.TryGetCompanionFishingRod(member, out FishingRod rod))
            return false;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(
            location,
            npcTile,
            MaxFollowReachabilitySearchTiles);
        int castDistance = FishingSessionPolicy.GetMaximumCastDistance(
            rod.UpgradeLevel,
            this.HasSkill(member, "SKILL-FISHING-002"));

        IEnumerable<Vector2> shoreOptions = waterBody.ShoreTiles
            .Select(tile => new Vector2(tile.X, tile.Y))
            .Where(tile => !assignedStandTiles.Contains(tile))
            .Where(tile => excludedStandTiles?.Contains(tile) != true)
            .Where(tile => this.IsGroundCommandTileStructurallyValid(location, tile))
            .Where(tile => tile == npcTile || this.IsTileSafe(location, tile))
            .Where(tile => !this.IsStandTileReserved(location, tile, member.NpcName))
            .Where(tile => IsReachableOrUncertain(
                reachableDistances,
                tile,
                MaxFollowReachabilitySearchTiles))
            .OrderBy(tile => reachableDistances.TryGetValue(tile, out int pathDistance)
                ? pathDistance
                : int.MaxValue)
            .ThenBy(tile => reachableDistances.ContainsKey(tile)
                ? 0f
                : Vector2.DistanceSquared(tile, npcTile))
            .ThenBy(tile => Vector2.DistanceSquared(
                tile,
                new Vector2(waterBody.Anchor.X, waterBody.Anchor.Y)))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X);

        foreach (Vector2 standTile in shoreOptions)
        {
            if (!FishingWaterBodyPolicy.TrySelectBobber(
                    waterBody,
                    new FishingTile((int)standTile.X, (int)standTile.Y),
                    castDistance,
                    tile => this.IsFishingWaterTile(location, new Vector2(tile.X, tile.Y)),
                    out FishingBobberSelection bobber))
            {
                continue;
            }

            plan = new FishingWorkerPlan(
                member,
                npc,
                standTile,
                new Vector2(bobber.Tile.X, bobber.Tile.Y),
                bobber.ApproximateDepth);
            return true;
        }

        return false;
    }

    private bool TryQueueFishingTask(
        GameLocation location,
        FishingWaterBody waterBody,
        Vector2 waterAnchorTile,
        FishingWorkerPlan plan)
    {
        if (!this.IsFishingWaterTile(location, waterAnchorTile)
            || !this.IsFishingWaterTile(location, plan.CastTile)
            || !this.IsGroundCommandTileStructurallyValid(location, plan.StandTile)
            || this.IsStandTileReserved(location, plan.StandTile, plan.Member.NpcName)
            || !this.TryReserveStandTile(
                plan.Member.NpcName,
                location.NameOrUniqueName,
                plan.StandTile))
        {
            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.Fishing,
            NpcName = plan.Member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = waterAnchorTile,
            Manual = true,
            IgnoresTaskMode = true,
            IgnoresTaskToggle = true,
            WorkRadius = MaxCompanionDistanceTiles,
            ReturnDistance = MaxCompanionDistanceTiles,
            StartedTick = Game1.ticks,
            LastProcessedTick = Game1.ticks,
            StandTile = plan.StandTile,
            FishingWaterAnchorTile = waterAnchorTile,
            FishingCastTile = plan.CastTile,
            FishingWaterBodyToken = waterBody.Token,
            FishingWaterDepth = plan.WaterDepth,
            NextFishingTime = 0
        };

        this.pendingTasks[plan.Member.NpcName] = task;
        this.SetCompanionActivity(plan.Member, "companion.status.moving_to_fish");
        this.SetCompanionTarget(plan.Member, CompanionTaskKind.Fishing, plan.CastTile);
        this.UpdateTargetPreview(
            plan.Member,
            new TargetPreview(false, "", -1, -1, "companion.preview.inactive"));
        this.ShowCompanionWorkSignal(plan.Npc, location, plan.CastTile, "target");
        return true;
    }

    private void ProcessPendingFishingTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member)
            || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (FishingSessionPolicy.HasDayEnded(Game1.timeOfDay))
        {
            this.SetTaskResult(member, "companion.task_result.fishing_finished");
            this.RemovePendingTask(task);
            return;
        }

        NPC? npc = this.GetNpcByName(task.NpcName);
        GameLocation? location = Game1.getLocationFromName(task.LocationName);
        if (npc is null || location is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        if (!this.IsFishingWaterTile(location, task.FishingWaterAnchorTile)
            || !this.IsFishingWaterTile(location, task.FishingCastTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.fishing_water_lost", returning: true);
            return;
        }

        if (!this.TryGetCompanionFishingRod(member, out FishingRod activeRod))
        {
            this.RemovePendingTask(task, "companion.task_failure.fishing_rod_removed", returning: true);
            this.WarnForPlayer(member.OwnerId, "fishing.rod_removed", new { npc = member.DisplayName });
            return;
        }

        if (!this.TryRefreshFishingCastForRod(task, member, activeRod, location))
        {
            this.RemovePendingTask(task, "companion.task_failure.fishing_no_safe_shore", returning: true);
            this.WarnForPlayer(member.OwnerId, "fishing.no_safe_shore");
            return;
        }

        this.DisableNpcSchedule(npc, stopCurrentRoute: false);
        task.InactiveTicks = 0;
        Vector2 standTile = NormalizeTile(task.StandTile);
        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.SetCompanionActivity(member, "companion.status.moving_to_fish");
            if (!this.IsGroundCommandTileStructurallyValid(location, standTile)
                || task.NoProgressTicks >= TaskNoProgressUpdatesThreshold)
            {
                if (!this.TryReplanFishingStand(task, member, npc, location))
                {
                    this.RemovePendingTask(task, "companion.task_failure.fishing_no_safe_shore", returning: true);
                    this.WarnForPlayer(member.OwnerId, "fishing.no_safe_shore");
                    return;
                }

                standTile = NormalizeTile(task.StandTile);
            }

            // The companion controller deliberately pauses instead of
            // teleporting when nobody is simulating this map. Preserve the
            // route and resume it when a farmer returns.
            if (!location.farmers.Any())
            {
                task.NoProgressTicks = 0;
                return;
            }

            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        this.StopCompanionMovement(npc);
        this.FaceTile(npc, task.FishingCastTile);
        task.NoProgressTicks = 0;
        this.SetCompanionActivity(member, "companion.status.fishing");

        if (task.NextFishingTime <= 0)
        {
            task.NextFishingTime = FishingSessionPolicy.AddMinutes(
                Game1.timeOfDay,
                FishingSessionPolicy.GetCatchIntervalMinutes(
                    this.HasSkill(member, "SKILL-FISHING-001")));
            this.Say(
                npc,
                "Fishing_Waiting",
                force: false,
                ownerIdOverride: member.OwnerId,
                context: new CompanionDialogueContext
                {
                    TaskKind = CompanionTaskKind.Fishing,
                    IsManual = true
                });
            return;
        }

        if (!FishingSessionPolicy.IsCatchReady(Game1.timeOfDay, task.NextFishingTime))
            return;

        this.TryPerformCompanionFishingCatch(task, member, npc, location);
        task.NextFishingTime = FishingSessionPolicy.AddMinutes(
            Game1.timeOfDay,
            FishingSessionPolicy.GetCatchIntervalMinutes(
                this.HasSkill(member, "SKILL-FISHING-001")));
    }

    private bool TryReplanFishingStand(
        PendingCompanionTask task,
        SquadMemberState member,
        NPC npc,
        GameLocation location)
    {
        this.ReleaseStandTile(task.NpcName, task.LocationName, task.StandTile);
        task.RejectedStandTiles.Add(NormalizeTile(task.StandTile));
        if (!this.TryDiscoverFishingWaterBody(
                location,
                task.FishingWaterAnchorTile,
                out FishingWaterBody waterBody)
            || !string.Equals(
                waterBody.Token,
                task.FishingWaterBodyToken,
                StringComparison.Ordinal)
            || !this.TryPlanFishingWorker(
                location,
                waterBody,
                member,
                npc,
                new HashSet<Vector2>(),
                task.RejectedStandTiles,
                out FishingWorkerPlan plan)
            || !this.TryReserveStandTile(task.NpcName, task.LocationName, plan.StandTile))
        {
            return false;
        }

        this.StopCompanionMovement(npc);
        task.StandTile = plan.StandTile;
        task.FishingCastTile = plan.CastTile;
        task.FishingWaterDepth = plan.WaterDepth;
        task.HasPathStartAttempted = false;
        task.HasLastProgressPosition = false;
        task.NoProgressTicks = 0;
        task.LastPathTick = 0;
        task.NextFishingTime = 0;
        this.SetCompanionTarget(member, CompanionTaskKind.Fishing, plan.CastTile);
        return true;
    }

    private bool TryRefreshFishingCastForRod(
        PendingCompanionTask task,
        SquadMemberState member,
        FishingRod rod,
        GameLocation location)
    {
        Vector2 standTile = NormalizeTile(task.StandTile);
        Vector2 castTile = NormalizeTile(task.FishingCastTile);
        int maximumCastDistance = FishingSessionPolicy.GetMaximumCastDistance(
            rod.UpgradeLevel,
            this.HasSkill(member, "SKILL-FISHING-002"));
        int currentCastDistance = (int)(Math.Abs(standTile.X - castTile.X)
            + Math.Abs(standTile.Y - castTile.Y));
        if (currentCastDistance > 0
            && currentCastDistance <= maximumCastDistance
            && this.IsFishingWaterTile(location, castTile))
        {
            return true;
        }

        if (!this.TryDiscoverFishingWaterBody(
                location,
                task.FishingWaterAnchorTile,
                out FishingWaterBody waterBody)
            || !string.Equals(waterBody.Token, task.FishingWaterBodyToken, StringComparison.Ordinal)
            || !FishingWaterBodyPolicy.TrySelectBobber(
                waterBody,
                new FishingTile((int)standTile.X, (int)standTile.Y),
                maximumCastDistance,
                tile => this.IsFishingWaterTile(location, new Vector2(tile.X, tile.Y)),
                out FishingBobberSelection bobber))
        {
            return false;
        }

        task.FishingCastTile = new Vector2(bobber.Tile.X, bobber.Tile.Y);
        task.FishingWaterDepth = bobber.ApproximateDepth;
        task.NextFishingTime = 0;
        this.SetCompanionTarget(member, CompanionTaskKind.Fishing, task.FishingCastTile);
        return true;
    }

    private void TryPerformCompanionFishingCatch(
        PendingCompanionTask task,
        SquadMemberState member,
        NPC npc,
        GameLocation location)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || !this.TryGetCompanionFishingRod(member, out _))
            return;

        Item? caught;
        try
        {
            // Call the data-driven selector directly. Virtual getFish overrides
            // can consume unique quest/team rewards before returning a non-fish
            // item, which an automated companion must never trigger.
            caught = GameLocation.GetFishFromLocationData(
                location.Name,
                task.FishingCastTile,
                Math.Max(1, task.FishingWaterDepth),
                owner,
                isTutorialCatch: false,
                isInherited: false,
                location: location);
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Companion fishing failed safely for '{member.NpcName}' at {location.NameOrUniqueName}: {ex.Message}",
                LogLevel.Warn);
            return;
        }

        if (caught is not SObject fish
            || fish.Category != SObject.FishCategory
            || !string.IsNullOrWhiteSpace(fish.SetFlagOnPickup)
            || (fish.TryGetTempData<bool>("IsBossFish", out bool isBossFish) && isBossFish)
            || fish.HasContextTag("fish_legendary")
            || fish.HasContextTag("fish_legendary_family"))
        {
            return;
        }

        fish.Stack = 1;
        fish.Quality = Math.Max(
            fish.Quality,
            FishingSessionPolicy.GetCatchQuality(
                task.FishingWaterDepth,
                this.HasSkill(member, "SKILL-FISHING-002")));
        this.CommitCompanionFishingCatch(task, member, npc, location, fish);

        if (FishingSessionPolicy.RollsExtraCatch(
                this.HasSkill(member, "SKILL-FISHING-003"),
                Game1.random.NextDouble())
            && fish.getOne() is SObject bonusFish)
        {
            bonusFish.Stack = 1;
            bonusFish.Quality = fish.Quality;
            this.CommitCompanionFishingCatch(task, member, npc, location, bonusFish);
        }
    }

    private void CommitCompanionFishingCatch(
        PendingCompanionTask task,
        SquadMemberState member,
        NPC npc,
        GameLocation location,
        Item fish)
    {
        // Record the successful catch first. If every inventory fallback is
        // full, routing then adds a durable overflow failure without having it
        // cleared again by the success result.
        this.SetTaskResult(member, "companion.task_result.caught_fish");
        this.RouteTaskRewardOrDrop(
            member,
            fish,
            location,
            task.StandTile,
            "companion.loot_source.fishing");
        this.AddCompanionXp(member, FishingSessionPolicy.XpPerCatch);
        this.ShowCompanionWorkSignal(npc, location, task.FishingCastTile, "fish");
        this.Say(
            npc,
            "Fishing_Caught",
            force: false,
            ownerIdOverride: member.OwnerId,
            context: new CompanionDialogueContext
            {
                TaskKind = CompanionTaskKind.Fishing,
                ItemName = fish.DisplayName,
                ItemId = fish.QualifiedItemId,
                ResultKey = "companion.task_result.caught_fish",
                IsManual = true
            });
    }

    private bool TryDiscoverFishingWaterBody(
        GameLocation location,
        Vector2 rawWaterTile,
        out FishingWaterBody waterBody)
    {
        waterBody = null!;
        if (location.Map is null || location.Map.Layers.Count == 0)
            return false;

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        long mapTileCount = (long)width * height;
        int componentLimit = (int)Math.Min(MaximumFishingWaterBodyTiles, Math.Max(1L, mapTileCount));
        Vector2 waterTile = NormalizeTile(rawWaterTile);
        return FishingWaterBodyPolicy.TryDiscover(
            new FishingTile((int)waterTile.X, (int)waterTile.Y),
            width,
            height,
            tile => location.isWaterTile(tile.X, tile.Y),
            componentLimit,
            out waterBody);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || this.saveWritesBlocked)
            return;

        foreach (PendingCompanionTask task in this.pendingTasks.Values
            .Where(task => task.Kind == CompanionTaskKind.Fishing)
            .ToList())
        {
            if (this.members.TryGetValue(task.NpcName, out SquadMemberState? member))
                this.SetTaskResult(member, "companion.task_result.fishing_finished");
            this.RemovePendingTask(task);
        }
    }

    private void DrawCompanionFishingVisuals(SpriteBatch b)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null)
            return;

        string locationName = Game1.currentLocation.NameOrUniqueName;
        float bob = MathF.Sin(Game1.ticks * 0.12f) * 2f;
        foreach (SquadMemberState member in this.members.Values)
        {
            if (member.CurrentActivityKey != "companion.status.fishing"
                || member.CurrentTargetKey != "companion.target.fishing"
                || member.CurrentTargetX < 0
                || member.CurrentTargetY < 0)
            {
                continue;
            }

            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc?.currentLocation is null
                || npc.IsInvisible
                || !string.Equals(
                    npc.currentLocation.NameOrUniqueName,
                    locationName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            Rectangle bounds = npc.GetBoundingBox();
            Vector2 shoulderWorld = new(bounds.Center.X, bounds.Top + 5f);
            Vector2 bobberWorld = new(
                member.CurrentTargetX * 64f + 32f,
                member.CurrentTargetY * 64f + 30f + bob);
            Vector2 direction = bobberWorld - shoulderWorld;
            if (direction.LengthSquared() <= 0.001f)
                continue;
            direction.Normalize();
            Vector2 rodTipWorld = shoulderWorld
                + direction * 52f
                + new Vector2(0f, -38f);
            Vector2 shoulder = Game1.GlobalToLocal(Game1.viewport, shoulderWorld);
            Vector2 rodTip = Game1.GlobalToLocal(Game1.viewport, rodTipWorld);
            Vector2 bobber = Game1.GlobalToLocal(Game1.viewport, bobberWorld);

            DrawAnimationLine(b, shoulder, rodTip, new Color(118, 73, 39), 4f);
            DrawAnimationLine(b, rodTip, bobber, new Color(205, 224, 225) * 0.9f, 1f);
            b.Draw(
                Game1.staminaRect,
                new Rectangle((int)bobber.X - 3, (int)bobber.Y - 4, 6, 4),
                Color.White);
            b.Draw(
                Game1.staminaRect,
                new Rectangle((int)bobber.X - 3, (int)bobber.Y, 6, 4),
                new Color(208, 61, 55));
        }
    }
}
