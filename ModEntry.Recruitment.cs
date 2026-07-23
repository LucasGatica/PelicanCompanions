using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Integrations.GenericModConfigMenu;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private void TryOpenRecruitmentOrManagement(ICursorPosition cursor)
    {
        NPC? target = this.FindTargetNpc(cursor);
        if (target is null)
        {
            this.Warn("recruitment.no_target");
            return;
        }

        if (this.members.TryGetValue(target.Name, out SquadMemberState? member))
        {
            if (member.OwnerId != Game1.player.UniqueMultiplayerID)
            {
                this.Warn("recruitment.not_owner", new { npc = target.displayName });
                return;
            }

            if (this.config.UseVanillaDialogueUi)
                this.OpenManagementMenu(target, member);
            else
                this.OpenCompanionPanel(member.NpcName);

            return;
        }

        this.TryRecruit(target, Game1.player.UniqueMultiplayerID, showPrompt: true);
    }

    private void TryRecruit(NPC npc, long ownerId, bool showPrompt)
    {
        if (!Context.IsMainPlayer)
        {
            if (ownerId != Game1.player.UniqueMultiplayerID)
                return;

            if (showPrompt)
            {
                this.ShowRecruitmentPrompt(npc, ownerId);
                return;
            }

            this.SendActionRequest("Recruit", npc.Name);
            this.Info("recruitment.request_sent", new { npc = npc.displayName });
            return;
        }

        EligibilityResult eligibility = this.CanRecruit(npc, ownerId);
        if (!eligibility.Allowed)
        {
            if (eligibility.ReasonKey == "recruitment.friendship_low")
                this.Say(npc, "FriendshipTooLow", force: true, ownerIdOverride: ownerId);

            this.Warn(eligibility.ReasonKey, new { npc = npc.displayName, required = this.config.FriendshipRequirement });
            return;
        }

        if (showPrompt)
        {
            this.ShowRecruitmentPrompt(npc, ownerId);
            return;
        }

        this.AddMember(npc, ownerId);
    }

    private void ShowRecruitmentPrompt(NPC npc, long ownerId)
    {
        string question = this.Tr("recruitment.prompt", new { npc = npc.displayName });
        Game1.currentLocation.createQuestionDialogue(
            question,
            new[] { new Response("Recruit", this.Tr("generic.yes")), new Response("Cancel", this.Tr("generic.cancel")) },
            (_, answer) =>
            {
                if (answer == "Recruit")
                {
                    // The world may have changed while the confirmation prompt was
                    // open (ownership, friendship, squad capacity, location, etc.).
                    // Run the full host-side eligibility check at the commit point.
                    this.TryRecruit(npc, ownerId, showPrompt: false);
                }
                else if (answer == "Cancel")
                {
                    this.DeclineRecruitment(npc, ownerId);
                }
            });
    }

    private void DeclineRecruitment(NPC npc, long ownerId)
    {
        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest(
                "RecruitmentRefusal",
                npc.Name,
                expectedLocationName: npc.currentLocation?.NameOrUniqueName ?? "");
            return;
        }

        this.Say(npc, "RecruitmentRefusal", force: true, ownerIdOverride: ownerId);
    }

    private EligibilityResult CanRecruit(NPC npc, long ownerId)
    {
        if (this.IsOwnerSimulationBlocked(ownerId, blockForMenu: false))
            return new EligibilityResult(false, "recruitment.blocked_state");

        if (this.members.TryGetValue(npc.Name, out SquadMemberState? existing))
        {
            return existing.OwnerId == ownerId
                ? new EligibilityResult(false, "recruitment.already_yours")
                : new EligibilityResult(false, "recruitment.already_other");
        }

        if (this.deferredNpcRestores.ContainsKey(npc.Name))
            return new EligibilityResult(false, "recruitment.blocked_state");

        if (this.members.Values.Count(p => p.OwnerId == ownerId) >= this.config.MaxSquadSize)
            return new EligibilityResult(false, "recruitment.squad_full");

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return new EligibilityResult(false, "recruitment.unsupported");

        if (!RecruitmentContextPolicy.IsLocationValid(
                owner.currentLocation is not null,
                npc.currentLocation == owner.currentLocation))
        {
            return new EligibilityResult(false, "recruitment.no_target");
        }

        if (!this.IsSupportedTarget(npc, owner))
            return new EligibilityResult(false, "recruitment.unsupported");

        if (!this.MeetsFriendshipRequirement(npc, owner))
            return new EligibilityResult(false, "recruitment.friendship_low");

        return EligibilityResult.Success;
    }

    private void AddMember(NPC npc, long ownerId)
    {
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return;

        SquadMemberState member = new()
        {
            NpcName = npc.Name,
            DisplayName = npc.displayName,
            OwnerId = ownerId,
            Mode = CompanionMode.Following,
            Profile = this.GetOrCreateCompanionProfile(npc.Name)
        };
        this.GetOrCreateOperationalProfile(ownerId, npc.Name);
        CaptureOriginalNpcState(member, npc, captureSchedule: true);
        this.deferredNpcRestores.Remove(npc.Name);
        this.members[npc.Name] = member;
        this.lastObservedCommunicationFailures[npc.Name] = "";
        try
        {
            this.RemovePendingTask(npc.Name);
            this.DisableNpcSchedule(npc, stopCurrentRoute: true);
            this.UpdateFollower(member, npc, owner, forceCatchUp: true);
        }
        catch (Exception ex)
        {
            this.members.Remove(npc.Name);
            this.lastObservedCommunicationFailures.Remove(npc.Name);
            this.ClearFollowState(npc.Name);
            this.ReleaseWorkTargetsForNpc(npc.Name);
            try
            {
                this.RestoreNpcSchedule(npc, member);
            }
            catch (Exception restoreError)
            {
                this.deferredNpcRestores[npc.Name] = CreateDeferredNpcRestore(member);
                this.MarkStateDirty();
                this.Monitor.Log($"Rollback also failed for companion '{npc.Name}': {restoreError}", LogLevel.Error);
            }

            this.Monitor.Log($"Recruitment of '{npc.Name}' was rolled back after control acquisition failed: {ex}", LogLevel.Error);
            this.Warn("recruitment.blocked_state", new { npc = npc.displayName });
            return;
        }

        this.MarkStateDirty();
        this.Info("recruitment.joined", new { npc = npc.displayName });
        this.Say(npc, "Recruit", force: true, ownerIdOverride: ownerId);
    }

    private void OpenManagementMenu(NPC npc, SquadMemberState member)
    {
        if (member.OwnerId != Game1.player.UniqueMultiplayerID)
        {
            this.Warn("recruitment.not_owner", new { npc = npc.displayName });
            return;
        }

        List<Response> responses = new()
        {
            new Response("Dismiss", this.Tr("management.dismiss")),
            new Response("DismissAll", this.Tr("management.dismiss_all")),
            new Response("Panel", this.Tr("management.panel")),
            new Response(member.Mode == CompanionMode.Following ? "Wait" : "Resume", this.Tr(member.Mode == CompanionMode.Following ? "management.wait" : "management.resume"))
        };

        if ((this.replicatedHostRules?.UseSquadInventory ?? this.config.UseSquadInventory) || this.squadInventory.Count > 0)
            responses.Add(new Response("Inventory", this.Tr("management.inventory")));

        responses.Add(new Response("Cancel", this.Tr("generic.cancel")));

        Game1.currentLocation.createQuestionDialogue(
            this.Tr("management.prompt", new { npc = npc.displayName, status = this.Tr($"status.{member.Mode}") }),
            responses.ToArray(),
            (_, answer) => this.HandleManagementAnswer(npc, member, answer));
    }

    private void HandleManagementAnswer(NPC npc, SquadMemberState member, string answer)
    {
        switch (answer)
        {
            case "Dismiss":
                this.DismissMember(npc.Name);
                break;

            case "DismissAll":
                this.DismissAll(Game1.player.UniqueMultiplayerID);
                break;

            case "Wait":
                this.SetWaiting(npc.Name, Game1.player.UniqueMultiplayerID);
                break;

            case "Resume":
                this.ResumeFollowing(npc.Name, Game1.player.UniqueMultiplayerID);
                break;

            case "Inventory":
                this.OpenSquadInventoryMenu();
                break;

            case "Panel":
                this.OpenCompanionPanel(member.NpcName);
                break;
        }
    }

    private void DismissAll(long ownerId)
    {
        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("DismissAll");
            return;
        }

        foreach (string npcName in this.members.Values.Where(p => p.OwnerId == ownerId).Select(p => p.NpcName).ToList())
        {
            try
            {
                this.DismissMember(npcName, ownerOverride: ownerId);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Dismiss-all failed for '{npcName}' and continued with the remaining companions: {ex}", LogLevel.Error);
            }
        }

        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
            this.Info("recruitment.dismissed_all");
    }

    private void DismissMember(string npcName, bool silent = false, long? ownerOverride = null)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member))
            return;

        long requester = ownerOverride ?? Game1.player.UniqueMultiplayerID;
        if (member.OwnerId != requester)
        {
            this.Warn("recruitment.not_owner", new { npc = member.DisplayName });
            return;
        }

        if (!Context.IsMainPlayer && requester == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Dismiss", npcName);
            return;
        }

        NPC? npc = Game1.getCharacterFromName(npcName, mustBeVillager: false, includeEventActors: false);
        // An unavailable custom NPC still needs a persisted vanilla-restore
        // intent. Removing membership without it would permanently forget the
        // schedule state captured when control was acquired.
        bool deferNpcRestore = npc is null
            || (silent && this.IsOwnerSimulationBlocked(requester, blockForMenu: false));
        if (npc is not null)
        {
            try
            {
                this.StopCompanionMovement(npc);
            }
            catch (Exception ex)
            {
                deferNpcRestore = true;
                this.Monitor.Log($"Stopping '{npcName}' during dismissal failed; vanilla restoration was queued: {ex}", LogLevel.Error);
            }
        }

        if (!deferNpcRestore && npc is not null)
        {
            try
            {
                this.Say(npc, "Dismiss", force: true, ownerIdOverride: requester);
                this.RestoreNpcSchedule(npc, member);
            }
            catch (Exception ex)
            {
                deferNpcRestore = true;
                this.Monitor.Log($"Schedule restoration for '{npcName}' failed and was deferred: {ex}", LogLevel.Error);
            }
        }

        if (deferNpcRestore)
        {
            // Commit the inventory/state removal now so a save or shutdown
            // can't resurrect the companion. Only the vanilla restoration is
            // deferred and persisted.
            this.deferredNpcRestores[npcName] = CreateDeferredNpcRestore(member);
        }

        // Dismissal must never destroy carried items (including the silent
        // disconnect path). Move raw stacks through the persistent overflow;
        // resolvable items are then folded into the shared squad inventory.
        if (member.Inventory is { Count: > 0 })
        {
            this.legacyOverflowItems.AddRange(member.Inventory);
            member.Inventory.Clear();
            this.ReloadOverflowInventoryIntoSquad();
        }

        this.members.Remove(npcName);
        this.lastObservedCommunicationFailures.Remove(npcName);
        this.workAreaPositionRecoveryNeeded.Remove(npcName);
        this.ClearFollowState(npcName);
        this.RemovePendingTask(npcName);
        if (!deferNpcRestore)
            this.deferredNpcRestores.Remove(npcName);
        this.MarkStateDirty();
        if (!silent)
            this.Info("recruitment.dismissed", new { npc = member.DisplayName });
    }

    private void SetWaiting(string npcName, long ownerId, bool showMessage = true)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member) || !this.CanOwnerMutate(member, ownerId))
            return;

        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            NPC? localNpc = this.GetNpcByName(npcName);
            string? expectedLocationName = localNpc?.currentLocation?.NameOrUniqueName;
            if (string.IsNullOrWhiteSpace(expectedLocationName))
            {
                this.Warn("commands.no_followers");
                return;
            }

            this.SendActionRequest("Wait", npcName, expectedLocationName: expectedLocationName);
            return;
        }

        NPC? npc = this.GetNpcByName(npcName);
        if (npc is null || npc.currentLocation is null)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("commands.no_followers");
            return;
        }

        this.RemovePendingTask(npcName);
        this.ClearFollowState(npcName);
        this.StoreWaitingPosition(member, npc);
        this.DisableNpcSchedule(npc, stopCurrentRoute: true);

        member.Mode = CompanionMode.Waiting;
        member.RoutinePausedByPlayer = true;
        this.ClearCompanionTarget(member);
        this.UpdateTargetPreview(member, new TargetPreview(false, "", -1, -1, "companion.preview.not_following"));
        this.SetCompanionActivity(member, "companion.status.waiting");
        this.MarkStateDirty();
        if (showMessage && this.ShouldShowFeedbackFor(ownerId))
            this.Info("management.waiting", new { npc = member.DisplayName });
    }

    private void ResumeFollowing(string npcName, long ownerId, bool showMessage = true)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member) || !this.CanOwnerMutate(member, ownerId))
            return;

        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Resume", npcName);
            return;
        }

        this.ClearFollowState(member.NpcName);
        member.Mode = CompanionMode.Following;
        member.RoutinePausedByPlayer = false;
        member.WaitingLocationName = null;
        member.ParkedAtUtcTicks = 0;
        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        bool needsReturn = npc is not null
            && owner is not null
            && (npc.currentLocation != owner.currentLocation
                || Vector2.Distance(NormalizeTile(npc.Tile), NormalizeTile(owner.Tile)) > MaxCompanionDistanceTiles);
        bool tasksEnabled = this.AreTasksEnabled(member.OwnerId);
        bool hasWorkArea = this.HasActiveWorkArea(member);
        bool workAreaRecoveryPending = hasWorkArea && this.HasPendingWorkAreaRecovery(member);
        if (npc is not null)
            this.DisableNpcSchedule(npc, stopCurrentRoute: true);

        this.SetCompanionActivity(
            member,
            hasWorkArea
                ? tasksEnabled && !workAreaRecoveryPending
                    ? "companion.status.work_area"
                    : "companion.status.work_area_paused"
                : needsReturn
                    ? "companion.status.returning"
                    : "companion.status.following");
        if (hasWorkArea && !tasksEnabled)
            this.SetTaskFailure(member, "companion.task_failure.tasks_disabled");
        else if (workAreaRecoveryPending)
            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
        else if (tasksEnabled && member.LastFailureReasonKey == "companion.task_failure.tasks_disabled")
            this.SetTaskFailure(member, "");
        this.UpdateTargetPreview(
            member,
            !tasksEnabled
                ? new TargetPreview(false, "", -1, -1, "companion.preview.tasks_disabled")
                : workAreaRecoveryPending
                    ? new TargetPreview(false, "", -1, -1, "companion.task_failure.work_area_unavailable")
                    : this.HasActiveWorkDirective(member)
                        ? new TargetPreview(false, "", -1, -1, "companion.preview.planning")
                        : new TargetPreview(false, "", -1, -1, "companion.preview.inactive"));
        if (hasWorkArea && tasksEnabled)
        {
            this.priorityTaskPlanningMembers.Add(member.NpcName);
            this.nextTaskScanTick = Game1.ticks + 1;
        }
        this.MarkStateDirty();
        if (showMessage && this.ShouldShowFeedbackFor(ownerId))
            this.Info("management.resumed", new { npc = member.DisplayName });
    }

    private void StoreWaitingPosition(SquadMemberState member, NPC npc)
    {
        member.WaitingLocationName = npc.currentLocation?.NameOrUniqueName;
        member.WaitingTileX = npc.Tile.X;
        member.WaitingTileY = npc.Tile.Y;
    }

    private static DeferredNpcRestoreState CreateDeferredNpcRestore(SquadMemberState member)
    {
        return new DeferredNpcRestoreState
        {
            NpcName = member.NpcName,
            OriginalLocationName = member.OriginalLocationName,
            OriginalTileX = member.OriginalTileX,
            OriginalTileY = member.OriginalTileY,
            HasOriginalPosition = member.HasOriginalPosition,
            OriginalDayIndex = member.OriginalDayIndex,
            OriginalScheduleCaptured = member.OriginalScheduleCaptured,
            OriginalScheduleKey = member.OriginalScheduleKey,
            OriginalPetBehavior = member.OriginalPetBehavior,
            OriginalSpousePatioActivity = member.OriginalSpousePatioActivity,
            OriginalMovementSpeedCaptured = member.OriginalMovementSpeedCaptured,
            OriginalMovementSpeed = member.OriginalMovementSpeed,
            OriginalAddedSpeed = member.OriginalAddedSpeed
        };
    }

    private static void CaptureOriginalNpcState(SquadMemberState member, NPC npc, bool captureSchedule)
    {
        member.OriginalLocationName = npc.currentLocation?.NameOrUniqueName;
        member.OriginalTileX = npc.Tile.X;
        member.OriginalTileY = npc.Tile.Y;
        member.HasOriginalPosition = npc.currentLocation is not null;
        member.OriginalDayIndex = Game1.Date.TotalDays;
        // A custom NPC may inject a runtime Schedule dictionary without a
        // reloadable ScheduleKey. Treat that as unknown (reload fallback), not
        // as a positively captured "no schedule" state.
        bool hasUnkeyedSchedule = npc.Schedule is { Count: > 0 }
            && string.IsNullOrWhiteSpace(npc.ScheduleKey);
        member.OriginalScheduleCaptured = captureSchedule && !hasUnkeyedSchedule;
        member.OriginalScheduleKey = member.OriginalScheduleCaptured ? npc.ScheduleKey : null;
        member.OriginalPetBehavior = npc is Pet pet ? pet.CurrentBehavior : null;
        member.OriginalSpousePatioActivity = npc.shouldPlaySpousePatioAnimation.Value;
        CaptureOriginalNpcMovementSpeed(member, npc);
    }

    private static void CaptureOriginalNpcMovementSpeed(SquadMemberState member, NPC npc)
    {
        member.OriginalMovementSpeedCaptured = true;
        member.OriginalMovementSpeed = npc.speed;
        member.OriginalAddedSpeed = npc.addedSpeed;
    }

    private void EnsureOriginalNpcMovementSpeedCaptured(SquadMemberState member, NPC npc)
    {
        if (member.OriginalMovementSpeedCaptured)
            return;

        CaptureOriginalNpcMovementSpeed(member, npc);
        this.MarkStateDirty();
    }
}
