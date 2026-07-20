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
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // ModEntry fields are shared by every local split-screen instance. The
        // host's in-memory state is authoritative, so a secondary local screen
        // must never clear it or replace it with a network snapshot.
        if (Context.IsOnHostComputer && !Context.IsMainPlayer)
            return;

        this.ResetRuntimeState(clearProfiles: false);
        this.LoadNpcProfiles();

        if (!Context.IsMainPlayer)
        {
            if (!Context.IsOnHostComputer)
                this.RequestStateSnapshot();
            return;
        }

        SavedModState data;
        try
        {
            data = this.Helper.Data.ReadSaveData<SavedModState>(SaveKey) ?? new SavedModState();
        }
        catch (Exception ex)
        {
            this.saveWritesBlocked = true;
            this.Monitor.Log($"Pelican Companions couldn't read its save data. Writing is disabled for this session to preserve the original data. {ex}", LogLevel.Error);
            this.Warn("state.unavailable");
            return;
        }

        if (data.Version < 1 || data.Version > CurrentSaveVersion)
        {
            this.saveWritesBlocked = true;
            this.Monitor.Log(
                $"Pelican Companions save schema {data.Version} is outside the supported range 1-{CurrentSaveVersion}. The mod state won't be loaded or overwritten.",
                LogLevel.Error);
            this.Warn("state.unavailable");
            return;
        }

        this.stateRevision = Math.Max(0, data.Revision);
        try
        {
            foreach (SquadMemberState? member in data.Members ?? Enumerable.Empty<SquadMemberState>())
            {
                if (member is null || string.IsNullOrWhiteSpace(member.NpcName))
                    throw new InvalidDataException("The companion list contains an unnamed or null entry.");
                if (this.members.ContainsKey(member.NpcName))
                    throw new InvalidDataException($"The companion list contains duplicate NPC key '{member.NpcName}'.");

                this.legacyOverflowItems.AddRange(this.NormalizeLoadedMember(member));
                this.members.Add(member.NpcName, member);
            }

            foreach ((string key, bool value) in data.TaskTogglesByPlayer ?? new Dictionary<string, bool>())
            {
                if (long.TryParse(key, out long playerId))
                    this.taskToggles[playerId] = value;
            }

            foreach (SavedItemStack? saved in data.SquadInventory ?? new List<SavedItemStack>())
            {
                if (saved is null || string.IsNullOrWhiteSpace(saved.QualifiedItemId) || saved.Stack <= 0)
                    throw new InvalidDataException("The shared squad inventory contains an invalid stack.");

                Item? item = this.TryCreateItem(saved);
                if (item is not null)
                    this.squadInventory.Add(item);
                else
                    this.legacyOverflowItems.Add(saved);
            }

            foreach (SavedItemStack? saved in data.LegacyOverflowItems ?? new List<SavedItemStack>())
            {
                if (saved is null || string.IsNullOrWhiteSpace(saved.QualifiedItemId) || saved.Stack <= 0)
                    throw new InvalidDataException("The overflow inventory contains an invalid stack.");

                this.legacyOverflowItems.Add(saved);
            }

            foreach (DeferredNpcRestoreState? restore in data.PendingNpcRestores ?? new List<DeferredNpcRestoreState>())
            {
                if (restore is null || string.IsNullOrWhiteSpace(restore.NpcName))
                    throw new InvalidDataException("The pending NPC restore list contains an invalid entry.");
                if (this.members.ContainsKey(restore.NpcName) || this.deferredNpcRestores.ContainsKey(restore.NpcName))
                    throw new InvalidDataException($"NPC '{restore.NpcName}' has conflicting active or deferred state.");

                this.deferredNpcRestores.Add(restore.NpcName, restore);
            }
        }
        catch (Exception ex)
        {
            this.ResetRuntimeState(clearProfiles: false);
            this.saveWritesBlocked = true;
            this.Monitor.Log($"Pelican Companions rejected invalid save data. Writing is disabled for this session to preserve the original data. {ex}", LogLevel.Error);
            this.Warn("state.unavailable");
            return;
        }

        try
        {
            // Finish all in-memory normalization before touching live NPCs. If
            // this phase fails, rollback is complete and save writes can safely
            // remain disabled without leaving a character under partial control.
            this.ValidateLoadedMembers();
            this.ReloadOverflowInventoryIntoSquad();
        }
        catch (Exception ex)
        {
            this.ResetRuntimeState(clearProfiles: false);
            this.saveWritesBlocked = true;
            this.Monitor.Log($"Pelican Companions couldn't finish restoring its save state. Writing is disabled for this session to preserve the original data. {ex}", LogLevel.Error);
            this.Warn("state.unavailable");
            return;
        }

        // World restoration is deliberately isolated from the data transaction.
        // Keeping the loaded members active lets the periodic controller repair
        // any custom NPC/map which throws here, instead of abandoning it with a
        // cleared schedule after ResetRuntimeState.
        this.CaptureDailyOriginalNpcStates();
        try
        {
            this.RestorePersistedMemberPositions();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Some persisted companion positions could not be restored and will be retried during normal updates. {ex}", LogLevel.Error);
        }

        try
        {
            this.MaintainCompanionScheduleLocks(stopCurrentRoutes: true);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Some companion schedule locks could not be restored and will be retried during normal updates. {ex}", LogLevel.Error);
        }

        try
        {
            this.ProcessDeferredNpcRestores();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Some dismissed NPCs could not be restored and remain queued for retry. {ex}", LogLevel.Error);
        }

        this.MarkStateDirty();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!Context.IsMainPlayer || this.saveWritesBlocked)
            return;

        try
        {
            this.Helper.Data.WriteSaveData(SaveKey, this.BuildSaveData());
        }
        catch (Exception ex)
        {
            // Don't let one custom item/NPC abort the game's own save. The
            // previous mod data remains intact and the next save can retry.
            this.Monitor.Log($"Pelican Companions couldn't write its save data; the previous state was preserved. {ex}", LogLevel.Error);
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (this.saveWritesBlocked)
            return;

        if (!Context.IsMainPlayer)
        {
            this.RequestStateSnapshot();
            return;
        }

        foreach (PendingCompanionTask task in this.pendingTasks.Values.ToList())
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);

        this.ownerTrails.Clear();
        this.ownerMovementSnapshots.Clear();
        foreach (SquadMemberState member in this.members.Values)
            this.ClearFollowState(member.NpcName);

        // SMAPI can raise DayStarted while the save/shipping menu is still
        // closing, before Game1.OnDayStarted runs NPC marriage duties and other
        // special setup. Capturing or clearing schedules now would record the
        // pre-setup state and let vanilla retake control afterward.
        if (Game1.showingEndOfNightStuff)
        {
            this.pendingDailyCompanionRefresh = true;
            return;
        }

        this.FinishDailyCompanionRefresh();
    }

    private void FinishDailyCompanionRefresh()
    {
        this.pendingDailyCompanionRefresh = false;
        this.CaptureDailyOriginalNpcStates();

        try
        {
            this.RestorePersistedMemberPositions();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Some persisted companion positions could not be restored after the new day and will be retried: {ex}", LogLevel.Error);
        }

        try
        {
            this.MaintainCompanionScheduleLocks(stopCurrentRoutes: true);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Some companion schedule locks could not be restored after the new day and will be retried: {ex}", LogLevel.Error);
        }

        this.MarkStateDirty();
    }

    private void CaptureDailyOriginalNpcStates()
    {
        int today = Game1.Date.TotalDays;
        foreach (SquadMemberState member in this.members.Values)
        {
            if (member.OriginalDayIndex == today)
                continue;

            try
            {
                NPC? npc = this.GetNpcByName(member.NpcName);
                if (npc is null)
                    continue;

                bool hasLoadedSchedule = npc.Schedule is { Count: > 0 }
                    || !string.IsNullOrWhiteSpace(npc.ScheduleKey);
                CaptureOriginalNpcState(member, npc, captureSchedule: hasLoadedSchedule);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not capture the new-day vanilla state for '{member.NpcName}': {ex.Message}", LogLevel.Warn);
            }
        }
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        if (Context.IsOnHostComputer && !Context.IsMainPlayer)
            return;

        this.ResetRuntimeState(clearProfiles: true);
        this.companionQuickHuds?.ResetAllScreens();
        this.companionActionWheels?.ResetAllScreens();
    }

    /// <summary>Clear every save-scoped collection in one place.</summary>
    /// <remarks>
    /// Keeping this centralized prevents state from one farm leaking into the
    /// next one when the player returns to the title screen without restarting
    /// the game.
    /// </remarks>
    private void ResetRuntimeState(bool clearProfiles)
    {
        this.members.Clear();
        this.pendingTasks.Clear();
        this.workTargetReservations.Clear();
        this.sharedWorkTargetReservations.Clear();
        this.taskToggles.Clear();
        this.ownerTrails.Clear();
        this.lastFollowTargets.Clear();
        this.lastFollowTargetDistances.Clear();
        this.lastFollowPathTicks.Clear();
        this.lastFollowProgressPositions.Clear();
        this.activeRecallTargets.Clear();
        this.activeRecallActivatedTicks.Clear();
        this.recoveredFollowTargets.Clear();
        this.followNoProgressTicks.Clear();
        this.lastDisconnectedProbeTicks.Clear();
        this.followRecoveryUntilTick.Clear();
        this.disconnectedFollowRecovery.Clear();
        this.disconnectedFollowBackoffs.Clear();
        this.failedFollowPathTargets.Clear();
        this.lastMovementDebugNoticeTicks.Clear();
        this.followPathStartsRemaining = 0;
        this.taskPathStartsRemaining = 0;
        this.taskPlanningCursor = 0;
        this.taskPreviewCursor = 0;
        this.companionMovementControllers.Clear();
        this.workTargetRetryAfterTicks.Clear();
        this.priorityTaskPlanningMembers.Clear();
        this.suppressedVanillaArrivals.Clear();
        this.reachabilityCache.Clear();
        this.targetPreviewCache.Clear();
        this.ownerMovementSnapshots.Clear();
        this.squadInventory.Clear();
        this.legacyOverflowItems.Clear();
        this.companionHudNotices.Clear();
        this.deferredActionRequests.Clear();
        this.commandReplayGuard.Clear();
        this.vanillaMovementAllowances.Clear();
        this.controlledNpcLeases.Clear();
        this.localOwnerSimulationBlocks.Clear();
        this.followDestinationsThisUpdate.Clear();
        this.workStandReservations.Clear();
        this.deferredNpcRestores.Clear();
        this.nextTaskScanTick = 0;
        this.stateRevision = 0;
        this.lastAppliedStateRevision = -1;
        this.stateSnapshotDirty = true;
        this.stateSnapshotFailureLogged = false;
        this.nextStateSnapshotRetryTick = 0;
        this.saveWritesBlocked = false;
        this.planningFollowDestinations = false;
        this.pendingDailyCompanionRefresh = false;
        this.commandFeedbackTargetPlayerId = null;
        this.replicatedHostRules = null;

        if (clearProfiles)
            this.npcProfiles.Clear();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || this.saveWritesBlocked)
            return;

        if (this.TryHandleCompanionActionWheelInput(e))
            return;

        if (e.Button == SButton.MouseLeft && this.TryHandleCompanionQuickHudClick(GetUiScreenPixels(e.Cursor)))
        {
            this.Helper.Input.Suppress(e.Button);
            return;
        }

        // Keep every companion command out of event, festival, transition, minigame,
        // and existing-menu input. This is intentionally checked before hotkeys so a
        // panel or inventory can't replace a game-owned menu.
        if (Game1.activeClickableMenu is not null || this.IsBlockedGameState(blockForMenu: false))
            return;

        if (this.config.TasksToggleKey.JustPressed())
        {
            this.ToggleTasks(Game1.player);
            return;
        }

        if (this.config.OpenSquadInventoryKey.JustPressed())
        {
            this.OpenSquadInventoryMenu();
            return;
        }

        if (this.config.OpenCompanionPanelKey.JustPressed())
        {
            this.OpenCompanionPanel();
            return;
        }

        if (this.config.RecallAllCompanionsKey.JustPressed())
        {
            this.RecallAllLocalCompanions();
            return;
        }

        if (this.config.RecruitKey.JustPressed())
        {
            this.TryOpenRecruitmentOrManagement(e.Cursor);
            return;
        }

        if (this.config.ManualTaskKey.JustPressed())
        {
            this.TryManualTask(e.Cursor);
            return;
        }

        if (e.Button.IsActionButton())
            this.TryMimicAction(e.Cursor);

        if (e.Button.IsUseToolButton())
            this.TryMimicToolUse(e.Cursor);
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (this.ShouldShowCompanionQuickHud())
            this.companionQuickHuds?.Value.Draw(e.SpriteBatch);

        if (Context.IsWorldReady && Game1.displayHUD)
        {
            this.DrawCompanionHudNotices(e.SpriteBatch);
            this.DrawCompanionMovementDebug(e.SpriteBatch);
            this.DrawCompanionActionWheel(e.SpriteBatch);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        this.CaptureLocalOwnerSimulationBlockState();
        this.UpdateCompanionActionWheel();
        if (this.saveWritesBlocked)
            return;

        if (!Context.IsMainPlayer)
        {
            if (!Context.IsOnHostComputer && e.IsMultipleOf(120) && this.lastAppliedStateRevision < 0)
                this.RequestStateSnapshot();

            if (e.IsMultipleOf(30) && Game1.activeClickableMenu is CompanionPanelMenu)
                this.RefreshCompanionPanelPreviews();

            return;
        }

        if (this.pendingDailyCompanionRefresh)
        {
            if (Game1.showingEndOfNightStuff)
                return;

            this.FinishDailyCompanionRefresh();
        }

        if (e.IsMultipleOf(5))
        {
            this.ProcessDeferredActionRequests();
            this.UpdateOwnerTrails();
            this.ProcessPendingTasks();
        }

        if (e.IsMultipleOf(30)
            && Game1.activeClickableMenu is CompanionPanelMenu
            && Game1.ticks < this.nextTaskScanTick)
            this.RefreshCompanionPanelPreviews();

        // Plan work before the single navigation pass. This prevents a task
        // completion from installing follow -> task controllers in the same tick.
        if (Game1.ticks >= this.nextTaskScanTick)
        {
            this.nextTaskScanTick = Game1.ticks + 60;
            this.UpdateAutonomousTasks();
            this.UpdateDisconnectTimeouts();
        }

        if (e.IsMultipleOf(FollowUpdateIntervalTicks))
            this.UpdateFollowers();

        if (e.IsMultipleOf(300) && !this.IsBlockedGameState(blockForMenu: false))
            this.RestorePersistedMemberPositions(logFailures: false);

        if (e.IsMultipleOf(60))
        {
            if (!this.IsBlockedGameState(blockForMenu: false))
                this.MaintainCompanionScheduleLocks(stopCurrentRoutes: false);
            this.UpdateAmbientDialogue();
        }

        // Repair snapshots make a missed dirty marker self-healing without
        // flooding the network during normal play.
        if (e.IsMultipleOf(600))
            this.MarkStateDirty();

        if (e.IsMultipleOf(30))
            this.SendStateSnapshot();
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || this.saveWritesBlocked)
            return;

        if (!this.IsBlockedGameState(blockForMenu: false))
            this.MaintainCompanionScheduleLocks(stopCurrentRoutes: false);

        if (this.config.FriendshipPointsPerHour <= 0 || e.NewTime % 100 != 0)
            return;

        foreach (SquadMemberState member in this.members.Values)
        {
            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
            if (npc is not null && npc is not Pet && owner is not null)
                owner.changeFriendship(this.config.FriendshipPointsPerHour, npc);
        }
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        IMultiplayerPeerMod? peerMod = e.Peer.GetMod(this.ModManifest.UniqueID);
        if (peerMod is not null && peerMod.Version.ToString() != this.ModManifest.Version.ToString())
            this.Warn("multiplayer.version_mismatch", new { local = this.ModManifest.Version, remote = peerMod.Version });

        if (Context.IsMainPlayer && Context.IsWorldReady)
        {
            if (this.saveWritesBlocked)
                this.SendStateUnavailable(e.Peer.PlayerID);
            else
                this.SendStateSnapshot(e.Peer.PlayerID, force: true);
        }

    }

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (!Context.IsMainPlayer || this.saveWritesBlocked)
            return;

        this.commandReplayGuard.RemovePlayer(e.Peer.PlayerID);
        this.localOwnerSimulationBlocks.Remove(e.Peer.PlayerID);
        this.CancelPendingTasksForOwner(e.Peer.PlayerID, "companion.task_failure.owner_disconnected");

        foreach (SquadMemberState member in this.members.Values.Where(p => p.OwnerId == e.Peer.PlayerID).ToList())
        {
            try
            {
                // Waiting is an explicit persistent order. A disconnect must not
                // dismiss it or silently turn it into Following on reconnect.
                if (member.Mode == CompanionMode.Waiting)
                    continue;

                if (this.config.WarpHomeOnDisconnect)
                {
                    this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
                    continue;
                }

                NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
                if (npc is not null)
                    this.StoreWaitingPosition(member, npc);

                member.Mode = CompanionMode.ParkedForDisconnect;
                member.ParkedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
                this.SetCompanionActivity(member, "companion.status.parked");
                if (npc is not null)
                    this.StopCompanionMovement(npc);
            }
            catch (Exception ex)
            {
                this.SetTaskFailure(member, "companion.task_failure.unexpected_error");
                this.Monitor.Log($"Disconnect handling failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
            }
        }

        this.MarkStateDirty();
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.ModManifest.UniqueID)
            return;

        if (e.Type == MessageCommandFeedback
            && !Context.IsMainPlayer
            && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            try
            {
                CompanionCommandFeedbackMessage feedback = e.ReadAs<CompanionCommandFeedbackMessage>();
                if (feedback is not null && !string.IsNullOrWhiteSpace(feedback.Text) && feedback.Text.Length <= 1024)
                {
                    Game1.addHUDMessage(feedback.IsError
                        ? new HUDMessage(feedback.Text, HUDMessage.error_type)
                        : new HUDMessage(feedback.Text));
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored invalid companion command feedback: {ex.Message}", LogLevel.Warn);
            }
            return;
        }

        if (e.Type == MessageStateUnavailable
            && !Context.IsOnHostComputer
            && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            this.saveWritesBlocked = true;
            this.Warn("state.unavailable");
            return;
        }

        if (Context.IsMainPlayer && this.saveWritesBlocked)
        {
            if (e.Type == MessageStateRequest)
                this.SendStateUnavailable(e.FromPlayerID);
            return;
        }

        if (e.Type == MessageStateSnapshot
            && !Context.IsOnHostComputer
            && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            try
            {
                this.ApplyStateSnapshot(e.ReadAs<SavedModState>());
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid companion state snapshot: {ex}", LogLevel.Error);
            }
            return;
        }

        if (e.Type == MessageStateRequest && Context.IsMainPlayer)
        {
            this.SendStateSnapshot(e.FromPlayerID, force: true);
            return;
        }

        if (e.Type != MessageActionRequest || !Context.IsMainPlayer)
            return;

        SquadActionMessage message;
        try
        {
            message = e.ReadAs<SquadActionMessage>();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Ignored an invalid companion command from player {e.FromPlayerID}: {ex.Message}", LogLevel.Warn);
            return;
        }
        if (message is null)
        {
            this.Monitor.Log($"Ignored a null companion command from player {e.FromPlayerID}.", LogLevel.Warn);
            return;
        }

        if (!this.TryRegisterCommand(e.FromPlayerID, message.CommandId))
        {
            this.SendStateSnapshot(e.FromPlayerID, force: true);
            return;
        }

        if (this.IsOwnerSimulationBlocked(e.FromPlayerID, blockForMenu: false))
        {
            if (this.deferredActionRequests.Count < 64)
                this.deferredActionRequests.Enqueue(new DeferredActionRequest(e.FromPlayerID, message, Game1.ticks));
            else
                this.SendStateSnapshot(e.FromPlayerID, force: true);

            return;
        }

        this.HandleActionRequestSafely(e.FromPlayerID, message);
        this.SendStateSnapshot(force: true);
    }

    private void ProcessDeferredActionRequests()
    {
        if (!Context.IsMainPlayer
            || this.deferredActionRequests.Count == 0
            || this.IsGlobalSimulationBlocked())
        {
            return;
        }

        int requestsToProcess = Math.Min(8, this.deferredActionRequests.Count);
        bool handledAny = false;
        for (int i = 0; i < requestsToProcess; i++)
        {
            DeferredActionRequest request = this.deferredActionRequests.Dequeue();
            if (Game1.ticks - request.ReceivedTick > DeferredActionMaxAgeTicks)
                continue;

            if (this.IsOwnerSimulationBlocked(request.PlayerId, blockForMenu: false))
            {
                this.deferredActionRequests.Enqueue(request);
                continue;
            }

            this.HandleActionRequestSafely(request.PlayerId, request.Message);
            handledAny = true;
        }

        if (handledAny)
            this.SendStateSnapshot(force: true);
    }

    private void HandleActionRequest(long playerId, SquadActionMessage message)
    {
        string action = message.Action ?? "";
        message.NpcName ??= "";
        message.Argument ??= "";
        message.LocationName ??= "";
        message.ExpectedItemToken ??= "";
        if (action.Length > 64
            || message.NpcName.Length > 128
            || message.Argument.Length > 512
            || message.LocationName.Length > 256
            || message.ExpectedItemToken.Length > 128)
        {
            return;
        }

        if (action is "ManualTask" or "MimicToolUse" or "MimicAction" or "ContextTask" or "MoveToWait")
        {
            Farmer? owner = this.GetOwnerFarmer(playerId);
            if (owner?.currentLocation is null
                || !string.Equals(owner.currentLocation.NameOrUniqueName, message.LocationName, StringComparison.Ordinal))
            {
                if (action is "ContextTask" or "MoveToWait")
                    this.Warn("multiplayer.command_stale");
                return;
            }
        }

        switch (action)
        {
            case "Recruit":
                NPC? npc = Game1.getCharacterFromName(message.NpcName, mustBeVillager: false, includeEventActors: false);
                if (npc is not null)
                    this.TryRecruit(npc, playerId, showPrompt: false);
                break;

            case "Dismiss":
                this.DismissMember(message.NpcName, ownerOverride: playerId);
                break;

            case "DismissAll":
                this.DismissAll(playerId);
                break;

            case "Wait":
                if (this.members.TryGetValue(message.NpcName, out SquadMemberState? waitingMember)
                    && this.CanOwnerMutate(waitingMember, playerId))
                {
                    NPC? waitingNpc = this.GetNpcByName(message.NpcName);
                    if (waitingNpc?.currentLocation?.NameOrUniqueName == message.LocationName
                        && !string.IsNullOrWhiteSpace(message.LocationName))
                    {
                        this.SetWaiting(message.NpcName, playerId);
                    }
                    else
                    {
                        this.Warn("multiplayer.command_stale");
                    }
                }
                break;

            case "Resume":
                this.ResumeFollowing(message.NpcName, playerId);
                break;

            case "Recall":
                this.RecallCompanion(message.NpcName, playerId, showMessage: true);
                break;

            case "RecallAll":
                this.RecallAllCompanions(playerId, showMessage: true);
                break;

            case "SetTasksEnabled":
                if (message.DesiredEnabled is bool tasksEnabled)
                    this.SetTasksEnabled(playerId, tasksEnabled);
                break;

            case "ManualTask":
                this.TryManualTask(playerId, new Vector2(message.TileX, message.TileY));
                break;

            case "ContextTask":
                if (Enum.TryParse(message.Argument, ignoreCase: true, out CompanionTaskKind contextKind)
                    && Enum.IsDefined(contextKind))
                {
                    this.TryAssignContextTask(
                        playerId,
                        string.IsNullOrWhiteSpace(message.NpcName) ? null : message.NpcName,
                        contextKind,
                        message.LocationName,
                        new Vector2(message.TileX, message.TileY),
                        message.ExpectedItemToken);
                }
                break;

            case "MoveToWait":
                this.TryMoveCompanionToWait(
                    playerId,
                    message.NpcName,
                    message.LocationName,
                    new Vector2(message.TileX, message.TileY));
                break;

            case "MimicToolUse":
                this.TryMimicToolUse(playerId, new Vector2(message.TileX, message.TileY));
                break;

            case "MimicAction":
                this.TryMimicAction(playerId, new Vector2(message.TileX, message.TileY));
                break;

            case "SetQuickWork":
                if (message.DesiredEnabled is bool quickWorkEnabled
                    && this.members.TryGetValue(message.NpcName, out SquadMemberState? quickMember))
                {
                    this.SetCompanionQuickWork(quickMember, playerId, quickWorkEnabled);
                }
                break;

            case "SetDirective":
                if (message.DesiredEnabled is bool directiveEnabled
                    && this.members.TryGetValue(message.NpcName, out SquadMemberState? directiveMember)
                    && Enum.TryParse(message.Argument, ignoreCase: true, out CompanionDirective directive)
                    && Enum.IsDefined(directive))
                {
                    this.SetCompanionDirective(directiveMember, directive, playerId, directiveEnabled);
                }
                break;

            case "UnlockSkill":
                if (this.members.TryGetValue(message.NpcName, out SquadMemberState? skillMember))
                    this.TryUnlockCompanionSkill(skillMember, message.Argument, playerId);
                break;

            case "WithdrawCompanionSavedItem":
                if (this.members.TryGetValue(message.NpcName, out SquadMemberState? itemMember))
                {
                    this.WithdrawCompanionInventorySavedItem(
                        itemMember,
                        message.Index,
                        message.Argument,
                        playerId,
                        message.ExpectedItemToken);
                }
                break;

            case "WithdrawAllCompanionItems":
                if (this.members.TryGetValue(message.NpcName, out SquadMemberState? allItemsMember))
                    this.WithdrawAllCompanionInventoryItems(allItemsMember, playerId);
                break;

            case "WithdrawSquadInventory":
                this.WithdrawSquadInventory(playerId);
                break;

            default:
                return;
        }
    }

    private void HandleActionRequestSafely(long playerId, SquadActionMessage message)
    {
        long? previousFeedbackTarget = this.commandFeedbackTargetPlayerId;
        this.commandFeedbackTargetPlayerId = playerId;
        try
        {
            this.HandleActionRequest(playerId, message);
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Rejected companion command '{message.Action}' from player {playerId} after an unexpected error. {ex}",
                LogLevel.Error);
            this.Warn("multiplayer.command_failed");
        }
        finally
        {
            this.commandFeedbackTargetPlayerId = previousFeedbackTarget;
        }
    }
}
