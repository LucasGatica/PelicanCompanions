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
            member.WorkAreaCenterX,
            member.WorkAreaCenterY,
            member.WorkAreaRadius,
            member.WorkAreaSpecialty);
    }

    private Vector2 GetWorkAreaCenter(SquadMemberState member)
    {
        return NormalizeTile(new Vector2(member.WorkAreaCenterX, member.WorkAreaCenterY));
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
        CompanionWorkSpecialty specialty,
        int radius)
    {
        Vector2 center = NormalizeTile(rawCenter);
        radius = CompanionWorkAreaPolicy.NormalizeRadius(radius);
        if (!Context.IsMainPlayer)
        {
            string commandId = this.SendActionRequest(
                "SetWorkArea",
                npcName ?? "",
                specialty.ToString(),
                center,
                index: radius,
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
            specialty,
            radius);
    }

    private void TryAssignWorkArea(
        long ownerId,
        string? requestedNpcName,
        string locationName,
        Vector2 rawCenter,
        CompanionWorkSpecialty specialty,
        int radius)
    {
        // The host replaces a locally optimistic preview with either the
        // authoritative persisted outline or an immediate rejection. Remote
        // players clear theirs when the scoped command feedback arrives.
        if (ownerId == Game1.player.UniqueMultiplayerID)
            this.workAreaPreviews.Remove(ownerId);
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        GameLocation? location = owner?.currentLocation;
        Vector2 center = NormalizeTile(rawCenter);
        if (!this.AreTaskActionsSafe(ownerId)
            || owner is null
            || location is null
            || !Enum.IsDefined(specialty)
            || radius is < CompanionWorkAreaPolicy.MinimumRadius or > CompanionWorkAreaPolicy.MaximumRadius
            || radius > this.GetConfiguredWorkRadius()
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
        if (!allowsWood && !allowsMining)
        {
            this.WarnForPlayer(ownerId, "work_area.modes_disabled");
            return;
        }

        List<SquadMemberState> candidates = this.members.Values
            .Where(member => member.OwnerId == ownerId && member.Mode != CompanionMode.ParkedForDisconnect)
            .Where(member => string.IsNullOrWhiteSpace(requestedNpcName)
                || string.Equals(member.NpcName, requestedNpcName, StringComparison.OrdinalIgnoreCase))
            .Select(member => new { Member = member, Npc = this.GetNpcByName(member.NpcName) })
            .Where(candidate => candidate.Npc?.currentLocation == location)
            .OrderBy(candidate => Vector2.DistanceSquared(NormalizeTile(candidate.Npc!.Tile), center))
            .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Member)
            .ToList();
        if (candidates.Count == 0)
        {
            this.WarnForPlayer(ownerId, "commands.no_followers");
            return;
        }

        if (!this.HasMatchingWorkAreaTarget(location, center, radius, specialty, includeReserved: true))
        {
            this.WarnForPlayer(ownerId, "work_area.empty");
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
            member.ClearArea = false;
            member.WorkAreaActive = true;
            member.WorkAreaOrderId = orderId;
            member.WorkAreaLocationName = location.NameOrUniqueName;
            member.WorkAreaCenterX = (int)center.X;
            member.WorkAreaCenterY = (int)center.Y;
            member.WorkAreaRadius = radius;
            member.WorkAreaSpecialty = specialty;
            member.PreferredWorkSpecialty = specialty;
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
        bool respectConfiguredModes = true)
    {
        center = NormalizeTile(center);
        bool allowsWood = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Lumbering)
            && (!respectConfiguredModes
                || this.GetConfiguredTaskMode(CompanionTaskKind.Lumbering) != TaskMode.Disabled);
        if (allowsWood && location.terrainFeatures.Keys.Any(tile =>
                CompanionWorkAreaPolicy.Contains((int)center.X, (int)center.Y, radius, (int)tile.X, (int)tile.Y)
                && this.IsValidWoodTarget(location, tile)
                && (includeReserved || !this.IsTargetReserved(location, tile))))
        {
            return true;
        }

        bool allowsMining = CompanionWorkAreaPolicy.Allows(specialty, CompanionTaskKind.Mining)
            && (!respectConfiguredModes
                || this.GetConfiguredTaskMode(CompanionTaskKind.Mining) != TaskMode.Disabled);
        return allowsMining && location.Objects.Keys.Any(tile =>
            CompanionWorkAreaPolicy.Contains((int)center.X, (int)center.Y, radius, (int)tile.X, (int)tile.Y)
            && this.IsValidMiningTarget(location, tile)
            && (includeReserved || !this.IsTargetReserved(location, tile)));
    }

    private bool IsTaskInsideWorkArea(SquadMemberState member, CompanionTaskKind kind, string locationName, Vector2 tile)
    {
        return this.HasActiveWorkArea(member)
            && string.Equals(member.WorkAreaLocationName, locationName, StringComparison.Ordinal)
            && CompanionWorkAreaPolicy.Allows(member.WorkAreaSpecialty, kind)
            && CompanionWorkAreaPolicy.Contains(
                member.WorkAreaCenterX,
                member.WorkAreaCenterY,
                member.WorkAreaRadius,
                (int)NormalizeTile(tile).X,
                (int)NormalizeTile(tile).Y);
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

        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.ClearCompanionTarget(member);
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
        member.Mode = CompanionMode.Waiting;
        member.SearchWood = false;
        member.SearchMining = false;
        member.ClearArea = false;
        this.StoreWaitingPosition(member, npc);
        this.SetTaskResult(member, "companion.task_result.work_area_complete");
        this.SetCompanionActivity(member, "companion.status.waiting");
        this.StopCompanionMovement(npc);
        this.ClearFollowState(member.NpcName);
        this.ShowCompanionWorkSignal(npc, npc.currentLocation, npc.Tile, "success");
        npc.doEmote(32);
        this.Say(npc, "WorkAreaComplete", force: false);
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
                ? $"{member.WorkAreaCenterX}:{member.WorkAreaCenterY}:{member.WorkAreaRadius}:{member.WorkAreaSpecialty}"
                : member.WorkAreaOrderId,
                StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            this.DrawWorkAreaOutline(
                b,
                this.GetWorkAreaCenter(area),
                area.WorkAreaRadius,
                area.WorkAreaSpecialty,
                alpha: area.Mode == CompanionMode.Waiting ? 0.13f : 0.2f);
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
}
