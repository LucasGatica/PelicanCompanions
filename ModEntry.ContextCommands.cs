using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private readonly record struct ContextWorkerPlan(
        SquadMemberState Member,
        NPC Npc,
        Vector2 StandTile);

    private bool TryGetContextWorldTarget(GameLocation? location, Vector2 rawTile, out ContextWorldTarget target)
    {
        target = default;
        if (location is null)
            return false;

        Vector2 tile = NormalizeTile(rawTile);
        HoeDirt? dirt = location.GetHoeDirtAtTile(tile);
        Crop? crop = dirt?.crop;
        if (dirt is not null
            && crop is not null
            && !crop.dead.Value
            && dirt.readyForHarvest()
            && crop.GetHarvestMethod() == HarvestMethod.Grab)
        {
            target = new ContextWorldTarget(
                CompanionTaskKind.Harvesting,
                location.NameOrUniqueName,
                tile,
                "wheel.target.crop",
                "wheel.target_name.crop",
                GetContextCropToken(crop),
                crop);
            return true;
        }

        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is Tree tree
            && this.IsValidWoodTarget(location, tile))
        {
            target = new ContextWorldTarget(
                CompanionTaskKind.Lumbering,
                location.NameOrUniqueName,
                tile,
                "wheel.target.tree",
                "wheel.target_name.tree",
                GetContextTreeToken(tree),
                tree);
            return true;
        }

        if (location.Objects.TryGetValue(tile, out SObject? obj) && this.IsSafeMineableObject(obj))
        {
            target = new ContextWorldTarget(
                CompanionTaskKind.Mining,
                location.NameOrUniqueName,
                tile,
                "wheel.target.stone",
                "wheel.target_name.stone",
                GetContextStoneToken(obj),
                obj);
            return true;
        }

        return false;
    }

    private bool TryGetContextWorldTargetUnderCursor(ICursorPosition cursor, out ContextWorldTarget target)
    {
        GameLocation? location = Game1.currentLocation;
        target = default;
        if (location is null)
            return false;

        Point worldPoint = new((int)MathF.Round(cursor.AbsolutePixels.X), (int)MathF.Round(cursor.AbsolutePixels.Y));
        foreach (Vector2 treeTile in location.terrainFeatures.Keys
            .Select(tile => new { Tile = tile, Feature = location.terrainFeatures[tile] })
            .Where(candidate => candidate.Feature is Tree tree
                && this.IsValidWoodTarget(location, candidate.Tile)
                && tree.getRenderBounds().Contains(worldPoint))
            .OrderByDescending(candidate => candidate.Tile.Y)
            .ThenBy(candidate => Vector2.DistanceSquared(
                new Vector2(candidate.Feature.getRenderBounds().Center.X, candidate.Feature.getRenderBounds().Center.Y),
                new Vector2(worldPoint.X, worldPoint.Y)))
            .Select(candidate => candidate.Tile))
        {
            if (this.TryGetContextWorldTarget(location, treeTile, out target))
                return true;
        }

        return this.TryGetContextWorldTarget(location, NormalizeTile(cursor.Tile), out target);
    }

    private static string GetContextCropToken(Crop crop)
    {
        string? cropId = crop.GetData()?.HarvestItemId;
        if (string.IsNullOrWhiteSpace(cropId))
            cropId = crop.indexOfHarvest.Value;

        return $"crop|{cropId}";
    }

    private static string GetContextTreeToken(Tree tree)
    {
        return $"tree|{tree.treeType.Value}|{tree.growthStage.Value}";
    }

    private static string GetContextStoneToken(SObject obj)
    {
        return $"stone|{obj.QualifiedItemId}|{obj.ParentSheetIndex}";
    }

    private bool TryGetContextTargetIdentity(
        GameLocation location,
        CompanionTaskKind kind,
        Vector2 rawTile,
        out string token,
        out object? instance)
    {
        Vector2 tile = NormalizeTile(rawTile);
        switch (kind)
        {
            case CompanionTaskKind.Harvesting:
                Crop? crop = location.GetHoeDirtAtTile(tile)?.crop;
                if (crop is not null)
                {
                    token = GetContextCropToken(crop);
                    instance = crop;
                    return true;
                }
                break;

            case CompanionTaskKind.Lumbering:
                if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature) && feature is Tree tree)
                {
                    token = GetContextTreeToken(tree);
                    instance = tree;
                    return true;
                }
                break;

            case CompanionTaskKind.Mining:
                if (location.Objects.TryGetValue(tile, out SObject? obj))
                {
                    token = GetContextStoneToken(obj);
                    instance = obj;
                    return true;
                }
                break;
        }

        token = "";
        instance = null;
        return false;
    }

    private bool IsExpectedContextTargetCurrent(PendingCompanionTask task, GameLocation location)
    {
        if (task.ExpectedTargetInstance is null && string.IsNullOrWhiteSpace(task.ExpectedTargetToken))
            return true;

        if (!this.TryGetContextTargetIdentity(
                location,
                task.Kind,
                task.TargetTile,
                out string currentToken,
                out object? currentInstance))
        {
            return false;
        }

        if (task.ExpectedTargetInstance is not null)
            return ReferenceEquals(task.ExpectedTargetInstance, currentInstance);

        return string.IsNullOrWhiteSpace(task.ExpectedTargetToken)
            || string.Equals(task.ExpectedTargetToken, currentToken, StringComparison.Ordinal);
    }

    private bool IsContextWorldTargetValid(ContextWorldTarget expected)
    {
        GameLocation? location = Game1.currentLocation;
        return location is not null
            && string.Equals(location.NameOrUniqueName, expected.LocationName, StringComparison.Ordinal)
            && this.TryGetContextWorldTarget(location, expected.Tile, out ContextWorldTarget current)
            && current.Kind == expected.Kind
            && string.Equals(current.TargetToken, expected.TargetToken, StringComparison.Ordinal)
            && ReferenceEquals(current.TargetInstance, expected.TargetInstance);
    }

    private IEnumerable<SquadMemberState> GetContextCommandMembers(string locationName, Vector2 targetTile)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        return this.members.Values
            .Where(member => member.OwnerId == ownerId && member.Mode != CompanionMode.ParkedForDisconnect)
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => candidate.Npc?.currentLocation?.NameOrUniqueName == locationName
                && IsWithinCompanionDistance(Game1.player.Tile, candidate.Npc.Tile))
            .OrderBy(candidate => Vector2.DistanceSquared(NormalizeTile(candidate.Npc!.Tile), NormalizeTile(targetTile)))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member);
    }

    private void RequestContextTask(ContextWorldTarget target, string? npcName)
    {
        if (!this.IsContextWorldTargetValid(target))
        {
            this.Warn("tasks.no_valid_target");
            return;
        }

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "ContextTask",
                npcName ?? "",
                argument: target.Kind.ToString(),
                tile: target.Tile,
                expectedItemToken: target.TargetToken,
                expectedLocationName: target.LocationName);
            return;
        }

        this.TryAssignContextTask(
            Game1.player.UniqueMultiplayerID,
            npcName,
            target.Kind,
            target.LocationName,
            target.Tile,
            target.TargetToken);
    }

    private void TryAssignContextTask(
        long ownerId,
        string? requestedNpcName,
        CompanionTaskKind kind,
        string locationName,
        Vector2 rawTile,
        string expectedTargetToken)
    {
        if (kind is not (CompanionTaskKind.Lumbering or CompanionTaskKind.Mining or CompanionTaskKind.Harvesting)
            || !this.AreTaskActionsSafe(ownerId))
        {
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        GameLocation? location = owner?.currentLocation;
        Vector2 tile = NormalizeTile(rawTile);
        if (owner is null
            || location is null
            || !string.Equals(location.NameOrUniqueName, locationName, StringComparison.Ordinal)
            || !IsWithinCompanionDistance(owner.Tile, tile)
            || !this.TryGetContextWorldTarget(location, tile, out ContextWorldTarget currentTarget)
            || currentTarget.Kind != kind
            || !string.Equals(currentTarget.TargetToken, expectedTargetToken, StringComparison.Ordinal))
        {
            this.Warn("multiplayer.command_stale");
            return;
        }

        if (kind == CompanionTaskKind.Harvesting
            && Context.IsMainPlayer
            && ownerId != Game1.player.UniqueMultiplayerID
            && Game1.getOnlineFarmers().Count() > 1)
        {
            this.Warn("wheel.remote_crop_unsupported");
            return;
        }

        HoeDirt? cropDirt = kind == CompanionTaskKind.Harvesting
            ? location.GetHoeDirtAtTile(tile)
            : null;
        if (cropDirt?.crop is Crop crop && this.IsProtectedBeeHouseFlower(location, tile, crop))
        {
            this.Warn("tasks.bee_flower_protected");
            return;
        }

        List<SquadMemberState> candidates = this.members.Values
            .Where(member => member.OwnerId == ownerId && member.Mode != CompanionMode.ParkedForDisconnect)
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => candidate.Npc?.currentLocation == location
                && IsWithinCompanionDistance(owner.Tile, candidate.Npc.Tile))
            .Where(candidate => string.IsNullOrWhiteSpace(requestedNpcName)
                || string.Equals(candidate.Member.NpcName, requestedNpcName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => Vector2.DistanceSquared(NormalizeTile(candidate.Npc!.Tile), tile))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member)
            .ToList();
        if (candidates.Count == 0)
        {
            this.Warn("commands.no_followers");
            return;
        }

        HashSet<string> candidateNames = candidates
            .Select(member => member.NpcName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!this.CanPrepareContextTarget(location.NameOrUniqueName, tile, candidateNames, out HashSet<string> requiredWorkers))
        {
            this.Warn("wheel.no_worker_available");
            return;
        }

        List<ContextWorkerPlan> plans = this.PlanContextWorkers(location, owner, tile, candidates, requiredWorkers);
        HashSet<string> plannedNames = plans
            .Select(plan => plan.Member.NpcName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (plans.Count == 0 || !requiredWorkers.All(plannedNames.Contains))
        {
            this.Warn("wheel.no_worker_available");
            return;
        }

        // Preparation above is read-only. Only companions with a valid,
        // reservable stand tile have their previous order replaced.
        foreach (ContextWorkerPlan plan in plans)
        {
            this.ClearCompanionWorkArea(plan.Member, cancelPendingAreaTask: true);
            this.RemovePendingTask(plan.Member.NpcName);
            this.ResumeFollowing(plan.Member.NpcName, ownerId, showMessage: false);
            this.SetTaskFailure(plan.Member, "");
        }

        string groupId = plans.Count > 1 ? Guid.NewGuid().ToString("N") : "";
        int assigned = 0;
        foreach (ContextWorkerPlan plan in plans)
        {
            bool queued = kind switch
            {
                CompanionTaskKind.Lumbering => this.TryQueueDirectiveLumberTask(
                    location,
                    tile,
                    plan.Member,
                    plan.Npc,
                    MaxCompanionDistanceTiles,
                    manual: true,
                    ignoreTaskMode: true,
                    sharedTargetGroupId: groupId,
                    ignoreTaskToggle: true,
                    expectedTargetToken: currentTarget.TargetToken,
                    expectedTargetInstance: currentTarget.TargetInstance,
                    preparedStandTile: plan.StandTile),
                CompanionTaskKind.Mining => this.TryQueueDirectiveMiningTask(
                    location,
                    tile,
                    plan.Member,
                    plan.Npc,
                    MaxCompanionDistanceTiles,
                    manual: true,
                    ignoreTaskMode: true,
                    sharedTargetGroupId: groupId,
                    ignoreTaskToggle: true,
                    expectedTargetToken: currentTarget.TargetToken,
                    expectedTargetInstance: currentTarget.TargetInstance,
                    preparedStandTile: plan.StandTile),
                CompanionTaskKind.Harvesting => this.TryQueueInstantTask(
                    CompanionTaskKind.Harvesting,
                    location,
                    tile,
                    plan.Member,
                    manual: true,
                    ignoreTaskMode: true,
                    sharedTargetGroupId: groupId,
                    ignoreTaskToggle: true,
                    expectedTargetToken: currentTarget.TargetToken,
                    expectedTargetInstance: currentTarget.TargetInstance,
                    preparedStandTile: plan.StandTile),
                _ => false
            };

            if (queued)
                assigned++;
        }

        if (assigned == 0)
        {
            this.Warn("wheel.no_worker_available");
            return;
        }

        this.MarkStateDirty();
        string targetName = this.Tr(currentTarget.TargetNameKey);
        if (candidates.Count == 1)
        {
            this.Info("wheel.sent_one", new
            {
                npc = candidates[0].DisplayName,
                target = targetName
            });
        }
        else
        {
            this.Info(assigned == candidates.Count ? "wheel.sent_many" : "wheel.sent_partial", new
            {
                count = assigned,
                total = candidates.Count,
                target = targetName
            });
        }
    }

    private bool CanPrepareContextTarget(
        string locationName,
        Vector2 tile,
        ISet<string> candidateNames,
        out HashSet<string> requiredWorkers)
    {
        requiredWorkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workTargetReservations.TryGetValue(key, out string? exclusiveOwner))
        {
            if (!candidateNames.Contains(exclusiveOwner))
                return false;

            requiredWorkers.Add(exclusiveOwner);
        }

        if (this.sharedWorkTargetReservations.TryGetValue(key, out SharedWorkTargetReservation? shared))
        {
            if (shared.NpcNames.Any(name => !candidateNames.Contains(name)))
                return false;

            requiredWorkers.UnionWith(shared.NpcNames);
        }

        return true;
    }

    private List<ContextWorkerPlan> PlanContextWorkers(
        GameLocation location,
        Farmer owner,
        Vector2 targetTile,
        IEnumerable<SquadMemberState> candidates,
        ISet<string> requiredWorkers)
    {
        List<(SquadMemberState Member, NPC Npc, int Order)> available = candidates
            .Select((member, order) => new { Member = member, Npc = this.GetNpcByName(member.NpcName), Order = order })
            .Where(candidate => candidate.Npc?.currentLocation == location)
            .Select(candidate => (candidate.Member, candidate.Npc!, candidate.Order))
            .Take(12)
            .ToList();
        Dictionary<string, List<Vector2>> baseOptions = available.ToDictionary(
            candidate => candidate.Member.NpcName,
            candidate => this.GetContextStandOptions(
                location,
                owner,
                targetTile,
                candidate.Npc),
            StringComparer.OrdinalIgnoreCase);
        int maximumWorkers = Math.Min(5, available.Count);
        int subsetCount = 1 << available.Count;
        for (int workerCount = maximumWorkers; workerCount >= 1; workerCount--)
        {
            for (int mask = 1; mask < subsetCount; mask++)
            {
                if (CountSetBits(mask) != workerCount)
                    continue;

                List<(SquadMemberState Member, NPC Npc, int Order)> subset = available
                    .Where((_, index) => (mask & (1 << index)) != 0)
                    .ToList();
                HashSet<string> replaceableWorkers = subset
                    .Select(candidate => candidate.Member.NpcName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!requiredWorkers.All(replaceableWorkers.Contains))
                    continue;

                Dictionary<string, List<Vector2>> options = subset.ToDictionary(
                    candidate => candidate.Member.NpcName,
                    candidate => baseOptions[candidate.Member.NpcName]
                        .Where(tile => !this.IsContextStandTileReserved(
                            location,
                            tile,
                            candidate.Member.NpcName,
                            replaceableWorkers))
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
                if (options.Any(pair => pair.Value.Count == 0))
                    continue;

                Dictionary<string, Vector2> assignments = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> usedStandKeys = new(StringComparer.OrdinalIgnoreCase);
                List<(SquadMemberState Member, NPC Npc, int Order)> constrainedFirst = subset
                    .OrderBy(candidate => options[candidate.Member.NpcName].Count)
                    .ThenBy(candidate => candidate.Order)
                    .ToList();
                if (!this.TryAssignContextStandTiles(
                        location.NameOrUniqueName,
                        constrainedFirst,
                        options,
                        workerIndex: 0,
                        assignments,
                        usedStandKeys))
                {
                    continue;
                }

                return subset
                    .OrderBy(candidate => candidate.Order)
                    .Select(candidate => new ContextWorkerPlan(
                        candidate.Member,
                        candidate.Npc,
                        assignments[candidate.Member.NpcName]))
                    .ToList();
            }
        }

        return new List<ContextWorkerPlan>();
    }

    private static int CountSetBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private bool TryAssignContextStandTiles(
        string locationName,
        IReadOnlyList<(SquadMemberState Member, NPC Npc, int Order)> workers,
        IReadOnlyDictionary<string, List<Vector2>> options,
        int workerIndex,
        IDictionary<string, Vector2> assignments,
        ISet<string> usedStandKeys)
    {
        if (workerIndex >= workers.Count)
            return true;

        string npcName = workers[workerIndex].Member.NpcName;
        foreach (Vector2 standTile in options[npcName])
        {
            string key = this.GetWorkTargetKey(locationName, standTile);
            if (!usedStandKeys.Add(key))
                continue;

            assignments[npcName] = standTile;
            if (this.TryAssignContextStandTiles(
                    locationName,
                    workers,
                    options,
                    workerIndex + 1,
                    assignments,
                    usedStandKeys))
            {
                return true;
            }

            assignments.Remove(npcName);
            usedStandKeys.Remove(key);
        }

        return false;
    }

    private List<Vector2> GetContextStandOptions(
        GameLocation location,
        Farmer owner,
        Vector2 targetTile,
        NPC npc)
    {
        Vector2 npcTile = NormalizeTile(npc.Tile);
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        List<Vector2> options = new();
        Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(
            location,
            npcTile,
            MaxFollowReachabilitySearchTiles);

        if (Vector2.Distance(npcTile, targetTile) <= TaskArrivalDistance
            && IsWithinOwnerDistance(ownerTile, npcTile, MaxCompanionDistanceTiles))
        {
            options.Add(npcTile);
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
            .Where(candidate => !options.Contains(candidate))
            .Where(candidate => IsWithinOwnerDistance(ownerTile, candidate, MaxCompanionDistanceTiles))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => IsReachableOrUncertain(reachableDistances, candidate, MaxFollowReachabilitySearchTiles))
            .OrderBy(candidate => reachableDistances.TryGetValue(candidate, out int pathDistance) ? pathDistance : int.MaxValue)
            .ThenBy(candidate => Vector2.Distance(candidate, npcTile)))
        {
            options.Add(candidate);
        }

        return options;
    }

    private bool IsContextStandTileReserved(
        GameLocation location,
        Vector2 tile,
        string npcName,
        ISet<string> replaceableWorkers)
    {
        string key = this.GetWorkTargetKey(location.NameOrUniqueName, tile);
        if (this.workStandReservations.TryGetValue(key, out string? workOwner)
            && !string.Equals(workOwner, npcName, StringComparison.OrdinalIgnoreCase)
            && !replaceableWorkers.Contains(workOwner))
        {
            return true;
        }

        return this.followDestinationsThisUpdate.TryGetValue(key, out string? followOwner)
            && !string.Equals(followOwner, npcName, StringComparison.OrdinalIgnoreCase)
            && !replaceableWorkers.Contains(followOwner);
    }
}
