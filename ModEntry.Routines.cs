using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private CompanionRoutineState GetCompanionRoutineForPanel(SquadMemberState member)
    {
        if (!this.TryGetOperationalProfile(member.OwnerId, member.NpcName, out CompanionOperationalProfileState? profile))
        {
            CompanionRoutineState empty = new();
            empty.Hours = CompanionRoutinePolicy.NormalizeHours(empty.Hours).ToList();
            return empty;
        }

        CompanionRoutineState routine = CompanionOperationsStateCopy.CloneRoutine(profile.Routine);
        routine.Hours = CompanionRoutinePolicy.NormalizeHours(routine.Hours).ToList();
        routine.AreaPresets = CompanionRoutinePolicy.NormalizeAreaPresets(routine.AreaPresets).ToList();
        return routine;
    }

    private bool SaveCompanionRoutineFromPanel(
        SquadMemberState member,
        CompanionRoutineState routine,
        string expectedStateToken)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId))
            return false;

        string encoded = CompanionRoutinePolicy.Encode(routine);
        if (!CompanionRoutinePolicy.TryDecode(encoded, out _))
            return false;

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "SetRoutine",
                member.NpcName,
                encoded,
                expectedStateToken: expectedStateToken);
            return true;
        }

        return this.TryApplyCompanionRoutineEdit(member, ownerId, encoded, expectedStateToken);
    }

    private bool TryApplyCompanionRoutineEdit(
        SquadMemberState member,
        long ownerId,
        string encoded,
        string expectedStateToken)
    {
        if (!Context.IsMainPlayer
            || !this.CanOwnerMutate(member, ownerId)
            || string.IsNullOrWhiteSpace(encoded)
            || encoded.Length > 32768)
        {
            return false;
        }

        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(ownerId, member.NpcName);
        CompanionRoutineState current = profile.Routine ??= new CompanionRoutineState();
        string currentToken = CompanionRoutinePolicy.CreateStateToken(current);
        if (string.IsNullOrWhiteSpace(expectedStateToken)
            || !string.Equals(expectedStateToken, currentToken, StringComparison.Ordinal))
        {
            this.WarnForPlayer(ownerId, "companion.routine.save_conflict");
            return false;
        }

        if (!CompanionRoutinePolicy.TryDecode(encoded, out CompanionRoutineState edited)
            || !this.AreRoutineAreaPresetsHostValid(edited.AreaPresets))
        {
            this.WarnForPlayer(ownerId, "companion.routine.save_invalid");
            return false;
        }

        edited.Revision = current.Revision == long.MaxValue
            ? 0
            : Math.Max(0, current.Revision) + 1;
        edited.ScheduledDayIndex = edited.RepeatDaily ? -1 : Game1.Date.TotalDays;
        if (!CompanionRoutinePolicy.HasAreaConfigurationPayload(encoded))
            edited.AreaPresets = CompanionRoutinePolicy.NormalizeAreaPresets(current.AreaPresets).ToList();
        CompanionRoutinePolicy.ResetExecution(edited);
        bool applyCompletion = CompanionRoutinePolicy.ShouldApplyCompletionAfterEdit(current, edited);
        profile.Routine = edited;

        this.MarkStateDirty();
        if (applyCompletion)
            this.ApplyRoutineCompletion(member, edited.CompletionBehavior);
        else
            this.RefreshCompanionRoutine(member, allowOneShotExpiry: true);
        this.InfoForPlayer(ownerId, "companion.routine.saved", new { npc = member.DisplayName });
        return true;
    }

    private bool AreRoutineAreaPresetsHostValid(
        IEnumerable<CompanionRoutineAreaPreset>? presets)
    {
        return CompanionRoutinePolicy.AreAreaPresetsValidForKnownMaps(
            presets,
            locationName =>
            {
                GameLocation? location = ResolvePersistentRoutineLocation(locationName);
                if (location?.Map is null || location.Map.Layers.Count == 0)
                    return null;

                return (
                    location.Map.Layers[0].LayerWidth,
                    location.Map.Layers[0].LayerHeight);
            });
    }

    private static GameLocation? ResolvePersistentRoutineLocation(string? locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName))
            return null;

        GameLocation? match = null;
        Utility.ForEachLocation(
            location =>
            {
                if (location.IsTemporary
                    || !string.Equals(
                        location.NameOrUniqueName,
                        locationName,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                match = location;
                return false;
            },
            includeInteriors: true,
            includeGenerated: false);
        return match;
    }

    private void RequestFollowCompanionRoutine(SquadMemberState member)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId))
            return;

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("FollowRoutine", member.NpcName);
            return;
        }

        this.TryFollowCompanionRoutine(member, ownerId);
    }

    private bool TryFollowCompanionRoutine(
        SquadMemberState member,
        long ownerId,
        bool showMessage = true)
    {
        if (!Context.IsMainPlayer || !this.CanOwnerMutate(member, ownerId))
            return false;

        if (!this.TryGetOperationalProfile(
                ownerId,
                member.NpcName,
                out CompanionOperationalProfileState? profile))
        {
            if (showMessage)
            {
                this.WarnForPlayer(
                    ownerId,
                    "companion.routine.follow_unavailable",
                    new { npc = member.DisplayName });
            }
            return false;
        }

        CompanionRoutineState routine = profile.Routine ??= new CompanionRoutineState();
        int dayIndex = Game1.Date.TotalDays;
        if (!CompanionRoutinePolicy.CanResumeNow(routine, dayIndex)
            || this.GetOwnerFarmer(ownerId) is null
            || this.GetNpcByName(member.NpcName) is null)
        {
            if (showMessage)
            {
                this.WarnForPlayer(
                    ownerId,
                    "companion.routine.follow_unavailable",
                    new { npc = member.DisplayName });
            }
            return false;
        }

        if (!routine.RepeatDaily && routine.ScheduledDayIndex < 0)
            routine.ScheduledDayIndex = dayIndex;

        // Clear every explicit/manual override through the same transition used
        // by routine activities, then let the scheduler restore the current
        // block. Its execution key is intentionally preserved so completed work
        // and deposits aren't repeated by a simple "follow routine" command.
        this.SetRoutineFollowing(member);
        this.MarkStateDirty();
        if (showMessage)
        {
            this.InfoForPlayer(
                ownerId,
                "companion.routine.following_now",
                new { npc = member.DisplayName });
        }
        this.RefreshCompanionRoutine(member, allowOneShotExpiry: true);
        return true;
    }

    private void RefreshCompanionRoutines(bool allowOneShotExpiry = true)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || this.saveWritesBlocked)
            return;

        foreach (SquadMemberState member in this.members.Values.ToList())
        {
            try
            {
                this.RefreshCompanionRoutine(member, allowOneShotExpiry);
            }
            catch (Exception ex)
            {
                this.SetTaskFailure(member, "companion.task_failure.routine_unavailable");
                this.Monitor.Log(
                    $"Routine refresh failed for '{member.NpcName}' and was isolated: {ex}",
                    LogLevel.Error);
            }
        }
    }

    private void RefreshCompanionRoutine(SquadMemberState member, bool allowOneShotExpiry)
    {
        if (!this.TryGetOperationalProfile(member.OwnerId, member.NpcName, out CompanionOperationalProfileState? profile))
            return;

        CompanionRoutineState routine = profile.Routine ??= new CompanionRoutineState();
        int dayIndex = Game1.Date.TotalDays;
        bool ownerAvailable = this.GetOwnerFarmer(member.OwnerId) is not null;
        if (routine.Enabled && !routine.RepeatDaily && routine.ScheduledDayIndex < 0)
        {
            routine.ScheduledDayIndex = dayIndex;
            this.MarkStateDirty();
        }

        if (allowOneShotExpiry
            && routine.Enabled
            && !routine.RepeatDaily
            && routine.ScheduledDayIndex >= 0
            && routine.ScheduledDayIndex < dayIndex)
        {
            routine.Enabled = false;
            routine.ScheduledDayIndex = -1;
            routine.Revision = routine.Revision == long.MaxValue ? 0 : Math.Max(0, routine.Revision) + 1;
            CompanionRoutinePolicy.ResetExecution(routine);
            if (ownerAvailable)
                this.ApplyRoutineCompletion(member, routine.CompletionBehavior);
            this.MarkStateDirty();
            return;
        }

        if (!ownerAvailable
            || !CompanionRoutinePolicy.ShouldRun(routine, dayIndex)
            || member.Mode == CompanionMode.ParkedForDisconnect)
        {
            return;
        }

        CompanionRoutineActivity activity = CompanionRoutinePolicy.GetActivity(routine.Hours, Game1.timeOfDay);
        int blockStartHour = CompanionRoutinePolicy.GetBlockStartHour(routine.Hours, Game1.timeOfDay);
        bool startsOperationalBlock = activity == CompanionRoutineActivity.Deposit
            || CompanionRoutinePolicy.TryGetWorkSpecialty(activity, out _);
        bool isApplied = CompanionRoutinePolicy.IsAppliedBlock(routine, dayIndex, blockStartHour);
        bool hasExplicitOverride = this.HasExplicitRoutineOverride(member);
        if (CompanionRoutinePolicy.IsCompletedBlock(routine, dayIndex, blockStartHour))
        {
            this.RestoreRoutineActivityAfterOverride(
                member,
                CompanionRoutinePolicy.GetCompletionActivity(routine.CompletionBehavior),
                hasExplicitOverride);
            return;
        }

        if (isApplied && !startsOperationalBlock)
        {
            this.RestoreRoutineActivityAfterOverride(member, activity, hasExplicitOverride);
            return;
        }

        if (isApplied
            && !CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                    routine,
                    dayIndex,
                    blockStartHour,
                    activity,
                    hasExplicitOverride,
                    this.HasActiveRoutineWorkArea(member)))
        {
            return;
        }

        if (startsOperationalBlock)
        {
            this.SuspendForPendingRoutineBlock(member);
            if (!isApplied)
            {
                // Claim the block before activation. Retryable prerequisites
                // (area, tool, map, toggle, chest) can then coexist with a
                // temporary explicit override without losing the hour boundary.
                CompanionRoutinePolicy.MarkApplied(routine, dayIndex, blockStartHour);
                this.MarkStateDirty();
            }
        }

        if (CompanionRoutinePolicy.TryGetWorkSpecialty(activity, out CompanionWorkSpecialty specialty))
        {
            RoutineWorkActivationResult result = this.TryActivateRoutineWorkArea(
                member,
                routine,
                specialty,
                dayIndex,
                blockStartHour);
            if (result == RoutineWorkActivationResult.Started)
                return;
            if (result == RoutineWorkActivationResult.Complete
                && this.ApplyRoutineCompletion(member, routine.CompletionBehavior))
            {
                CompanionRoutinePolicy.MarkCompleted(routine, dayIndex, blockStartHour);
                this.MarkStateDirty();
            }
            return;
        }

        if (activity == CompanionRoutineActivity.Deposit)
        {
            if (this.TryDepositCompanionInventoryToAssignedChest(member, showFeedback: false)
                && this.ApplyRoutineCompletion(member, routine.CompletionBehavior))
            {
                CompanionRoutinePolicy.MarkCompleted(routine, dayIndex, blockStartHour);
                this.MarkStateDirty();
            }
            return;
        }

        if (this.ApplyRoutineActivity(member, activity))
        {
            CompanionRoutinePolicy.MarkApplied(routine, dayIndex, blockStartHour);
            this.MarkStateDirty();
        }
    }

    /// <summary>
    /// Reassert an already-applied passive block after its temporary manual
    /// directive, context task, recall, or work area has ended. Mode matching
    /// keeps the periodic refresh idempotent.
    /// </summary>
    private void RestoreRoutineActivityAfterOverride(
        SquadMemberState member,
        CompanionRoutineActivity activity,
        bool hasExplicitOverride)
    {
        if (hasExplicitOverride
            || CompanionRoutinePolicy.IsActivityModeActive(activity, member.Mode))
        {
            return;
        }

        if (this.ApplyRoutineActivity(member, activity))
            this.MarkStateDirty();
    }

    /// <summary>
    /// End the previous routine block before an operational block starts. The
    /// new work/deposit may need to retry, but the old block must never keep
    /// mutating the world during that retry window.
    /// </summary>
    private void SuspendForPendingRoutineBlock(SquadMemberState member)
    {
        const string pendingStatus = "companion.status.routine_pending";
        if (member.Mode == CompanionMode.Waiting
            && !this.HasActiveWorkArea(member)
            && !this.pendingTasks.ContainsKey(member.NpcName)
            && member.CurrentActivityKey is pendingStatus
                or "companion.status.work_area_paused"
                or "companion.status.work_area_blocked")
        {
            return;
        }

        NPC? npc = this.GetNpcByName(member.NpcName);
        this.PrepareForRoutineModeChange(member, npc);
        member.Mode = CompanionMode.Waiting;
        member.RoutinePausedByPlayer = false;
        member.ParkedAtUtcTicks = 0;
        if (npc?.currentLocation is not null)
            this.StoreWaitingPosition(member, npc);
        else
        {
            member.WaitingLocationName = null;
            member.WaitingTileX = 0;
            member.WaitingTileY = 0;
        }

        this.SetCompanionActivity(member, pendingStatus);
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.planning"));
        this.MarkStateDirty();
    }

    private RoutineWorkActivationResult TryActivateRoutineWorkArea(
        SquadMemberState member,
        CompanionRoutineState routine,
        CompanionWorkSpecialty specialty,
        int dayIndex,
        int blockStartHour)
    {
        CompanionRoutineAreaPreset? preset = CompanionRoutinePolicy.GetAreaPreset(routine, specialty);
        if (preset is null)
        {
            const string missingAreaFailure = "companion.task_failure.routine_area_missing";
            bool alreadyReported = string.Equals(
                member.LastFailureReasonKey,
                missingAreaFailure,
                StringComparison.Ordinal);
            this.SetTaskFailure(member, missingAreaFailure);
            if (!alreadyReported)
            {
                this.WarnForPlayer(member.OwnerId, "companion.routine.area_missing", new
                {
                    npc = member.DisplayName,
                    specialty = this.Tr($"companion.specialty.{specialty}")
                });
            }
            return RoutineWorkActivationResult.Retry;
        }

        if (this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: false))
            return RoutineWorkActivationResult.Retry;

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        GameLocation? location = ResolvePersistentRoutineLocation(preset.LocationName);
        if (npc is null
            || owner is null
            || location?.Map is null
            || location.Map.Layers.Count == 0)
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
            return RoutineWorkActivationResult.Retry;
        }

        int mapWidth = location.Map.Layers[0].LayerWidth;
        int mapHeight = location.Map.Layers[0].LayerHeight;
        int radius = preset.RegionKind == CompanionWorkRegionKind.Circle
            ? CompanionWorkAreaPolicy.ClampRadiusToMaximum(
                preset.Radius,
                this.GetConfiguredWorkRadius())
            : preset.Radius;
        if (!CompanionWorkAreaPolicy.IsRegionGeometryValid(
                preset.RegionKind,
                preset.CenterX,
                preset.CenterY,
                radius,
                preset.MinX,
                preset.MinY,
                preset.Size,
                mapWidth,
                mapHeight))
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
            return RoutineWorkActivationResult.Retry;
        }

        Vector2 center = preset.RegionKind switch
        {
            CompanionWorkRegionKind.DelimitedSquare => new Vector2(
                preset.MinX + (preset.Size - 1) / 2,
                preset.MinY + (preset.Size - 1) / 2),
            CompanionWorkRegionKind.FarmWide => new Vector2(mapWidth / 2, mapHeight / 2),
            _ => new Vector2(preset.CenterX, preset.CenterY)
        };
        center = NormalizeTile(center);
        int searchRadius = preset.RegionKind switch
        {
            CompanionWorkRegionKind.DelimitedSquare => preset.Size * 2 + 1,
            CompanionWorkRegionKind.FarmWide => mapWidth + mapHeight,
            _ => radius
        };
        bool ContainsTarget(Vector2 rawTile)
        {
            Vector2 tile = NormalizeTile(rawTile);
            return CompanionWorkAreaPolicy.ContainsRegion(
                preset.RegionKind,
                (int)center.X,
                (int)center.Y,
                radius,
                preset.MinX,
                preset.MinY,
                preset.Size,
                (int)tile.X,
                (int)tile.Y,
                mapWidth,
                mapHeight);
        }

        bool hasAnyTarget = this.HasMatchingWorkAreaTarget(
            location,
            center,
            searchRadius,
            specialty,
            includeReserved: true,
            respectConfiguredModes: false,
            containsTarget: ContainsTarget);
        if (!hasAnyTarget)
            return RoutineWorkActivationResult.Complete;

        if (!this.AreTasksEnabled(member.OwnerId))
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.tasks_disabled");
            return RoutineWorkActivationResult.Retry;
        }

        bool hasEnabledTarget = this.HasMatchingWorkAreaTarget(
                location,
                center,
                searchRadius,
                specialty,
                includeReserved: true,
                respectConfiguredModes: true,
                containsTarget: ContainsTarget);
        if (!hasEnabledTarget)
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.tasks_disabled");
            return RoutineWorkActivationResult.Retry;
        }

        if (!this.HasMatchingWorkAreaTargetForMember(
                member,
                location,
                center,
                searchRadius,
                specialty,
                includeReserved: true,
                respectConfiguredModes: true,
                containsTarget: ContainsTarget))
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(
                member,
                this.GetMissingWorkAreaEquipmentFailureKey(
                    member,
                    location,
                    center,
                    searchRadius,
                    specialty,
                    ContainsTarget));
            return RoutineWorkActivationResult.Retry;
        }

        this.RemovePendingTask(member.NpcName);
        this.ReleaseWorkTargetsForNpc(member.NpcName);
        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.ClearFollowState(member.NpcName);
        if (!this.TryGetRoutineWorkStartTile(
                npc,
                location,
                preset,
                center,
                radius,
                out Vector2 startTile)
            || !this.PlaceNpc(npc, location, startTile))
        {
            this.workAreaPositionRecoveryNeeded.Add(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
            return RoutineWorkActivationResult.Retry;
        }
        if (preset.RegionKind != CompanionWorkRegionKind.Circle)
            center = NormalizeTile(startTile);

        member.Mode = CompanionMode.Following;
        member.RoutinePausedByPlayer = false;
        member.ParkedAtUtcTicks = 0;
        member.WaitingLocationName = null;
        member.SearchWood = false;
        member.SearchMining = false;
        member.SearchWatering = false;
        member.ClearArea = false;
        member.WorkAreaActive = true;
        member.WorkAreaOrderId = $"routine-{dayIndex}-{blockStartHour}-{Guid.NewGuid():N}";
        member.WorkAreaLocationName = location.NameOrUniqueName;
        member.WorkAreaCenterX = (int)center.X;
        member.WorkAreaCenterY = (int)center.Y;
        member.WorkAreaRadius = radius;
        member.WorkAreaRegionKind = preset.RegionKind;
        member.WorkAreaMinX = preset.MinX;
        member.WorkAreaMinY = preset.MinY;
        member.WorkAreaSize = preset.Size;
        member.WorkAreaSpecialty = specialty;
        member.PreferredWorkSpecialty = specialty;
        this.workAreaPositionRecoveryNeeded.Remove(member.NpcName);
        this.SetTaskFailure(member, "");
        this.ClearCompanionTarget(member);
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.planning"));
        this.SetCompanionActivity(member, "companion.status.work_area");
        this.priorityTaskPlanningMembers.Add(member.NpcName);
        this.nextTaskScanTick = Game1.ticks + 1;
        this.InvalidateTargetPreviews();
        return RoutineWorkActivationResult.Started;
    }

    private bool TryGetRoutineWorkStartTile(
        NPC npc,
        GameLocation location,
        CompanionRoutineAreaPreset preset,
        Vector2 center,
        int effectiveRadius,
        out Vector2 startTile)
    {
        if (preset.RegionKind == CompanionWorkRegionKind.FarmWide
            && npc.currentLocation == location
            && this.IsTileSafe(location, NormalizeTile(npc.Tile)))
        {
            startTile = NormalizeTile(npc.Tile);
            return true;
        }

        Vector2 preferred = center;
        if (preset.RegionKind == CompanionWorkRegionKind.FarmWide
            && location is Farm farm)
        {
            Point entry = farm.GetMainFarmHouseEntry();
            preferred = new Vector2(entry.X, entry.Y);
            if (this.IsTileSafe(location, preferred))
            {
                startTile = preferred;
                return true;
            }
        }

        int mapWidth = location.Map.Layers[0].LayerWidth;
        int mapHeight = location.Map.Layers[0].LayerHeight;
        int minX = preset.RegionKind switch
        {
            CompanionWorkRegionKind.Circle => Math.Max(0, preset.CenterX - effectiveRadius),
            CompanionWorkRegionKind.DelimitedSquare => preset.MinX,
            _ => 0
        };
        int minY = preset.RegionKind switch
        {
            CompanionWorkRegionKind.Circle => Math.Max(0, preset.CenterY - effectiveRadius),
            CompanionWorkRegionKind.DelimitedSquare => preset.MinY,
            _ => 0
        };
        int maxX = preset.RegionKind switch
        {
            CompanionWorkRegionKind.Circle => Math.Min(mapWidth - 1, preset.CenterX + effectiveRadius),
            CompanionWorkRegionKind.DelimitedSquare => preset.MinX + preset.Size - 1,
            _ => mapWidth - 1
        };
        int maxY = preset.RegionKind switch
        {
            CompanionWorkRegionKind.Circle => Math.Min(mapHeight - 1, preset.CenterY + effectiveRadius),
            CompanionWorkRegionKind.DelimitedSquare => preset.MinY + preset.Size - 1,
            _ => mapHeight - 1
        };
        long bestDistance = long.MaxValue;
        Vector2 best = preferred;
        bool found = false;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2 candidate = new(x, y);
                if (!CompanionWorkAreaPolicy.ContainsRegion(
                        preset.RegionKind,
                        preset.CenterX,
                        preset.CenterY,
                        effectiveRadius,
                        preset.MinX,
                        preset.MinY,
                        preset.Size,
                        x,
                        y,
                        mapWidth,
                        mapHeight)
                    || !this.IsTileSafe(location, candidate))
                {
                    continue;
                }

                long deltaX = x - (long)preferred.X;
                long deltaY = y - (long)preferred.Y;
                long distance = deltaX * deltaX + deltaY * deltaY;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = candidate;
                found = true;
            }
        }

        startTile = best;
        return found;
    }

    private bool ApplyRoutineActivity(SquadMemberState member, CompanionRoutineActivity activity)
    {
        return activity switch
        {
            CompanionRoutineActivity.Follow => this.SetRoutineFollowing(member),
            CompanionRoutineActivity.Wait => this.SetRoutineWaiting(member),
            CompanionRoutineActivity.VanillaRoutine => this.SetRoutineOriginal(member),
            _ => false
        };
    }

    private bool ApplyRoutineCompletion(
        SquadMemberState member,
        CompanionRoutineCompletionBehavior behavior)
    {
        CompanionRoutineActivity activity = CompanionRoutinePolicy.GetCompletionActivity(behavior);
        return this.ApplyRoutineActivity(member, activity)
            || activity != CompanionRoutineActivity.Follow && this.SetRoutineFollowing(member);
    }

    private bool SetRoutineFollowing(SquadMemberState member)
    {
        NPC? npc = this.GetNpcByName(member.NpcName);
        this.PrepareForRoutineModeChange(member, npc);
        member.Mode = CompanionMode.Following;
        member.RoutinePausedByPlayer = false;
        member.ParkedAtUtcTicks = 0;
        member.WaitingLocationName = null;
        this.SetTaskFailure(member, "");
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.inactive"));
        this.SetCompanionActivity(member, "companion.status.following");
        return true;
    }

    private bool SetRoutineWaiting(SquadMemberState member)
    {
        NPC? npc = this.GetNpcByName(member.NpcName);
        if (npc is null || npc.currentLocation is null)
            return false;

        this.PrepareForRoutineModeChange(member, npc);
        member.Mode = CompanionMode.Waiting;
        member.RoutinePausedByPlayer = false;
        member.ParkedAtUtcTicks = 0;
        this.StoreWaitingPosition(member, npc);
        this.SetTaskFailure(member, "");
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
        this.SetCompanionActivity(member, "companion.status.waiting");
        return true;
    }

    private bool SetRoutineOriginal(SquadMemberState member)
    {
        NPC? npc = this.GetNpcByName(member.NpcName);
        if (npc is null)
            return false;

        bool failureAlreadyReported = string.Equals(
            member.LastFailureReasonKey,
            "companion.task_failure.routine_unavailable",
            StringComparison.Ordinal);

        this.RemovePendingTask(member.NpcName);
        this.ReleaseWorkTargetsForNpc(member.NpcName);
        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.ClearFollowState(member.NpcName);
        this.ClearRoutineDirectives(member);
        this.ClearCompanionTarget(member);
        member.Mode = CompanionMode.OriginalRoutine;
        member.RoutinePausedByPlayer = false;
        member.ParkedAtUtcTicks = 0;
        member.WaitingLocationName = null;
        this.SetTaskFailure(member, "");
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
        this.SetCompanionActivity(member, "companion.status.original_routine");
        try
        {
            this.RestoreNpcSchedule(npc, member);
            return true;
        }
        catch (Exception ex)
        {
            member.Mode = CompanionMode.Following;
            member.RoutinePausedByPlayer = false;
            this.SetTaskFailure(member, "companion.task_failure.routine_unavailable");
            this.SetCompanionActivity(member, "companion.status.following");
            if (!failureAlreadyReported)
            {
                this.Monitor.Log(
                    $"Could not restore the original routine for '{member.NpcName}': {ex.Message}",
                    LogLevel.Warn);
            }
            try
            {
                this.DisableNpcSchedule(npc, stopCurrentRoute: true);
            }
            catch
            {
                // Periodic schedule maintenance will retry control acquisition.
            }
            return false;
        }
    }

    private void PrepareForRoutineModeChange(SquadMemberState member, NPC? npc)
    {
        this.RemovePendingTask(member.NpcName);
        this.ReleaseWorkTargetsForNpc(member.NpcName);
        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.ClearFollowState(member.NpcName);
        this.ClearRoutineDirectives(member);
        this.ClearCompanionTarget(member);
        if (npc is not null)
        {
            this.StopCompanionMovement(npc);
            this.DisableNpcSchedule(npc, stopCurrentRoute: true);
        }
    }

    private void ClearRoutineDirectives(SquadMemberState member)
    {
        member.SearchWood = false;
        member.SearchMining = false;
        member.SearchWatering = false;
        member.ClearArea = false;
    }

    private void RememberRoutineAreaPreset(SquadMemberState member)
    {
        if (!Context.IsMainPlayer || !this.HasActiveWorkArea(member))
            return;

        CompanionRoutineState routine = this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName).Routine;
        CompanionRoutineAreaPreset? previous = CompanionRoutinePolicy.GetAreaPreset(
            routine,
            member.WorkAreaSpecialty);
        // Manual wheel orders remain circular and can seed/update the legacy
        // circular preset. They must not silently replace a scope explicitly
        // chosen in the routine editor.
        if (previous is not null && previous.RegionKind != CompanionWorkRegionKind.Circle)
            return;

        CompanionRoutineAreaPreset preset = new()
        {
            Specialty = member.WorkAreaSpecialty,
            RegionKind = CompanionWorkRegionKind.Circle,
            LocationName = member.WorkAreaLocationName,
            CenterX = member.WorkAreaCenterX,
            CenterY = member.WorkAreaCenterY,
            Radius = member.WorkAreaRadius,
            MinX = -1,
            MinY = -1,
            Size = 0
        };
        if (previous is not null
            && previous.RegionKind == CompanionWorkRegionKind.Circle
            && string.Equals(previous.LocationName, preset.LocationName, StringComparison.Ordinal)
            && previous.CenterX == preset.CenterX
            && previous.CenterY == preset.CenterY
            && previous.Radius == preset.Radius)
        {
            return;
        }

        CompanionRoutinePolicy.UpsertAreaPreset(routine, preset);
        bool presetBelongsToCurrentBlock = CompanionRoutinePolicy.ShouldRun(
                routine,
                Game1.Date.TotalDays)
            && CompanionRoutinePolicy.TryGetWorkSpecialty(
                CompanionRoutinePolicy.GetActivity(routine.Hours, Game1.timeOfDay),
                out CompanionWorkSpecialty scheduledSpecialty)
            && scheduledSpecialty == preset.Specialty;
        int currentBlockStartHour = CompanionRoutinePolicy.GetBlockStartHour(
            routine.Hours,
            Game1.timeOfDay);
        if (presetBelongsToCurrentBlock
            && CompanionRoutinePolicy.IsCompletedBlock(
                routine,
                Game1.Date.TotalDays,
                currentBlockStartHour))
        {
            // Recover a same-hour block which an older version may have marked
            // complete while its preset was missing. An applied-but-waiting
            // block keeps its claim so this manual area can satisfy it directly.
            routine.Revision = routine.Revision == long.MaxValue
                ? 0
                : Math.Max(0, routine.Revision) + 1;
            CompanionRoutinePolicy.ResetExecution(routine);
        }
        this.MarkStateDirty();
    }

    private CompanionRoutinePlanningLane GetRoutinePlanningLane(SquadMemberState member)
    {
        bool hasExplicitDirective = this.HasActiveWorkDirective(member);
        this.TryGetOperationalProfile(
            member.OwnerId,
            member.NpcName,
            out CompanionOperationalProfileState? profile);
        return CompanionRoutinePolicy.SelectPlanningLane(
            profile?.Routine,
            Game1.Date.TotalDays,
            hasExplicitDirective);
    }

    private bool HasActiveRoutineWorkArea(SquadMemberState member)
    {
        return this.HasActiveWorkArea(member)
            && member.WorkAreaOrderId.StartsWith("routine-", StringComparison.Ordinal);
    }

    private bool HasExplicitRoutineOverride(SquadMemberState member)
    {
        if (this.activeRecallTargets.ContainsKey(member.NpcName)
            || member.RoutinePausedByPlayer
            || member.SearchWood
            || member.SearchMining
            || member.SearchWatering
            || member.ClearArea)
        {
            return true;
        }

        if (this.HasActiveWorkArea(member) && !this.HasActiveRoutineWorkArea(member))
            return true;

        if (!this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task))
            return false;

        bool routineOwnedTask = TaskNavigationPolicy.IsRoutineWorkOrder(
            task.UsesFixedWorkArea,
            task.FixedWorkAreaOrderId);
        return !routineOwnedTask && !task.UsesConfiguredAutonomy;
    }

    private bool TryHandleRoutineWorkAreaCompletion(
        SquadMemberState member,
        CompanionWorkSpecialty completedSpecialty)
    {
        if (!this.TryGetOperationalProfile(member.OwnerId, member.NpcName, out CompanionOperationalProfileState? profile))
            return false;

        CompanionRoutineState routine = profile.Routine;
        int dayIndex = Game1.Date.TotalDays;
        int blockStartHour = CompanionRoutinePolicy.GetBlockStartHour(routine.Hours, Game1.timeOfDay);
        CompanionRoutineActivity activity = CompanionRoutinePolicy.GetActivity(routine.Hours, Game1.timeOfDay);
        if (!CompanionRoutinePolicy.ShouldRun(routine, dayIndex)
            || !CompanionRoutinePolicy.TryGetWorkSpecialty(activity, out CompanionWorkSpecialty scheduledSpecialty)
            || scheduledSpecialty != completedSpecialty
            || !CompanionRoutinePolicy.IsAppliedBlock(routine, dayIndex, blockStartHour)
            || CompanionRoutinePolicy.IsCompletedBlock(routine, dayIndex, blockStartHour))
        {
            return false;
        }

        if (!this.ApplyRoutineCompletion(member, routine.CompletionBehavior))
            return false;

        CompanionRoutinePolicy.MarkCompleted(routine, dayIndex, blockStartHour);
        this.MarkStateDirty();
        return true;
    }

    private enum RoutineWorkActivationResult
    {
        Retry,
        Started,
        Complete
    }
}
