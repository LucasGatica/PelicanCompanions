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
    private bool ShouldShowCompanionQuickHud()
    {
        return this.config.ShowCompanionQuickHud
            && Game1.displayHUD
            && !this.IsBlockedGameState(blockForMenu: true)
            && this.GetLocalMembers().Any();
    }

    private bool TryHandleCompanionQuickHudClick(Vector2 screenPixels)
    {
        return this.ShouldShowCompanionQuickHud()
            && this.companionQuickHuds is not null
            && this.companionQuickHuds.Value.TryHandleClick(screenPixels);
    }

    private bool IsCompanionQuickWorkActive(SquadMemberState member)
    {
        return member.Mode == CompanionMode.Following
            && (this.HasActiveWorkDirective(member)
            || (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task)
                && task.Kind != CompanionTaskKind.MovingToWait
                && !task.UsesConfiguredAutonomy)
            || (!Context.IsOnHostComputer && member.CurrentWorkIsDirect));
    }

    private void ToggleCompanionQuickWork(SquadMemberState member)
    {
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "SetQuickWork",
                member.NpcName,
                desiredEnabled: !this.IsCompanionQuickWorkActive(member));
            return;
        }

        this.ToggleCompanionQuickWork(member, Game1.player.UniqueMultiplayerID);
    }

    private void ToggleCompanionQuickWork(SquadMemberState member, long ownerId)
    {
        this.SetCompanionQuickWork(member, ownerId, !this.IsCompanionQuickWorkActive(member));
    }

    private void SetCompanionQuickWork(SquadMemberState member, long ownerId, bool enabled)
    {
        if (!this.CanOwnerMutate(member, ownerId))
            return;

        bool isActive = this.IsCompanionQuickWorkActive(member);
        if (!enabled)
        {
            this.priorityTaskPlanningMembers.Remove(member.NpcName);
            if (!isActive)
                return;

            member.SearchWood = false;
            member.SearchMining = false;
            member.ClearArea = false;
            this.RemovePendingTask(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.following");
            this.ClearCompanionTarget(member);
            this.InvalidateTargetPreviews();
            this.MarkStateDirty();
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Info("companion.quick.work_disabled", new { npc = member.DisplayName });
            return;
        }

        bool restoredWorkState = false;
        bool reenabledOwnerTasks = false;
        if (!this.AreTasksEnabled(ownerId))
        {
            this.taskToggles[ownerId] = true;
            restoredWorkState = true;
            reenabledOwnerTasks = true;
        }

        if (member.Mode != CompanionMode.Following)
        {
            this.ResumeFollowing(member.NpcName, ownerId, showMessage: false);
            restoredWorkState = true;
        }

        if (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? movementTask)
            && movementTask.Kind == CompanionTaskKind.MovingToWait)
        {
            this.RemovePendingTask(movementTask);
            restoredWorkState = true;
        }

        // Even if the selected companion is already working, an explicit Work
        // command must restore the owner's global task gate before returning.
        if (isActive)
        {
            if (restoredWorkState)
            {
                this.priorityTaskPlanningMembers.Add(member.NpcName);
                this.nextTaskScanTick = Game1.ticks + 1;
                this.MarkStateDirty();
                if (this.ShouldShowFeedbackFor(ownerId))
                {
                    this.Info(reenabledOwnerTasks
                        ? "companion.quick.work_enabled_tasks"
                        : "companion.quick.work_enabled_specialty", new
                    {
                        npc = member.DisplayName,
                        specialty = this.Tr($"companion.specialty.{member.PreferredWorkSpecialty}")
                    });
                }
            }

            return;
        }

        if (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? autonomousTask)
            && autonomousTask.UsesConfiguredAutonomy)
        {
            this.RemovePendingTask(autonomousTask, returning: false);
        }

        this.ApplyPreferredWorkSpecialty(member);
        this.priorityTaskPlanningMembers.Add(member.NpcName);
        // Keep the input/multiplayer command frame state-only. Planning starts
        // on a later update and path creation is budgeted by ProcessPendingTasks.
        this.nextTaskScanTick = Game1.ticks + 1;
        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
        {
            this.Info(reenabledOwnerTasks
                ? "companion.quick.work_enabled_tasks"
                : "companion.quick.work_enabled_specialty", new
            {
                npc = member.DisplayName,
                specialty = this.Tr($"companion.specialty.{member.PreferredWorkSpecialty}")
            });
        }
    }

    private void FollowCompanionFromQuickHud(SquadMemberState member)
    {
        this.RecallCompanion(member.NpcName, Game1.player.UniqueMultiplayerID, showMessage: true);
    }

    private void RecallAllLocalCompanions()
    {
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("RecallAll");
            return;
        }

        this.RecallAllCompanions(Game1.player.UniqueMultiplayerID, showMessage: true);
    }

    private void RecallAllCompanions(long ownerId, bool showMessage)
    {
        List<SquadMemberState> ownedMembers = this.members.Values.Where(member => member.OwnerId == ownerId).ToList();
        if (ownedMembers.Count == 0)
        {
            if (showMessage && this.ShouldShowFeedbackFor(ownerId))
                this.Warn("commands.no_followers");
            return;
        }

        int recalled = 0;
        foreach (SquadMemberState member in ownedMembers)
        {
            if (this.RecallCompanion(member.NpcName, ownerId, showMessage: false))
                recalled++;
        }

        if (showMessage && this.ShouldShowFeedbackFor(ownerId) && recalled > 0)
            this.Info("companion.quick.recall_all", new { count = recalled });
    }

    private bool RecallCompanion(string npcName, long ownerId, bool showMessage)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member) || !this.CanOwnerMutate(member, ownerId))
            return false;

        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Recall", npcName);
            return true;
        }

        bool hadPendingTask = this.pendingTasks.ContainsKey(member.NpcName);
        bool hadDirective = this.HasActiveWorkDirective(member);
        bool wasWaiting = member.Mode != CompanionMode.Following;
        bool wasStuck = member.CurrentActivityKey == "companion.status.stuck";
        bool wasReturning = member.CurrentActivityKey == "companion.status.returning";
        bool hadActiveRecall = this.activeRecallTargets.ContainsKey(member.NpcName);
        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        bool sameLocation = npc is not null && owner is not null && npc.currentLocation == owner.currentLocation;
        float ownerDistance = sameLocation
            ? Vector2.Distance(NormalizeTile(npc!.Tile), NormalizeTile(owner!.Tile))
            : float.MaxValue;
        bool canActivateRecall = npc is not null && owner is not null;
        bool mustResetNavigation = hadPendingTask
            || hadDirective
            || wasWaiting
            || FollowNavigationPolicy.ShouldResetForRecall(
                sameLocation,
                ownerDistance,
                MaxCompanionDistanceTiles,
                wasStuck || wasReturning || hadActiveRecall);
        bool shouldReturn = hadPendingTask
            || hadDirective
            || wasWaiting
            || wasStuck
            || wasReturning
            || hadActiveRecall
            || canActivateRecall;

        member.SearchWood = false;
        member.SearchMining = false;
        member.ClearArea = false;
        this.RemovePendingTask(member.NpcName);
        this.ReleaseWorkTargetsForNpc(member.NpcName);
        this.ClearCompanionTarget(member);

        member.Mode = CompanionMode.Following;
        member.WaitingLocationName = null;
        member.ParkedAtUtcTicks = 0;

        if (hadPendingTask || hadDirective)
            this.SetTaskFailure(member, "companion.task_failure.recalled");

        if (canActivateRecall && npc is not null)
        {
            if (mustResetNavigation && (npc.controller is not null || this.IsOwnedCompanionController(npc)))
                this.StopCompanionMovement(npc);

            // The input handler only changes state. Route planning is deferred
            // to UpdateFollowers so opening/confirming the wheel can't stall the
            // current frame on large or heavily modded maps.
            if (mustResetNavigation)
            {
                this.ClearFollowNavigationState(member.NpcName);
            }
            else
            {
                this.lastFollowPathTicks.Remove(member.NpcName);
                this.followNoProgressTicks.Remove(member.NpcName);
                this.recoveredFollowTargets.Remove(member.NpcName);
                this.lastDisconnectedProbeTicks.Remove(member.NpcName);
                this.disconnectedFollowRecovery.Remove(member.NpcName);
                this.disconnectedFollowBackoffs.Remove(member.NpcName);
            }

            this.activeRecallTargets[member.NpcName] = NormalizeTile(npc.Tile);
            this.activeRecallActivatedTicks[member.NpcName] = Game1.ticks;
        }
        else if (!hadActiveRecall && mustResetNavigation)
        {
            if (npc is not null && (npc.controller is not null || this.IsOwnedCompanionController(npc)))
                this.StopCompanionMovement(npc);
            this.ClearFollowNavigationState(member.NpcName);
        }

        bool recallActive = this.activeRecallTargets.ContainsKey(member.NpcName);
        this.SetCompanionActivity(
            member,
            recallActive ? "companion.status.returning" : "companion.status.following");

        if (showMessage && shouldReturn)
        {
            this.Info(
                recallActive ? "companion.quick.returning" : "companion.quick.following",
                new { npc = member.DisplayName });
        }

        this.MarkStateDirty();

        return shouldReturn;
    }

    private bool TryFindRecallTargetTile(string npcName, GameLocation location, Vector2 ownerTile, Vector2 npcTile, out Vector2 targetTile)
    {
        ownerTile = NormalizeTile(ownerTile);
        npcTile = NormalizeTile(npcTile);

        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, MaxCompanionDistanceTiles)
            .Where(candidate => candidate != ownerTile && candidate != npcTile)
            .Where(candidate => Vector2.Distance(ownerTile, NormalizeTile(candidate)) <= RecallArrivalDistance)
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => !this.IsFollowDestinationReserved(location, candidate))
            .Where(candidate => !this.IsFollowPathTargetBackedOff(npcName, location, candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, ownerTile))
            .ThenBy(candidate => Vector2.Distance(candidate, npcTile)))
        {
            targetTile = candidate;
            return true;
        }

        targetTile = npcTile;
        return false;
    }

    private void OpenCompanionPanel(string? selectedNpcName = null)
    {
        this.RefreshCompanionPanelPreviews();
        Game1.activeClickableMenu = new CompanionPanelMenu(
            getMembers: () => this.GetLocalMembers().ToList(),
            selectedNpcName: selectedNpcName,
            getNpc: this.GetNpcByName,
            translate: this.Tr,
            getStatusText: this.GetCompanionStatusText,
            getSummaryLines: this.GetCompanionPanelSummaryLines,
            getDetailLines: this.GetCompanionDetailLines,
            getMapInfo: this.GetCompanionMapInfo,
            getDirectivePreviewText: this.GetDirectivePreviewText,
            getInventoryItems: this.GetCompanionInventoryItems,
            withdrawInventoryItem: this.WithdrawCompanionInventoryItem,
            withdrawAllInventoryItems: this.WithdrawAllCompanionInventoryItems,
            toggleDirective: this.ToggleCompanionDirective,
            unlockSkill: this.TryUnlockCompanionSkill,
            isProgressionEnabled: this.IsCompanionProgressionEnabled,
            toggleWaiting: this.ToggleWaitingFromPanel,
            recallMember: member => this.RecallCompanion(member.NpcName, member.OwnerId, showMessage: true),
            dismissMember: this.ConfirmDismissFromPanel,
            inventorySlots: this.GetCompanionInventoryCapacity());
    }

    private void ToggleWaitingFromPanel(SquadMemberState member)
    {
        if (member.Mode == CompanionMode.Following)
            this.SetWaiting(member.NpcName, member.OwnerId);
        else
            this.ResumeFollowing(member.NpcName, member.OwnerId);
    }

    private void ConfirmDismissFromPanel(SquadMemberState member)
    {
        if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            return;

        Game1.activeClickableMenu = null;
        Game1.currentLocation.createQuestionDialogue(
            this.Tr("management.dismiss_confirm", new { npc = member.DisplayName }),
            new[]
            {
                new Response("Dismiss", this.Tr("management.dismiss")),
                new Response("Cancel", this.Tr("generic.cancel"))
            },
            (_, answer) =>
            {
                if (answer == "Dismiss")
                    this.DismissMember(member.NpcName);
                else
                    this.OpenCompanionPanel(member.NpcName);
            });
    }

    private IEnumerable<SquadMemberState> GetLocalMembers()
    {
        return this.members.Values
            .Where(p => p.OwnerId == Game1.player.UniqueMultiplayerID)
            .OrderBy(p => p.NpcName, StringComparer.OrdinalIgnoreCase);
    }

    private NPC? GetNpcByName(string npcName)
    {
        return Game1.getCharacterFromName(npcName, mustBeVillager: false, includeEventActors: false);
    }

    private string GetCompanionStatusText(SquadMemberState member)
    {
        if (this.IsBlockedGameState(blockForMenu: false))
            return this.Tr("companion.status.blocked");

        if (member.LastFailureReasonKey == "companion.task_failure.npc_missing")
            return this.Tr("companion.status.unavailable");

        if (member.Mode == CompanionMode.Waiting)
            return this.Tr("companion.status.waiting");

        if (member.Mode == CompanionMode.ParkedForDisconnect)
            return this.Tr("companion.status.parked");

        if (member.CurrentActivityKey == "companion.status.stuck")
            return this.Tr("companion.status.stuck");

        if (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? pendingTask)
            && pendingTask.Kind == CompanionTaskKind.MovingToWait)
        {
            return this.Tr("companion.status.moving_to_wait");
        }

        if (this.pendingTasks.ContainsKey(member.NpcName))
            return this.Tr("companion.status.working");

        if (member.CurrentActivityKey == "companion.status.moving_to_wait")
            return this.Tr("companion.status.moving_to_wait");

        if (member.CurrentActivityKey == "companion.status.working")
            return this.Tr("companion.status.working");

        if (member.CurrentActivityKey == "companion.status.returning")
            return this.Tr("companion.status.returning");

        if (member.Inventory.Count >= this.GetCompanionInventoryCapacity())
            return this.Tr("companion.status.inventory_full");

        return this.Tr("companion.status.following");
    }

    private IReadOnlyList<string> GetCompanionPanelSummaryLines()
    {
        List<SquadMemberState> localMembers = this.GetLocalMembers().ToList();
        int working = localMembers.Count(p => (this.pendingTasks.TryGetValue(p.NpcName, out PendingCompanionTask? task)
                && task.Kind != CompanionTaskKind.MovingToWait)
            || p.CurrentActivityKey == "companion.status.working");
        int fullInventories = localMembers.Count(p => p.Inventory.Count >= this.GetCompanionInventoryCapacity());
        string tasksState = this.AreTasksEnabled(Game1.player.UniqueMultiplayerID)
            ? this.Tr("companion.panel.tasks_on")
            : this.Tr("companion.panel.tasks_off");
        string safetyState = this.IsBlockedGameState(blockForMenu: false)
            ? this.Tr("companion.panel.blocked")
            : this.Tr("companion.panel.safe");

        return new[]
        {
            this.Tr("companion.panel.summary_members", new { count = localMembers.Count, working }),
            this.Tr("companion.panel.summary_tasks", new { state = tasksState, safety = safetyState }),
            this.Tr("companion.panel.summary_inventory", new { full = fullInventories })
        };
    }

    private CompanionPanelMapInfo GetCompanionMapInfo(SquadMemberState member)
    {
        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null || owner.currentLocation is null)
            return new CompanionPanelMapInfo("companion.map.unavailable", false, 0, 0, 0, 0);

        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        bool sameLocation = npc.currentLocation == owner.currentLocation;
        string statusKey;
        if (member.CurrentActivityKey == "companion.status.stuck")
            statusKey = "companion.map.stuck";
        else if (member.Mode == CompanionMode.Waiting)
            statusKey = "companion.map.waiting";
        else if (member.Mode == CompanionMode.ParkedForDisconnect)
            statusKey = "companion.map.other_location";
        else if (!sameLocation)
            statusKey = "companion.map.other_location";
        else if ((this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? pendingTask)
                && pendingTask.Kind == CompanionTaskKind.MovingToWait)
            || member.CurrentActivityKey == "companion.status.moving_to_wait")
        {
            statusKey = "companion.map.moving_to_wait";
        }
        else if (this.pendingTasks.ContainsKey(member.NpcName) || member.CurrentActivityKey == "companion.status.working")
            statusKey = "companion.map.working";
        else if (member.CurrentActivityKey == "companion.status.returning")
            statusKey = "companion.map.returning";
        else if (Vector2.Distance(ownerTile, npcTile) <= MaxCompanionDistanceTiles)
            statusKey = "companion.map.near";
        else
            statusKey = "companion.map.away";

        return new CompanionPanelMapInfo(
            statusKey,
            sameLocation,
            (int)ownerTile.X,
            (int)ownerTile.Y,
            (int)npcTile.X,
            (int)npcTile.Y);
    }

    private IReadOnlyList<string> GetCompanionDetailLines(SquadMemberState member)
    {
        TargetPreview preview = !string.IsNullOrWhiteSpace(member.PreviewTargetKey)
            && member.PreviewTargetX >= 0
            && member.PreviewTargetY >= 0
            ? new TargetPreview(true, member.PreviewTargetKey, member.PreviewTargetX, member.PreviewTargetY, "")
            : new TargetPreview(
                false,
                "",
                -1,
                -1,
                string.IsNullOrWhiteSpace(member.PreviewReasonKey)
                    ? "companion.preview.inactive"
                    : member.PreviewReasonKey);

        List<string> lines = new()
        {
            this.Tr("companion.detail.status", new { status = this.GetCompanionStatusText(member) }),
            this.Tr("companion.detail.specialty", new
            {
                specialty = this.Tr($"companion.specialty.{member.PreferredWorkSpecialty}")
            })
        };

        if (!string.IsNullOrWhiteSpace(member.CurrentTargetKey) && member.CurrentTargetX >= 0 && member.CurrentTargetY >= 0)
        {
            lines.Add(this.Tr("companion.detail.target", new
            {
                target = this.Tr(member.CurrentTargetKey),
                x = member.CurrentTargetX,
                y = member.CurrentTargetY
            }));
        }
        else
        {
            lines.Add(this.Tr("companion.detail.target_none"));
        }

        lines.Add(preview.HasTarget
            ? this.Tr("companion.detail.preview_target", new
            {
                target = this.Tr(preview.TargetKey),
                x = preview.X,
                y = preview.Y
            })
            : this.Tr("companion.detail.preview_reason", new { reason = this.Tr(preview.ReasonKey) }));

        if (!string.IsNullOrWhiteSpace(member.LastFailureReasonKey))
            lines.Add(this.Tr("companion.detail.last_failure", new { reason = this.Tr(member.LastFailureReasonKey) }));
        else if (!string.IsNullOrWhiteSpace(member.LastTaskResultKey))
            lines.Add(this.Tr("companion.detail.last_result", new { result = this.Tr(member.LastTaskResultKey) }));
        else
            lines.Add(this.Tr("companion.detail.no_result"));

        return lines;
    }

    private void RefreshCompanionPanelPreviews()
    {
        if (!Context.IsWorldReady)
            return;

        List<SquadMemberState> localMembers = this.GetLocalMembers().ToList();
        IReadOnlyList<string> selectedNames = TaskPlanningPolicy.SelectMembers(
            localMembers.Select(member => member.NpcName),
            priorityNames: null,
            this.taskPreviewCursor,
            TaskPlanningBudgetPerScan,
            out this.taskPreviewCursor);
        Dictionary<string, SquadMemberState> membersByName = localMembers.ToDictionary(
            member => member.NpcName,
            StringComparer.OrdinalIgnoreCase);
        foreach (string npcName in selectedNames)
        {
            if (!membersByName.TryGetValue(npcName, out SquadMemberState? member))
                continue;

            this.UpdateTargetPreview(member, this.BuildTargetPreview(member, null));
        }
    }

    private List<Item> GetCompanionInventoryItems(SquadMemberState member)
    {
        List<Item> items = new();
        foreach (SavedItemStack saved in member.Inventory)
        {
            Item? item = this.TryCreateItem(saved);
            if (item is not null)
                items.Add(item);
        }

        return items;
    }

    private bool WithdrawCompanionInventoryItem(SquadMemberState member, int index)
    {
        if (!Context.IsMainPlayer)
        {
            if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID) || index < 0)
                return false;

            if (!this.TryMapVisibleInventoryIndex(member, index, out int savedIndex, out SavedItemStack saved))
                return false;

            this.SendActionRequest(
                "WithdrawCompanionSavedItem",
                member.NpcName,
                saved.QualifiedItemId,
                index: savedIndex,
                expectedItemToken: SavedItemStackIdentity.CreateToken(saved));
            return true;
        }

        return this.WithdrawCompanionInventoryItem(member, index, Game1.player.UniqueMultiplayerID);
    }

    private bool WithdrawCompanionInventoryItem(SquadMemberState member, int index, long ownerId)
    {
        if (!this.CanOwnerMutate(member, ownerId))
            return false;

        if (index < 0 || this.GetOwnerFarmer(ownerId) is null)
            return false;

        if (!this.TryMapVisibleInventoryIndex(member, index, out int savedIndex, out SavedItemStack saved))
            return false;

        return this.WithdrawCompanionInventorySavedItem(
            member,
            savedIndex,
            saved.QualifiedItemId,
            ownerId,
            SavedItemStackIdentity.CreateToken(saved));
    }

    private bool TryMapVisibleInventoryIndex(SquadMemberState member, int visibleIndex, out int savedIndex, out SavedItemStack saved)
    {
        savedIndex = -1;
        saved = null!;
        int currentVisibleIndex = 0;
        for (int i = 0; i < member.Inventory.Count; i++)
        {
            SavedItemStack candidate = member.Inventory[i];
            if (this.TryCreateItem(candidate) is null)
                continue;

            if (currentVisibleIndex++ != visibleIndex)
                continue;

            savedIndex = i;
            saved = candidate;
            return true;
        }

        return false;
    }

    private bool WithdrawCompanionInventorySavedItem(
        SquadMemberState member,
        int savedIndex,
        string expectedItemId,
        long ownerId,
        string? expectedItemToken)
    {
        if (!this.CanOwnerMutate(member, ownerId, showWarning: false))
        {
            return false;
        }

        if (savedIndex < 0 || savedIndex >= member.Inventory.Count)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return false;

        SavedItemStack saved = member.Inventory[savedIndex];
        if (!string.Equals(saved.QualifiedItemId, expectedItemId, StringComparison.Ordinal)
            || !SavedItemStackIdentity.Matches(saved, expectedItemToken))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        Item? item = this.TryCreateItem(saved);
        if (item is null)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("companion.inventory.withdraw_partial");
            return false;
        }

        int originalStack = item.Stack;
        string displayName = item.DisplayName;
        int inventoryBefore = CountInventoryStack(owner, saved.QualifiedItemId);
        Item? notAdded;
        try
        {
            notAdded = owner.addItemToInventory(item);
        }
        catch (Exception ex)
        {
            int transferred = Math.Clamp(CountInventoryStack(owner, saved.QualifiedItemId) - inventoryBefore, 0, originalStack);
            if (transferred >= originalStack)
                member.Inventory.RemoveAt(savedIndex);
            else if (transferred > 0)
                saved.Stack = originalStack - transferred;

            if (transferred > 0)
                this.MarkStateDirty();

            this.Monitor.Log($"Companion inventory withdrawal failed for '{saved.QualifiedItemId}' and was isolated: {ex}", LogLevel.Error);
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("companion.inventory.withdraw_partial");
            return transferred > 0;
        }

        if (notAdded is null)
        {
            member.Inventory.RemoveAt(savedIndex);
            this.MarkStateDirty();
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Info("companion.inventory.withdraw_complete", new { item = displayName });
            return true;
        }

        int remainingStack = Math.Clamp(notAdded.Stack, 0, originalStack);
        bool movedAny = remainingStack < originalStack;
        if (movedAny)
        {
            if (remainingStack == 0)
                member.Inventory.RemoveAt(savedIndex);
            else
                saved.Stack = remainingStack;
            this.MarkStateDirty();
        }
        if (this.ShouldShowFeedbackFor(ownerId))
            this.Warn("companion.inventory.withdraw_partial");
        return movedAny;
    }

    private bool WithdrawAllCompanionInventoryItems(SquadMemberState member)
    {
        if (!Context.IsMainPlayer)
        {
            if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
                return false;

            this.SendActionRequest("WithdrawAllCompanionItems", member.NpcName);
            return true;
        }

        return this.WithdrawAllCompanionInventoryItems(member, Game1.player.UniqueMultiplayerID);
    }

    private bool WithdrawAllCompanionInventoryItems(SquadMemberState member, long ownerId)
    {
        if (!this.CanOwnerMutate(member, ownerId))
            return false;

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return false;

        if (member.Inventory.Count == 0)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Info("companion.inventory.empty");
            return false;
        }

        bool movedAny = false;
        int index = 0;
        while (index < member.Inventory.Count)
        {
            SavedItemStack saved = member.Inventory[index];
            Item? item = this.TryCreateItem(saved);
            if (item is null)
            {
                // Keep unresolved custom items intact. The content pack which
                // defines them may only be temporarily disabled.
                index++;
                continue;
            }

            int originalStack = item.Stack;
            int inventoryBefore = CountInventoryStack(owner, saved.QualifiedItemId);
            Item? notAdded;
            try
            {
                notAdded = owner.addItemToInventory(item);
            }
            catch (Exception ex)
            {
                int transferred = Math.Clamp(CountInventoryStack(owner, saved.QualifiedItemId) - inventoryBefore, 0, originalStack);
                if (transferred >= originalStack)
                    member.Inventory.RemoveAt(index);
                else if (transferred > 0)
                {
                    saved.Stack = originalStack - transferred;
                    index++;
                }
                else
                {
                    index++;
                }

                if (transferred > 0)
                {
                    movedAny = true;
                    this.MarkStateDirty();
                }

                this.Monitor.Log($"Companion inventory withdrawal failed for '{saved.QualifiedItemId}' and was isolated: {ex}", LogLevel.Error);
                continue;
            }

            int remainingStack = Math.Clamp(notAdded?.Stack ?? 0, 0, originalStack);
            int moved = originalStack - remainingStack;
            if (moved <= 0)
            {
                index++;
                continue;
            }

            if (remainingStack == 0)
                member.Inventory.RemoveAt(index);
            else
            {
                saved.Stack = remainingStack;
                index++;
            }

            movedAny = true;
            this.MarkStateDirty();
        }

        if (member.Inventory.Count == 0)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Info("companion.inventory.withdraw_all_complete", new { npc = member.DisplayName });
            return movedAny;
        }

        if (this.ShouldShowFeedbackFor(ownerId))
            this.Warn("companion.inventory.withdraw_partial");
        return movedAny;
    }

    private void ToggleCompanionDirective(SquadMemberState member, CompanionDirective directive)
    {
        if (!Context.IsMainPlayer)
        {
            if (this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            {
                this.SendActionRequest(
                    "SetDirective",
                    member.NpcName,
                    directive.ToString(),
                    desiredEnabled: !IsDirectiveEnabled(member, directive));
            }
            return;
        }

        this.ToggleCompanionDirective(member, directive, Game1.player.UniqueMultiplayerID);
    }

    private void ToggleCompanionDirective(SquadMemberState member, CompanionDirective directive, long ownerId)
    {
        this.SetCompanionDirective(member, directive, ownerId, !IsDirectiveEnabled(member, directive));
    }

    private void SetCompanionDirective(SquadMemberState member, CompanionDirective directive, long ownerId, bool enabled)
    {
        if (!this.CanOwnerMutate(member, ownerId))
            return;

        if (IsDirectiveEnabled(member, directive) == enabled)
            return;

        switch (directive)
        {
            case CompanionDirective.SearchWood:
                member.SearchWood = enabled;
                if (member.SearchWood)
                    member.PreferredWorkSpecialty = CompanionWorkSpecialty.Wood;
                break;

            case CompanionDirective.SearchMining:
                member.SearchMining = enabled;
                if (member.SearchMining)
                    member.PreferredWorkSpecialty = CompanionWorkSpecialty.Mining;
                break;

            case CompanionDirective.ClearArea:
                member.ClearArea = enabled;
                if (member.ClearArea)
                    member.PreferredWorkSpecialty = CompanionWorkSpecialty.ClearArea;
                break;
        }

        bool preferredDirectiveStillActive = member.PreferredWorkSpecialty switch
        {
            CompanionWorkSpecialty.Wood => member.SearchWood,
            CompanionWorkSpecialty.Mining => member.SearchMining,
            _ => member.ClearArea
        };
        if (!preferredDirectiveStillActive && this.HasActiveWorkDirective(member))
        {
            member.PreferredWorkSpecialty = member.ClearArea
                ? CompanionWorkSpecialty.ClearArea
                : member.SearchWood
                    ? CompanionWorkSpecialty.Wood
                    : CompanionWorkSpecialty.Mining;
        }

        if (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? pending)
            && pending.UsesWorkDirective
            && !this.IsPendingTaskAllowedByDirectives(member, pending.Kind))
        {
            this.RemovePendingTask(
                pending,
                "companion.task_failure.directive_disabled",
                returning: !this.HasActiveWorkDirective(member));
        }

        this.InvalidateTargetPreviews();
        if (this.HasActiveWorkDirective(member))
        {
            this.priorityTaskPlanningMembers.Add(member.NpcName);
            this.nextTaskScanTick = Game1.ticks + 1;
            this.UpdateTargetPreview(
                member,
                new TargetPreview(false, "", -1, -1, "companion.preview.planning"));
        }
        else
        {
            this.priorityTaskPlanningMembers.Remove(member.NpcName);
            this.UpdateTargetPreview(
                member,
                new TargetPreview(false, "", -1, -1, "companion.preview.inactive"));
        }
        this.MarkStateDirty();
    }

    private static bool IsDirectiveEnabled(SquadMemberState member, CompanionDirective directive)
    {
        return directive switch
        {
            CompanionDirective.SearchWood => member.SearchWood,
            CompanionDirective.SearchMining => member.SearchMining,
            CompanionDirective.ClearArea => member.ClearArea,
            _ => false
        };
    }

    private bool TryUnlockCompanionSkill(SquadMemberState member, string skillId)
    {
        if (!Context.IsMainPlayer)
        {
            if (!this.CanUnlockCompanionSkill(member, skillId, Game1.player.UniqueMultiplayerID, showWarnings: true))
                return false;

            this.SendActionRequest("UnlockSkill", member.NpcName, skillId);
            return true;
        }

        return this.TryUnlockCompanionSkill(member, skillId, Game1.player.UniqueMultiplayerID);
    }

    private bool TryUnlockCompanionSkill(SquadMemberState member, string skillId, long ownerId)
    {
        if (!this.CanUnlockCompanionSkill(member, skillId, ownerId, showWarnings: this.ShouldShowFeedbackFor(ownerId)))
            return false;

        CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skillId);
        if (skill is null)
            return false;

        member.UnspentSkillPoints -= skill.Cost;
        member.UnlockedSkillIds.Add(skill.Id);
        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
            this.Info("companion.skill.unlocked", new { skill = this.Tr(skill.NameKey), npc = member.DisplayName });
        return true;
    }

    private bool CanUnlockCompanionSkill(SquadMemberState member, string skillId, long ownerId, bool showWarnings)
    {
        if (!this.IsCompanionProgressionEnabled() || !this.CanOwnerMutate(member, ownerId, showWarnings))
            return false;

        CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skillId);
        if (skill is null)
            return false;

        CompanionSkillTreeState state = CompanionSkillTreePolicy.GetState(
            skill,
            member.UnlockedSkillIds,
            member.UnspentSkillPoints,
            progressionEnabled: true);
        if (state == CompanionSkillTreeState.LockedByPrerequisite)
        {
            if (showWarnings)
                this.Warn("companion.skill.locked");
            return false;
        }

        if (state == CompanionSkillTreeState.NeedsPoints)
        {
            if (showWarnings)
                this.Warn("companion.skill.no_points");
            return false;
        }

        return state == CompanionSkillTreeState.Available;
    }
}
