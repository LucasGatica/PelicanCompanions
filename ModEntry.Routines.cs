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
        if (!Context.IsMainPlayer || !this.CanOwnerMutate(member, ownerId))
            return false;

        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(ownerId, member.NpcName);
        CompanionRoutineState current = profile.Routine ??= new CompanionRoutineState();
        string currentToken = CompanionRoutinePolicy.CreateStateToken(current);
        if (string.IsNullOrWhiteSpace(expectedStateToken)
            || !string.Equals(expectedStateToken, currentToken, StringComparison.Ordinal))
        {
            this.WarnForPlayer(ownerId, "companion.routine.save_conflict");
            return false;
        }

        if (!CompanionRoutinePolicy.TryDecode(encoded, out CompanionRoutineState edited))
        {
            this.WarnForPlayer(ownerId, "companion.routine.save_invalid");
            return false;
        }

        edited.Revision = current.Revision == long.MaxValue
            ? 0
            : Math.Max(0, current.Revision) + 1;
        edited.ScheduledDayIndex = edited.RepeatDaily ? -1 : Game1.Date.TotalDays;
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
        GameLocation? location = Game1.getLocationFromName(preset.LocationName);
        Vector2 center = NormalizeTile(new Vector2(preset.CenterX, preset.CenterY));
        int radius = CompanionWorkAreaPolicy.ClampRadiusToMaximum(
            preset.Radius,
            this.GetConfiguredWorkRadius());
        if (npc is null || owner is null || location is null)
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
            return RoutineWorkActivationResult.Retry;
        }

        bool hasAnyTarget = this.HasMatchingWorkAreaTarget(
            location,
            center,
            radius,
            specialty,
            includeReserved: true,
            respectConfiguredModes: false);
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
                radius,
                specialty,
                includeReserved: true,
                respectConfiguredModes: true);
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
                radius,
                specialty,
                includeReserved: true,
                respectConfiguredModes: true))
        {
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(
                member,
                this.GetMissingWorkAreaEquipmentFailureKey(
                    member,
                    location,
                    center,
                    radius,
                    specialty));
            return RoutineWorkActivationResult.Retry;
        }

        this.RemovePendingTask(member.NpcName);
        this.ReleaseWorkTargetsForNpc(member.NpcName);
        this.ClearCompanionWorkArea(member, cancelPendingAreaTask: true);
        this.ClearFollowState(member.NpcName);
        if (!this.PlaceNpc(npc, location, center))
        {
            this.workAreaPositionRecoveryNeeded.Add(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.work_area_paused");
            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
            return RoutineWorkActivationResult.Retry;
        }

        member.Mode = CompanionMode.Following;
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
        CompanionRoutineAreaPreset preset = new()
        {
            Specialty = member.WorkAreaSpecialty,
            LocationName = member.WorkAreaLocationName,
            CenterX = member.WorkAreaCenterX,
            CenterY = member.WorkAreaCenterY,
            Radius = member.WorkAreaRadius
        };
        CompanionRoutineAreaPreset? previous = CompanionRoutinePolicy.GetAreaPreset(
            routine,
            preset.Specialty);
        if (previous is not null
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

        bool routineOwnedTask = task.UsesFixedWorkArea
            && task.FixedWorkAreaOrderId.StartsWith("routine-", StringComparison.Ordinal);
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
