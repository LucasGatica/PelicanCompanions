using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int WorkAreaPreviewDurationTicks = 360;
    private readonly Dictionary<long, WorkAreaPreviewState> workAreaPreviews = new();

    private readonly record struct WorkAreaPreviewState(
        string LocationName,
        Vector2 Center,
        int Radius,
        CompanionWorkSpecialty Specialty,
        int StartedTick,
        string CommandId);

    private bool HasActiveWorkArea(SquadMemberState member)
    {
        return CompanionWorkAreaPolicy.IsActiveStateValid(
            member.WorkAreaActive,
            member.WorkAreaOrderId,
            member.WorkAreaLocationName,
            member.WorkAreaRegionKind,
            member.WorkAreaCenterX,
            member.WorkAreaCenterY,
            member.WorkAreaRadius,
            member.WorkAreaMinX,
            member.WorkAreaMinY,
            member.WorkAreaSize,
            member.WorkAreaSpecialty);
    }

    private bool HasUsableEquipmentForWorkArea(
        SquadMemberState member,
        CompanionWorkSpecialty specialty)
    {
        return CompanionEquipmentPolicy.CanWorkSpecialty(
            specialty,
            lumberingEnabled: this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled,
            miningEnabled: this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled,
            wateringEnabled: this.GetConfiguredTaskMode(CompanionTaskKind.Watering) != TaskMode.Disabled,
            hasUsableAxe: this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Lumbering),
            hasUsablePickaxe: this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Mining),
            hasUsableWateringCan: this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Watering));
    }

    private string GetWorkAreaEquipmentWarningKey(
        CompanionWorkSpecialty specialty,
        IEnumerable<SquadMemberState>? candidates = null)
    {
        if (specialty == CompanionWorkSpecialty.Watering
            && candidates?.Any(this.HasEmptyCompanionWateringCan) == true)
        {
            return "tasks.watering_can_empty";
        }

        return specialty switch
        {
            CompanionWorkSpecialty.Wood => "tasks.need_axe",
            CompanionWorkSpecialty.Mining => "tasks.need_pickaxe",
            CompanionWorkSpecialty.Watering => "tasks.need_watering_can",
            _ => "wheel.no_worker_available"
        };
    }

    private Vector2 GetWorkAreaCenter(SquadMemberState member)
    {
        return NormalizeTile(new Vector2(member.WorkAreaCenterX, member.WorkAreaCenterY));
    }

    private int GetWorkAreaSearchRadius(SquadMemberState member, GameLocation location)
    {
        return member.WorkAreaRegionKind switch
        {
            CompanionWorkRegionKind.DelimitedSquare => Math.Max(1, member.WorkAreaSize * 2 + 1),
            CompanionWorkRegionKind.FarmWide => location.Map is not null && location.Map.Layers.Count > 0
                ? Math.Max(1, location.Map.Layers[0].LayerWidth + location.Map.Layers[0].LayerHeight)
                : CompanionWorkAreaPolicy.MaximumRadius,
            _ => member.WorkAreaRadius
        };
    }

    private bool IsTileInsideWorkAreaRegion(
        SquadMemberState member,
        GameLocation location,
        Vector2 rawTile,
        int padding = 0)
    {
        if (location.Map is null || location.Map.Layers.Count == 0)
            return false;

        Vector2 tile = NormalizeTile(rawTile);
        return CompanionWorkAreaPolicy.ContainsRegion(
            member.WorkAreaRegionKind,
            member.WorkAreaCenterX,
            member.WorkAreaCenterY,
            member.WorkAreaRadius,
            member.WorkAreaMinX,
            member.WorkAreaMinY,
            member.WorkAreaSize,
            (int)tile.X,
            (int)tile.Y,
            location.Map.Layers[0].LayerWidth,
            location.Map.Layers[0].LayerHeight,
            padding);
    }

    private bool HasPendingWorkAreaRecovery(SquadMemberState member)
    {
        return this.workAreaPositionRecoveryNeeded.Contains(member.NpcName)
            || (this.HasActiveWorkArea(member)
                && member.LastFailureReasonKey == "companion.task_failure.work_area_unavailable");
    }

    private void EnforceConfiguredWorkAreaRadius()
    {
        int maximumRadius = CompanionWorkAreaPolicy.NormalizeRadius(this.GetConfiguredWorkRadius());
        bool changed = false;
        foreach (SquadMemberState member in this.members.Values.Where(this.HasActiveWorkArea).ToList())
        {
            if (member.WorkAreaRegionKind != CompanionWorkRegionKind.Circle)
                continue;

            // Preserve the exact boundary of an existing saved order when the
            // host raises the setting; only clamp orders which now exceed it.
            if (member.WorkAreaRadius <= maximumRadius)
                continue;

            member.WorkAreaRadius = maximumRadius;
            if (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task)
                && task.UsesFixedWorkArea)
            {
                this.RemovePendingTask(task);
            }
            if (member.Mode == CompanionMode.Following)
            {
                this.SetCompanionActivity(member, "companion.status.work_area");
                this.priorityTaskPlanningMembers.Add(member.NpcName);
            }
            changed = true;
        }

        if (!changed)
            return;

        this.nextTaskScanTick = Game1.ticks + 1;
        this.InvalidateTargetPreviews();
        this.MarkStateDirty();
    }

    private void RequestWorkArea(
        string? npcName,
        string locationName,
        Vector2 rawCenter,
        CompanionWorkSpecialty specialty)
    {
        Vector2 center = NormalizeTile(rawCenter);
        int radius = CompanionWorkAreaPolicy.NormalizeRadius(this.GetConfiguredWorkRadius());
        if (!Context.IsMainPlayer)
        {
            string commandId = this.SendActionRequest(
                "SetWorkArea",
                npcName ?? "",
                specialty.ToString(),
                center,
                expectedLocationName: locationName);
            this.workAreaPreviews[Game1.player.UniqueMultiplayerID] = new WorkAreaPreviewState(
                locationName,
                center,
                radius,
                specialty,
                Game1.ticks,
                commandId);
            return;
        }

        this.workAreaPreviews[Game1.player.UniqueMultiplayerID] = new WorkAreaPreviewState(
            locationName,
            center,
            radius,
            specialty,
            Game1.ticks,
            "");
        this.TryAssignWorkArea(
            Game1.player.UniqueMultiplayerID,
            npcName,
            locationName,
            center,
            specialty);
    }

    private void TryAssignWorkArea(
        long ownerId,
        string? requestedNpcName,
        string locationName,
        Vector2 rawCenter,
        CompanionWorkSpecialty specialty)
    {
        // The host replaces a locally optimistic preview with either the
        // authoritative persisted outline or an immediate rejection. Remote
        // players clear theirs when the scoped command feedback arrives.
        if (ownerId == Game1.player.UniqueMultiplayerID)
            this.workAreaPreviews.Remove(ownerId);
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        GameLocation? location = owner?.currentLocation;
        Vector2 center = NormalizeTile(rawCenter);
        int radius = CompanionWorkAreaPolicy.NormalizeRadius(this.GetConfiguredWorkRadius());
        if (!this.AreTaskActionsSafe(ownerId)
            || owner is null
            || location is null
            || !Enum.IsDefined(specialty)
            || !string.Equals(location.NameOrUniqueName, locationName, StringComparison.Ordinal)
            || !this.IsGroundCommandTileAvailable(location, center))
        {
            this.WarnForPlayer(ownerId, "work_area.invalid");
            return;
        }

        bool allowsWood = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Lumbering)
            && this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled;
        bool allowsMining = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Mining)
            && this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled;
        bool allowsWatering = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Watering)
            && this.GetConfiguredTaskMode(CompanionTaskKind.Watering) != TaskMode.Disabled;
        if (!allowsWood && !allowsMining && !allowsWatering)
        {
            this.WarnForPlayer(ownerId, "work_area.modes_disabled");
            return;
        }

        List<SquadMemberState> nearbyCandidates = this.members.Values
            .Where(member => member.OwnerId == ownerId && member.Mode != CompanionMode.ParkedForDisconnect)
            .Where(member => string.IsNullOrWhiteSpace(requestedNpcName)
                || string.Equals(member.NpcName, requestedNpcName, StringComparison.OrdinalIgnoreCase))
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => candidate.Npc?.currentLocation == location)
            .OrderBy(candidate => Vector2.DistanceSquared(NormalizeTile(candidate.Npc!.Tile), center))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member)
            .ToList();
        if (nearbyCandidates.Count == 0)
        {
            this.WarnForPlayer(ownerId, "commands.no_followers");
            return;
        }

        if (!this.HasMatchingWorkAreaTarget(location, center, radius, specialty, includeReserved: true))
        {
            this.WarnForPlayer(ownerId, "work_area.empty");
            return;
        }

        List<SquadMemberState> equippedCandidates = nearbyCandidates
            .Where(member => this.HasUsableEquipmentForWorkArea(member, specialty))
            .ToList();
        if (equippedCandidates.Count == 0)
        {
            this.WarnForPlayer(ownerId, this.GetWorkAreaEquipmentWarningKey(specialty, nearbyCandidates));
            return;
        }

        List<SquadMemberState> candidates = equippedCandidates
            .Where(member => this.HasMatchingWorkAreaTargetForMember(
                member,
                location,
                center,
                radius,
                specialty,
                includeReserved: true))
            .ToList();
        if (candidates.Count == 0)
        {
            this.WarnForPlayer(ownerId, specialty == CompanionWorkSpecialty.ClearArea
                ? "work_area.equipment_mismatch"
                : this.GetWorkAreaEquipmentWarningKey(specialty, nearbyCandidates));
            return;
        }

        if (!this.AreTasksEnabled(ownerId))
            this.taskToggles[ownerId] = true;

        string orderId = Guid.NewGuid().ToString("N");
        foreach (SquadMemberState member in candidates)
        {
            this.RemovePendingTask(member.NpcName);
            this.ClearFollowState(member.NpcName);
            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
                this.StopCompanionMovement(npc);

            member.Mode = CompanionMode.Following;
            member.ParkedAtUtcTicks = 0;
            member.WaitingLocationName = null;
            member.SearchWood = false;
            member.SearchMining = false;
            member.SearchWatering = false;
            member.ClearArea = false;
            member.WorkAreaActive = true;
            member.WorkAreaOrderId = orderId;
            member.WorkAreaLocationName = location.NameOrUniqueName;
            member.WorkAreaCenterX = (int)center.X;
            member.WorkAreaCenterY = (int)center.Y;
            member.WorkAreaRadius = radius;
            member.WorkAreaRegionKind = CompanionWorkRegionKind.Circle;
            member.WorkAreaMinX = -1;
            member.WorkAreaMinY = -1;
            member.WorkAreaSize = 0;
            member.WorkAreaSpecialty = specialty;
            member.PreferredWorkSpecialty = specialty;
            this.RememberRoutineAreaPreset(member);
            this.workAreaPositionRecoveryNeeded.Remove(member.NpcName);
            this.SetTaskFailure(member, "");
            this.ClearCompanionTarget(member);
            this.SetCompanionActivity(member, "companion.status.work_area");
            this.priorityTaskPlanningMembers.Add(member.NpcName);
        }

        this.workAreaPreviews[ownerId] = new WorkAreaPreviewState(
            location.NameOrUniqueName,
            center,
            radius,
            specialty,
            Game1.ticks,
            "");
        this.nextTaskScanTick = Game1.ticks + 1;
        this.InvalidateTargetPreviews();
        this.MarkStateDirty();
        this.InfoForPlayer(
            ownerId,
            candidates.Count == 1 ? "work_area.assigned_one" : "work_area.assigned_many",
            new
            {
                npc = candidates[0].DisplayName,
                count = candidates.Count,
                specialty = this.Tr($"companion.specialty.{specialty}"),
                radius
            });
    }

    private bool HasMatchingWorkAreaTarget(
        GameLocation location,
        Vector2 center,
        int radius,
        CompanionWorkSpecialty specialty,
        bool includeReserved,
        bool respectConfiguredModes = true,
        Func<Vector2, bool>? containsTarget = null)
    {
        center = NormalizeTile(center);
        bool IsInside(Vector2 tile)
        {
            return containsTarget?.Invoke(tile) == true
                || containsTarget is null
                && CompanionWorkAreaPolicy.Contains(
                    (int)center.X,
                    (int)center.Y,
                    radius,
                    (int)tile.X,
                    (int)tile.Y);
        }

        bool allowsWood = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Lumbering)
            && (!respectConfiguredModes
                || this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled);
        if (allowsWood && location.terrainFeatures.Keys.Any(tile =>
                IsInside(tile)
                && this.IsValidWoodTarget(location, tile)
                && (includeReserved || !this.IsTargetReserved(location, tile))))
        {
            return true;
        }

        bool allowsMining = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Mining)
            && (!respectConfiguredModes
                || this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled);
        if (allowsMining && location.Objects.Keys.Any(tile =>
                IsInside(tile)
                && this.IsValidMiningTarget(location, tile)
                && (includeReserved || !this.IsTargetReserved(location, tile))))
        {
            return true;
        }

        bool allowsWatering = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Watering)
            && (!respectConfiguredModes
                || this.GetConfiguredTaskMode(CompanionTaskKind.Watering) != TaskMode.Disabled);
        return allowsWatering && this.GetWateringTargetTiles(location).Any(tile =>
            IsInside(tile)
            && (includeReserved || !this.IsTargetReserved(location, tile)));
    }

    private bool HasMatchingWorkAreaTargetForMember(
        SquadMemberState member,
        GameLocation location,
        Vector2 center,
        int radius,
        CompanionWorkSpecialty specialty,
        bool includeReserved,
        bool respectConfiguredModes = true,
        Func<Vector2, bool>? containsTarget = null)
    {
        if (CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Lumbering)
            && this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Lumbering)
            && this.HasMatchingWorkAreaTarget(
                location,
                center,
                radius,
                CompanionWorkSpecialty.Wood,
                includeReserved,
                respectConfiguredModes,
                containsTarget))
        {
            return true;
        }

        if (CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Mining)
            && this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Mining)
            && this.HasMatchingWorkAreaTarget(
                location,
                center,
                radius,
                CompanionWorkSpecialty.Mining,
                includeReserved,
                respectConfiguredModes,
                containsTarget))
        {
            return true;
        }

        return CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Watering)
            && this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Watering)
            && this.HasMatchingWorkAreaTarget(
                location,
                center,
                radius,
                CompanionWorkSpecialty.Watering,
                includeReserved,
                respectConfiguredModes,
                containsTarget);
    }

    private string GetMissingWorkAreaEquipmentFailureKey(
        SquadMemberState member,
        GameLocation location,
        Vector2 center,
        int radius,
        CompanionWorkSpecialty specialty,
        Func<Vector2, bool>? containsTarget = null)
    {
        if (CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Lumbering)
            && !this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Lumbering)
            && this.HasMatchingWorkAreaTarget(
                location,
                center,
                radius,
                CompanionWorkSpecialty.Wood,
                includeReserved: true,
                respectConfiguredModes: true,
                containsTarget: containsTarget))
        {
            return "companion.task_failure.need_axe";
        }

        if (CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Mining)
            && !this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Mining)
            && this.HasMatchingWorkAreaTarget(
                location,
                center,
                radius,
                CompanionWorkSpecialty.Mining,
                includeReserved: true,
                respectConfiguredModes: true,
                containsTarget: containsTarget))
        {
            return "companion.task_failure.need_pickaxe";
        }

        if (CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Watering)
            && !this.HasUsableCompanionToolForTask(member, CompanionTaskKind.Watering)
            && this.HasMatchingWorkAreaTarget(
                location,
                center,
                radius,
                CompanionWorkSpecialty.Watering,
                includeReserved: true,
                respectConfiguredModes: true,
                containsTarget: containsTarget))
        {
            return this.HasEmptyCompanionWateringCan(member)
                ? "companion.task_failure.watering_can_empty"
                : "companion.task_failure.need_watering_can";
        }

        return "companion.task_failure.work_area_blocked";
    }

    private bool IsTaskInsideWorkArea(SquadMemberState member, CompanionTaskKind kind, string locationName, Vector2 tile)
    {
        GameLocation? location = Game1.getLocationFromName(locationName);
        return location is not null
            && this.HasActiveWorkArea(member)
            && string.Equals(member.WorkAreaLocationName, locationName, StringComparison.Ordinal)
            && CompanionWorkAreaPolicy.Allows(member.WorkAreaSpecialty, kind)
            && this.IsTileInsideWorkAreaRegion(member, location, tile);
    }

    private void ClearCompanionWorkArea(SquadMemberState member, bool cancelPendingAreaTask)
    {
        this.workAreaPositionRecoveryNeeded.Remove(member.NpcName);
        if (!member.WorkAreaActive)
            return;

        string oldOrderId = member.WorkAreaOrderId;
        ClearPersistedWorkArea(member);
        if (cancelPendingAreaTask
            && this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task)
            && task.UsesFixedWorkArea
            && string.Equals(task.FixedWorkAreaOrderId, oldOrderId, StringComparison.Ordinal))
        {
            this.RemovePendingTask(task);
        }

        this.priorityTaskPlanningMembers.Remove(member.NpcName);
        this.InvalidateTargetPreviews();
        this.MarkStateDirty();
    }

    private void CompleteCompanionWorkArea(SquadMemberState member, NPC npc)
    {
        if (!this.HasActiveWorkArea(member))
            return;

        CompanionWorkSpecialty completedSpecialty = member.WorkAreaSpecialty;
        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.ClearCompanionTarget(member);
        member.SearchWood = false;
        member.SearchMining = false;
        member.SearchWatering = false;
        member.ClearArea = false;
        this.SetTaskResult(member, "companion.task_result.work_area_complete");
        this.ShowCompanionWorkSignal(npc, npc.currentLocation, npc.Tile, "success");
        npc.doEmote(32);
        this.Say(npc, "WorkAreaComplete", force: false);

        // A manual area of the currently scheduled specialty may have been
        // used as an explicit override while that block waited for its preset.
        // Completing it satisfies the same claimed routine block exactly once.
        if (!this.TryHandleRoutineWorkAreaCompletion(member, completedSpecialty))
        {
            this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
            member.Mode = CompanionMode.Waiting;
            this.StoreWaitingPosition(member, npc);
            this.SetCompanionActivity(member, "companion.status.waiting");
            this.StopCompanionMovement(npc);
            this.ClearFollowState(member.NpcName);
        }

        this.MarkStateDirty();
        this.InfoForPlayer(member.OwnerId, "work_area.complete", new { npc = member.DisplayName });
    }

    private void DrawCompanionWorkAreas(SpriteBatch b)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null)
            return;

        long localOwnerId = Game1.player.UniqueMultiplayerID;
        string currentLocationName = Game1.currentLocation.NameOrUniqueName;
        foreach (SquadMemberState area in this.members.Values
            .Where(member => member.OwnerId == localOwnerId
                && this.HasActiveWorkArea(member)
                && string.Equals(member.WorkAreaLocationName, currentLocationName, StringComparison.Ordinal))
            .GroupBy(member => string.IsNullOrWhiteSpace(member.WorkAreaOrderId)
                ? $"{member.WorkAreaRegionKind}:{member.WorkAreaCenterX}:{member.WorkAreaCenterY}:{member.WorkAreaRadius}:{member.WorkAreaMinX}:{member.WorkAreaMinY}:{member.WorkAreaSize}:{member.WorkAreaSpecialty}"
                : member.WorkAreaOrderId,
                StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            float alpha = area.Mode == CompanionMode.Waiting ? 0.13f : 0.2f;
            if (area.WorkAreaRegionKind == CompanionWorkRegionKind.DelimitedSquare)
            {
                this.DrawRectangularWorkAreaOutline(
                    b,
                    area.WorkAreaMinX,
                    area.WorkAreaMinY,
                    area.WorkAreaSize,
                    area.WorkAreaSize,
                    area.WorkAreaSpecialty,
                    alpha);
            }
            else if (area.WorkAreaRegionKind == CompanionWorkRegionKind.FarmWide
                && Game1.currentLocation.Map is not null
                && Game1.currentLocation.Map.Layers.Count > 0)
            {
                this.DrawRectangularWorkAreaOutline(
                    b,
                    0,
                    0,
                    Game1.currentLocation.Map.Layers[0].LayerWidth,
                    Game1.currentLocation.Map.Layers[0].LayerHeight,
                    area.WorkAreaSpecialty,
                    alpha);
            }
            else
            {
                this.DrawWorkAreaOutline(
                    b,
                    this.GetWorkAreaCenter(area),
                    area.WorkAreaRadius,
                    area.WorkAreaSpecialty,
                    alpha);
            }
        }

        if (!this.workAreaPreviews.TryGetValue(localOwnerId, out WorkAreaPreviewState preview))
            return;
        if (Game1.ticks - preview.StartedTick > WorkAreaPreviewDurationTicks)
        {
            this.workAreaPreviews.Remove(localOwnerId);
            return;
        }
        if (!string.Equals(currentLocationName, preview.LocationName, StringComparison.Ordinal))
            return;

        float remaining = 1f - Math.Clamp(
            (Game1.ticks - preview.StartedTick) / (float)WorkAreaPreviewDurationTicks,
            0f,
            1f);
        this.DrawWorkAreaOutline(
            b,
            preview.Center,
            preview.Radius,
            preview.Specialty,
            alpha: 0.25f + remaining * 0.45f);
    }

    private void DrawWorkAreaOutline(
        SpriteBatch b,
        Vector2 center,
        int radius,
        CompanionWorkSpecialty specialty,
        float alpha)
    {
        Color accent = specialty switch
        {
            CompanionWorkSpecialty.Wood => new Color(108, 166, 91),
            CompanionWorkSpecialty.Mining => new Color(126, 139, 175),
            CompanionWorkSpecialty.Watering => new Color(82, 158, 224),
            _ => new Color(220, 166, 74)
        };
        accent *= Math.Clamp(alpha, 0f, 1f);

        center = NormalizeTile(center);
        radius = CompanionWorkAreaPolicy.NormalizeRadius(radius);
        int centerX = (int)center.X;
        int centerY = (int)center.Y;
        for (int x = centerX - radius; x <= centerX + radius; x++)
        {
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (!CompanionWorkAreaPolicy.Contains(centerX, centerY, radius, x, y))
                    continue;

                bool boundary = !CompanionWorkAreaPolicy.Contains(centerX, centerY, radius, x + 1, y)
                    || !CompanionWorkAreaPolicy.Contains(centerX, centerY, radius, x - 1, y)
                    || !CompanionWorkAreaPolicy.Contains(centerX, centerY, radius, x, y + 1)
                    || !CompanionWorkAreaPolicy.Contains(centerX, centerY, radius, x, y - 1);
                if (!boundary && (x != centerX || y != centerY))
                    continue;

                Vector2 screen = Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, y * 64f));
                Rectangle tileBounds = new((int)screen.X, (int)screen.Y, 64, 64);
                b.Draw(Game1.staminaRect, tileBounds, accent * (boundary ? 0.32f : 0.55f));
                if (boundary)
                {
                    const int border = 2;
                    b.Draw(Game1.staminaRect, new Rectangle(tileBounds.X, tileBounds.Y, tileBounds.Width, border), accent);
                    b.Draw(Game1.staminaRect, new Rectangle(tileBounds.X, tileBounds.Bottom - border, tileBounds.Width, border), accent);
                    b.Draw(Game1.staminaRect, new Rectangle(tileBounds.X, tileBounds.Y, border, tileBounds.Height), accent);
                    b.Draw(Game1.staminaRect, new Rectangle(tileBounds.Right - border, tileBounds.Y, border, tileBounds.Height), accent);
                }
            }
        }
    }

    private void DrawRectangularWorkAreaOutline(
        SpriteBatch b,
        int minX,
        int minY,
        int width,
        int height,
        CompanionWorkSpecialty specialty,
        float alpha)
    {
        if (minX < 0 || minY < 0 || width <= 0 || height <= 0)
            return;

        Color accent = specialty switch
        {
            CompanionWorkSpecialty.Wood => new Color(108, 166, 91),
            CompanionWorkSpecialty.Mining => new Color(126, 139, 175),
            CompanionWorkSpecialty.Watering => new Color(82, 158, 224),
            _ => new Color(220, 166, 74)
        };
        accent *= Math.Clamp(alpha, 0f, 1f);

        Vector2 screen = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(minX * 64f, minY * 64f));
        Rectangle bounds = new(
            (int)screen.X,
            (int)screen.Y,
            checked(width * 64),
            checked(height * 64));
        const int border = 4;
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, bounds.Width, border), accent);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Bottom - border, bounds.Width, border), accent);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, border, bounds.Height), accent);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.Right - border, bounds.Y, border, bounds.Height), accent);
    }
}
