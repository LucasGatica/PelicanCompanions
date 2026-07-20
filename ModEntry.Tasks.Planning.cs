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
    private bool HasActiveWorkDirective(SquadMemberState member)
    {
        return member.SearchWood || member.SearchMining || member.ClearArea;
    }

    private bool IsPendingTaskAllowedByDirectives(SquadMemberState member, CompanionTaskKind kind)
    {
        return kind switch
        {
            CompanionTaskKind.Lumbering => member.SearchWood || member.ClearArea,
            CompanionTaskKind.Mining => member.SearchMining || member.ClearArea,
            _ => true
        };
    }

    private void CancelPendingTasksForOwner(long ownerId, string failureKey, bool includeMovementOrders = true)
    {
        foreach (PendingCompanionTask task in this.pendingTasks.Values
            .Where(task => this.members.TryGetValue(task.NpcName, out SquadMemberState? member) && member.OwnerId == ownerId)
            .Where(task => includeMovementOrders || task.Kind != CompanionTaskKind.MovingToWait)
            .ToList())
        {
            this.RemovePendingTask(task, failureKey, returning: true);
        }
    }

    private void ApplyPreferredWorkSpecialty(SquadMemberState member)
    {
        member.SearchWood = member.PreferredWorkSpecialty == CompanionWorkSpecialty.Wood;
        member.SearchMining = member.PreferredWorkSpecialty == CompanionWorkSpecialty.Mining;
        member.ClearArea = member.PreferredWorkSpecialty == CompanionWorkSpecialty.ClearArea;
        this.InvalidateTargetPreviews();
    }

    private bool TryAssignWorkDirectiveTask(SquadMemberState member)
    {
        NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null || owner.currentLocation is null)
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_unavailable");
            return false;
        }

        GameLocation location = owner.currentLocation;
        if (npc.currentLocation != location)
        {
            this.SetCompanionActivity(member, "companion.status.returning");
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            return false;
        }

        int radius = this.GetCompanionWorkRadius(member);
        bool includeWood = (member.SearchWood || member.ClearArea)
            && this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled;
        bool includeMining = (member.SearchMining || member.ClearArea)
            && this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled;
        WorkTarget? target = this.FindBestWorkTarget(member, npc, owner, location, radius);
        this.UpdateTargetPreview(
            member,
            target is WorkTarget plannedTarget
                ? CreateWorkTargetPreview(plannedTarget)
                : new TargetPreview(
                    false,
                    "",
                    -1,
                    -1,
                    includeWood || includeMining
                        ? "companion.preview.no_target"
                        : "companion.preview.work_modes_disabled"));
        if (target is null)
        {
            this.SetCompanionActivity(member, "companion.status.following");
            this.ClearCompanionTarget(member);
            return false;
        }

        return target.Value.Kind switch
        {
            CompanionTaskKind.Lumbering => this.TryQueueDirectiveLumberTask(
                location,
                target.Value.Tile,
                member,
                npc,
                radius,
                preparedStandTile: target.Value.StandTile),
            CompanionTaskKind.Mining => this.TryQueueDirectiveMiningTask(
                location,
                target.Value.Tile,
                member,
                npc,
                radius,
                preparedStandTile: target.Value.StandTile),
            _ => false
        };
    }

    private bool TryAssignConfiguredAutonomousTask(SquadMemberState member)
    {
        bool includeWood = this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) == TaskMode.Autonomous;
        bool includeMining = this.GetConfiguredTaskMode(CompanionTaskKind.Mining) == TaskMode.Autonomous;
        if (!includeWood && !includeMining)
            return false;

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner?.currentLocation is null || npc.currentLocation != owner.currentLocation)
            return false;

        int radius = this.GetCompanionWorkRadius(member);
        WorkTarget? target = this.FindBestWorkTarget(member, npc, owner, owner.currentLocation, radius, includeWood, includeMining);
        if (target is null)
            return false;

        bool queued = target.Value.Kind switch
        {
            CompanionTaskKind.Lumbering => this.TryQueueDirectiveLumberTask(
                owner.currentLocation,
                target.Value.Tile,
                member,
                npc,
                radius,
                preparedStandTile: target.Value.StandTile),
            CompanionTaskKind.Mining => this.TryQueueDirectiveMiningTask(
                owner.currentLocation,
                target.Value.Tile,
                member,
                npc,
                radius,
                preparedStandTile: target.Value.StandTile),
            _ => false
        };

        if (queued && this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task))
        {
            task.UsesWorkDirective = false;
            task.UsesConfiguredAutonomy = true;
        }

        return queued;
    }

    private WorkTarget? FindBestWorkTarget(SquadMemberState member, NPC npc, Farmer owner, GameLocation location, int radius)
    {
        bool includeWood = (member.SearchWood || member.ClearArea) && this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled;
        bool includeMining = (member.SearchMining || member.ClearArea) && this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled;
        return this.FindBestWorkTarget(member, npc, owner, location, radius, includeWood, includeMining);
    }

    private WorkTarget? FindBestWorkTarget(SquadMemberState member, NPC npc, Farmer owner, GameLocation location, int radius, bool includeWood, bool includeMining)
    {
        if (!includeWood && !includeMining)
            return null;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Dictionary<Vector2, List<WorkTarget>> targetsByStandTile = new();

        void TryAddTarget(CompanionTaskKind kind, Vector2 rawTile)
        {
            Vector2 tile = NormalizeTile(rawTile);
            float playerDistance = Vector2.Distance(ownerTile, tile);
            if (playerDistance > radius)
                return;

            if (this.IsTargetReserved(location, tile))
                return;

            float npcDistance = Vector2.Distance(npcTile, tile);
            foreach (Vector2 standTile in this.GetCandidateTaskStandTiles(
                location,
                tile,
                npc,
                member,
                radius))
            {
                if (!targetsByStandTile.TryGetValue(standTile, out List<WorkTarget>? standTargets))
                {
                    standTargets = new List<WorkTarget>();
                    targetsByStandTile[standTile] = standTargets;
                }

                standTargets.Add(new WorkTarget(kind, tile, standTile, npcDistance, playerDistance));
            }
        }

        // Enumerate actual world features instead of walking every tile in the
        // search square. All viable stands become goals of one early-exit BFS,
        // so nearby work doesn't pay for a full-map reachability flood.
        if (includeWood)
        {
            foreach (Vector2 tile in location.terrainFeatures.Keys)
            {
                if (this.IsValidWoodTarget(location, tile))
                    TryAddTarget(CompanionTaskKind.Lumbering, tile);
            }
        }

        if (includeMining)
        {
            foreach (Vector2 tile in location.Objects.Keys)
            {
                if (this.IsValidMiningTarget(location, tile))
                    TryAddTarget(CompanionTaskKind.Mining, tile);
            }
        }

        if (targetsByStandTile.Count == 0)
            return null;

        HashSet<Vector2> candidateStandTiles = targetsByStandTile.Keys.ToHashSet();
        ReachabilitySearchResult reachability = this.SearchReachableGoal(
            location,
            npcTile,
            candidateStandTiles,
            candidateStandTiles,
            MaxFollowReachabilitySearchTiles,
            out Vector2 reachedStandTile,
            out _);
        if (reachability != ReachabilitySearchResult.Reachable
            || !targetsByStandTile.TryGetValue(reachedStandTile, out List<WorkTarget>? reachedTargets))
        {
            return null;
        }

        return reachedTargets
            .OrderBy(p => p.NpcDistance)
            .ThenBy(p => p.PlayerDistance)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Tile.X)
            .ThenBy(p => p.Tile.Y)
            .FirstOrDefault();
    }

    private static TargetPreview CreateWorkTargetPreview(WorkTarget target)
    {
        string targetKey = target.Kind switch
        {
            CompanionTaskKind.Lumbering => "companion.target.wood",
            CompanionTaskKind.Mining => "companion.target.mining",
            _ => ""
        };
        return new TargetPreview(true, targetKey, (int)target.Tile.X, (int)target.Tile.Y, "");
    }

    private TargetPreview BuildTargetPreview(SquadMemberState member, CompanionDirective? simulatedDirective)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        NPC? npc = this.GetNpcByName(member.NpcName);
        string locationName = owner?.currentLocation?.NameOrUniqueName ?? "";
        Vector2 ownerTile = owner is null ? new Vector2(-1f, -1f) : NormalizeTile(owner.Tile);
        Vector2 npcTile = npc is null ? new Vector2(-1f, -1f) : NormalizeTile(npc.Tile);
        bool tasksEnabled = this.AreTasksEnabled(member.OwnerId);
        bool blocked = this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: false);
        TargetPreviewCacheKey cacheKey = new(
            member.NpcName,
            simulatedDirective,
            locationName,
            (int)ownerTile.X,
            (int)ownerTile.Y,
            (int)npcTile.X,
            (int)npcTile.Y,
            member.SearchWood,
            member.SearchMining,
            member.ClearArea,
            tasksEnabled,
            blocked,
            member.Mode);

        if (this.targetPreviewCache.Count > 256)
        {
            foreach (TargetPreviewCacheKey staleKey in this.targetPreviewCache
                .Where(p => Game1.ticks - p.Value.Tick >= 60)
                .Select(p => p.Key)
                .ToList())
            {
                this.targetPreviewCache.Remove(staleKey);
            }
        }

        if (this.targetPreviewCache.TryGetValue(cacheKey, out TargetPreviewCacheEntry cached)
            && Game1.ticks - cached.Tick < 60)
        {
            return cached.Preview;
        }

        TargetPreview preview = this.BuildTargetPreviewCore(member, simulatedDirective);
        this.targetPreviewCache[cacheKey] = new TargetPreviewCacheEntry(Game1.ticks, preview);
        return preview;
    }

    private TargetPreview BuildTargetPreviewCore(SquadMemberState member, CompanionDirective? simulatedDirective)
    {
        if (!this.AreTasksEnabled(member.OwnerId))
            return new TargetPreview(false, "", -1, -1, "companion.preview.tasks_disabled");

        if (this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: false))
            return new TargetPreview(false, "", -1, -1, "companion.preview.blocked");

        if (member.Mode != CompanionMode.Following)
            return new TargetPreview(false, "", -1, -1, "companion.preview.not_following");

        NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null || owner.currentLocation is null)
            return new TargetPreview(false, "", -1, -1, "companion.preview.no_owner");

        GameLocation location = owner.currentLocation;
        if (npc.currentLocation != location)
            return new TargetPreview(false, "", -1, -1, "companion.preview.other_location");

        bool searchWood = member.SearchWood;
        bool searchMining = member.SearchMining;
        bool clearArea = member.ClearArea;
        if (simulatedDirective.HasValue)
        {
            switch (simulatedDirective.Value)
            {
                case CompanionDirective.SearchWood:
                    searchWood = !searchWood;
                    break;

                case CompanionDirective.SearchMining:
                    searchMining = !searchMining;
                    break;

                case CompanionDirective.ClearArea:
                    clearArea = !clearArea;
                    break;
            }
        }

        bool includeWood = (searchWood || clearArea) && this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled;
        bool includeMining = (searchMining || clearArea) && this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled;
        if (!includeWood && !includeMining)
        {
            bool requestedWork = searchWood || searchMining || clearArea;
            string reason = requestedWork
                ? "companion.preview.work_modes_disabled"
                : simulatedDirective.HasValue
                    ? "companion.preview.disabled_after_click"
                    : "companion.preview.inactive";
            return new TargetPreview(false, "", -1, -1, reason);
        }

        WorkTarget? target = this.FindBestWorkTarget(member, npc, owner, location, this.GetCompanionWorkRadius(member), includeWood, includeMining);
        if (target is null)
            return new TargetPreview(false, "", -1, -1, "companion.preview.no_target");

        return CreateWorkTargetPreview(target.Value);
    }

    private void UpdateTargetPreview(SquadMemberState member, TargetPreview preview)
    {
        member.PreviewTargetKey = preview.TargetKey;
        member.PreviewTargetX = preview.X;
        member.PreviewTargetY = preview.Y;
        member.PreviewReasonKey = preview.ReasonKey;
    }

    private void InvalidateTargetPreviews()
    {
        this.targetPreviewCache.Clear();
    }

    private string GetDirectivePreviewText(SquadMemberState member, CompanionDirective directive)
    {
        bool searchWood = member.SearchWood;
        bool searchMining = member.SearchMining;
        bool clearArea = member.ClearArea;
        switch (directive)
        {
            case CompanionDirective.SearchWood:
                searchWood = !searchWood;
                break;
            case CompanionDirective.SearchMining:
                searchMining = !searchMining;
                break;
            case CompanionDirective.ClearArea:
                clearArea = !clearArea;
                break;
        }

        string reasonKey = searchWood || searchMining || clearArea
            ? "companion.preview.planning"
            : "companion.preview.disabled_after_click";
        return this.Tr("companion.preview.reason", new { reason = this.Tr(reasonKey) });
    }

    private bool IsTargetReserved(GameLocation location, Vector2 tile)
    {
        string locationName = location.NameOrUniqueName;
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workTargetReservations.ContainsKey(key)
            || this.sharedWorkTargetReservations.ContainsKey(key))
            return true;

        if (!this.workTargetRetryAfterTicks.TryGetValue(key, out int retryAfterTick))
            return false;

        if (Game1.ticks < retryAfterTick)
            return true;

        this.workTargetRetryAfterTicks.Remove(key);
        return false;
    }

    private bool IsValidWoodTarget(GameLocation location, Vector2 tile)
    {
        return location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is Tree tree
            && tree.growthStage.Value >= 5
            && !tree.stump.Value
            && !tree.tapped.Value;
    }

    private bool IsValidMiningTarget(GameLocation location, Vector2 tile)
    {
        return location.Objects.TryGetValue(tile, out SObject? obj)
            && this.IsSafeMineableObject(obj);
    }

    private bool IsSafeMineableObject(SObject obj)
    {
        return obj.IsBreakableStone();
    }

    private bool TryQueueDirectiveLumberTask(
        GameLocation location,
        Vector2 targetTile,
        SquadMemberState member,
        NPC npc,
        int radius,
        bool manual = false,
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
        {
            return false;
        }

        if (!ignoreTaskMode && this.config.LumberingMode == TaskMode.Disabled)
            return false;

        if (!location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not Tree
            || !this.IsValidWoodTarget(location, targetTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_lost");
            return false;
        }

        Vector2 standTile;
        if (preparedStandTile is Vector2 prepared)
            standTile = NormalizeTile(prepared);
        else if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, radius, out standTile))
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
            Kind = CompanionTaskKind.Lumbering,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            Manual = manual,
            UsesWorkDirective = !manual,
            RequiresPlayerTool = false,
            IgnoresTaskMode = ignoreTaskMode,
            IgnoresTaskToggle = ignoreTaskToggle,
            ExpectedTargetToken = expectedTargetToken,
            ExpectedTargetInstance = expectedTargetInstance,
            SharedTargetGroupId = sharedTargetGroupId,
            WorkRadius = radius,
            ReturnDistance = this.GetCompanionReturnDistance(member),
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

    private bool TryQueueDirectiveMiningTask(
        GameLocation location,
        Vector2 targetTile,
        SquadMemberState member,
        NPC npc,
        int radius,
        bool manual = false,
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
        {
            return false;
        }

        if (!ignoreTaskMode && this.config.MiningMode == TaskMode.Disabled)
            return false;

        if (!location.Objects.TryGetValue(targetTile, out SObject? obj) || !this.IsSafeMineableObject(obj))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_lost");
            return false;
        }

        Vector2 standTile;
        if (preparedStandTile is Vector2 prepared)
            standTile = NormalizeTile(prepared);
        else if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, radius, out standTile))
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
            Kind = CompanionTaskKind.Mining,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            Manual = manual,
            UsesWorkDirective = !manual,
            RequiresPlayerTool = false,
            IgnoresTaskMode = ignoreTaskMode,
            IgnoresTaskToggle = ignoreTaskToggle,
            ExpectedTargetToken = expectedTargetToken,
            ExpectedTargetInstance = expectedTargetInstance,
            SharedTargetGroupId = sharedTargetGroupId,
            WorkRadius = radius,
            ReturnDistance = this.GetCompanionReturnDistance(member),
            StartedTick = Game1.ticks,
            LastProcessedTick = Game1.ticks,
            StandTile = standTile
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.working");
        this.SetCompanionTarget(member, CompanionTaskKind.Mining, targetTile);
        this.ShowCompanionWorkSignal(npc, location, targetTile, "target");
        this.Say(npc, "Mining", force: false);

        return true;
    }
}
