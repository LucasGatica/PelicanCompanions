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

public sealed partial class ModEntry : Mod
{
    private const string SaveKey = "pelican-companions-state";
    private const string NpcConfigAssetKey = "Lucas.PelicanCompanions/NpcConfig";
    private const string MessageActionRequest = "CompanionActionRequest";
    private const int SquadInventoryCapacity = 36;
    private const int MaxTrailPointsPerOwner = 96;
    private const int FollowUpdateIntervalTicks = 5;
    private const int BaseTrailLag = 1;
    private const int TrailLagPerFollower = 2;
    private const int MaxCompanionDistanceTiles = 3;
    private const int SafePlacementSearchRadius = 6;
    private const int StationaryTrailExpiryTicks = 240;
    private const int DefaultCompanionMovementSpeed = 2;
    private const int LumberHitCooldownTicks = 45;
    private const int LumberTaskTimeoutTicks = 900;
    private const int MiningHitCooldownTicks = 45;
    private const int MiningTaskTimeoutTicks = 900;
    private const float StartPathingDistance = 1.15f;
    private const float TaskArrivalDistance = 1.1f;
    private const float FollowTrailMaxOwnerDistance = 2.25f;
    private const int FollowRepathCooldownTicks = 20;
    private const int FollowRecoveryRepathCooldownTicks = 15;
    private const int OwnerStationaryThresholdTicks = 20;
    private const int FollowNoProgressUpdatesThreshold = 18;
    private const int FollowRecoveryDurationTicks = 90;
    private const int MaxFollowReachabilitySearchTiles = 2048;
    private const int TaskNoProgressUpdatesThreshold = 18;
    private const int RecentLootLimit = 5;
    private const int CompanionHudNoticeDurationTicks = 260;
    private const float RecallArrivalDistance = 1.5f;
    private const float FollowProgressTolerance = 0.05f;
    private const float FollowPositionProgressTolerance = 0.03f;
    private const float FollowRetargetDistanceThreshold = 1.25f;
    private static readonly Vector2[] CardinalTileOffsets =
    {
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    };
    private static readonly string[] TilePropertyLayers =
    {
        "Back",
        "Buildings",
        "Front",
        "AlwaysFront"
    };

    private readonly Dictionary<string, SquadMemberState> members = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingCompanionTask> pendingTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> workTargetReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, bool> taskToggles = new();
    private readonly Dictionary<long, List<FollowTrailPoint>> ownerTrails = new();
    private readonly Dictionary<string, Vector2> lastFollowTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> lastFollowTargetDistances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastFollowPathTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vector2> lastFollowProgressPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vector2> activeRecallTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> followNoProgressTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> followRecoveryUntilTick = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastMovementDebugNoticeTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ReachabilityCacheKey, ReachabilityCacheEntry> reachabilityCache = new();
    private readonly Dictionary<TargetPreviewCacheKey, TargetPreviewCacheEntry> targetPreviewCache = new();
    private readonly Dictionary<long, OwnerMovementSnapshot> ownerMovementSnapshots = new();
    private readonly List<Item> squadInventory = new();
    private readonly List<SavedItemStack> legacyOverflowItems = new();
    private readonly List<CompanionHudNotice> companionHudNotices = new();
    private readonly Queue<DeferredActionRequest> deferredActionRequests = new();
    private readonly Random random = new();

    private ModConfig config = new();
    private CompanionQuickHud? companionQuickHud;
    private Dictionary<string, NpcCompanionProfile> npcProfiles = new(StringComparer.OrdinalIgnoreCase);
    private int nextTaskScanTick;

    private readonly record struct FollowTrailPoint(string LocationName, Vector2 Tile, int Tick);
    private readonly record struct OwnerMovementSnapshot(string LocationName, Vector2 Position, int LastMoveTick, int LastObservedTick, bool IsStationary);
    private readonly record struct WorkTarget(CompanionTaskKind Kind, Vector2 Tile, float NpcDistance, float PlayerDistance);
    private readonly record struct TargetPreview(bool HasTarget, string TargetKey, int X, int Y, string ReasonKey);
    private readonly record struct CompanionHudNotice(string NpcName, string Text, string? ItemQualifiedId, int StartedTick, int DurationTicks, Color Accent);
    private readonly record struct ReachabilityCacheKey(string LocationName, int OriginX, int OriginY, int MaxVisitedTiles);
    private readonly record struct ReachabilityCacheEntry(int Tick, Dictionary<Vector2, int> Distances);
    private readonly record struct TargetPreviewCacheKey(
        string NpcName,
        CompanionDirective? SimulatedDirective,
        string LocationName,
        int OwnerX,
        int OwnerY,
        int NpcX,
        int NpcY,
        bool SearchWood,
        bool SearchMining,
        bool ClearArea,
        bool TasksEnabled,
        bool Blocked,
        CompanionMode Mode);
    private readonly record struct TargetPreviewCacheEntry(int Tick, TargetPreview Preview);
    private readonly record struct DeferredActionRequest(long PlayerId, SquadActionMessage Message);

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        this.MigrateConfig();
        this.config.Validate();
        helper.WriteConfig(this.config);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Display.RenderedHud += this.OnRenderedHud;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
        helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;

        this.companionQuickHud = new CompanionQuickHud(
            getMembers: () => this.GetLocalMembers().ToList(),
            getNpc: this.GetNpcByName,
            translate: this.Tr,
            getStatusText: this.GetCompanionStatusText,
            isWorkActive: this.IsCompanionQuickWorkActive,
            getMode: () => this.config.CompanionQuickHudMode,
            getMaxVisibleRows: () => this.config.CompanionQuickHudMaxRows,
            getInventorySlotCount: () => this.config.CompanionInventorySlots,
            toggleWork: this.ToggleCompanionQuickWork,
            follow: this.FollowCompanionFromQuickHud,
            openPanel: member => this.OpenCompanionPanel(member.NpcName));
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterGenericModConfigMenu();
    }

    private void MigrateConfig()
    {
        if (this.config.ConfigVersion < 2)
        {
            this.config.LumberingMode = TaskMode.Mimicking;
            this.config.ConfigVersion = 2;
        }

        if (this.config.ConfigVersion < 3)
        {
            this.config.CompanionQuickHudMode = CompanionQuickHudMode.Detailed;
            this.config.CompanionQuickHudMaxRows = 6;
            this.config.ConfigVersion = 3;
        }

        if (this.config.ConfigVersion < 4)
        {
            // Keep the player's existing formation choice. Adaptive is only the
            // default for newly generated configs.
            this.config.ConfigVersion = 4;
        }
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(NpcConfigAssetKey))
            e.LoadFromModFile<Dictionary<string, NpcCompanionProfile>>("assets/NpcConfig.json", AssetLoadPriority.Exclusive);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.members.Clear();
        this.pendingTasks.Clear();
        this.workTargetReservations.Clear();
        this.taskToggles.Clear();
        this.ownerTrails.Clear();
        this.lastFollowTargets.Clear();
        this.lastFollowTargetDistances.Clear();
        this.lastFollowPathTicks.Clear();
        this.lastFollowProgressPositions.Clear();
        this.activeRecallTargets.Clear();
        this.followNoProgressTicks.Clear();
        this.followRecoveryUntilTick.Clear();
        this.lastMovementDebugNoticeTicks.Clear();
        this.reachabilityCache.Clear();
        this.targetPreviewCache.Clear();
        this.ownerMovementSnapshots.Clear();
        this.squadInventory.Clear();
        this.legacyOverflowItems.Clear();
        this.companionHudNotices.Clear();
        this.deferredActionRequests.Clear();

        SavedModState data = this.Helper.Data.ReadSaveData<SavedModState>(SaveKey) ?? new SavedModState();
        foreach (SquadMemberState member in data.Members ?? Enumerable.Empty<SquadMemberState>())
        {
            if (!string.IsNullOrWhiteSpace(member.NpcName))
            {
                this.legacyOverflowItems.AddRange(this.NormalizeLoadedMember(member));
                this.members[member.NpcName] = member;
            }
        }

        foreach ((string key, bool value) in data.TaskTogglesByPlayer ?? new Dictionary<string, bool>())
        {
            if (long.TryParse(key, out long playerId))
                this.taskToggles[playerId] = value;
        }

        foreach (SavedItemStack saved in data.SquadInventory ?? new List<SavedItemStack>())
        {
            Item? item = this.TryCreateItem(saved);
            if (item is not null)
                this.squadInventory.Add(item);
        }

        foreach (SavedItemStack saved in data.LegacyOverflowItems ?? new List<SavedItemStack>())
        {
            if (!string.IsNullOrWhiteSpace(saved.QualifiedItemId) && saved.Stack > 0)
                this.legacyOverflowItems.Add(saved);
        }

        this.LoadNpcProfiles();
        this.ValidateLoadedMembers();
        this.ReloadOverflowInventoryIntoSquad();
        this.RestorePersistedMemberPositions();
        this.MaintainCompanionScheduleLocks(stopCurrentRoutes: true);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.Helper.Data.WriteSaveData(SaveKey, this.BuildSaveData());
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        foreach (PendingCompanionTask task in this.pendingTasks.Values.ToList())
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);

        this.ownerTrails.Clear();
        this.ownerMovementSnapshots.Clear();
        foreach (SquadMemberState member in this.members.Values)
            this.ClearFollowState(member.NpcName);

        this.RestorePersistedMemberPositions();
        this.MaintainCompanionScheduleLocks(stopCurrentRoutes: true);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.members.Clear();
        this.pendingTasks.Clear();
        this.workTargetReservations.Clear();
        this.taskToggles.Clear();
        this.ownerTrails.Clear();
        this.lastFollowTargets.Clear();
        this.lastFollowTargetDistances.Clear();
        this.lastFollowPathTicks.Clear();
        this.lastFollowProgressPositions.Clear();
        this.activeRecallTargets.Clear();
        this.followNoProgressTicks.Clear();
        this.followRecoveryUntilTick.Clear();
        this.lastMovementDebugNoticeTicks.Clear();
        this.reachabilityCache.Clear();
        this.targetPreviewCache.Clear();
        this.ownerMovementSnapshots.Clear();
        this.squadInventory.Clear();
        this.legacyOverflowItems.Clear();
        this.companionHudNotices.Clear();
        this.deferredActionRequests.Clear();
        this.npcProfiles.Clear();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (e.Button == SButton.MouseLeft && this.TryHandleCompanionQuickHudClick(e.Cursor.GetScaledScreenPixels()))
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
            this.companionQuickHud?.Draw(e.SpriteBatch);

        if (Context.IsWorldReady && Game1.displayHUD)
        {
            this.DrawCompanionHudNotices(e.SpriteBatch);
            this.DrawCompanionMovementDebug(e.SpriteBatch);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (e.IsMultipleOf(5))
        {
            this.ProcessDeferredActionRequests();
            this.UpdateOwnerTrails();
            this.ProcessPendingTasks();
        }

        if (e.IsMultipleOf(FollowUpdateIntervalTicks))
            this.UpdateFollowers();

        if (Game1.ticks >= this.nextTaskScanTick)
        {
            this.nextTaskScanTick = Game1.ticks + 60;
            this.UpdateAutonomousTasks();
            this.UpdateDisconnectTimeouts();
        }

        if (e.IsMultipleOf(60))
        {
            this.MaintainCompanionScheduleLocks(stopCurrentRoutes: false);
            this.UpdateAmbientDialogue();
        }
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        this.MaintainCompanionScheduleLocks(stopCurrentRoutes: false);

        if (this.config.FriendshipPointsPerHour <= 0 || e.NewTime % 100 != 0)
            return;

        foreach (SquadMemberState member in this.members.Values.Where(p => p.OwnerId == Game1.player.UniqueMultiplayerID))
        {
            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            if (npc is not null && npc is not Pet)
                Game1.player.changeFriendship(this.config.FriendshipPointsPerHour, npc);
        }
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        IMultiplayerPeerMod? peerMod = e.Peer.GetMod(this.ModManifest.UniqueID);
        if (peerMod is not null && peerMod.Version.ToString() != this.ModManifest.Version.ToString())
            this.Warn("multiplayer.version_mismatch", new { local = this.ModManifest.Version, remote = peerMod.Version });

    }

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (!Context.IsMainPlayer)
            return;

        this.CancelPendingTasksForOwner(e.Peer.PlayerID, "companion.task_failure.owner_disconnected");

        foreach (SquadMemberState member in this.members.Values.Where(p => p.OwnerId == e.Peer.PlayerID).ToList())
        {
            if (this.config.WarpHomeOnDisconnect)
            {
                this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
                continue;
            }

            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            if (npc is not null)
            {
                this.StoreWaitingPosition(member, npc);
                npc.controller = null;
                ResetCompanionMovementSpeed(npc);
                npc.Halt();
            }

            member.Mode = CompanionMode.ParkedForDisconnect;
            member.ParkedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        }
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.ModManifest.UniqueID || e.Type != MessageActionRequest || !Context.IsMainPlayer)
            return;

        SquadActionMessage message = e.ReadAs<SquadActionMessage>();
        if (this.IsBlockedGameState(blockForMenu: false))
        {
            if (this.deferredActionRequests.Count < 64)
                this.deferredActionRequests.Enqueue(new DeferredActionRequest(e.FromPlayerID, message));

            return;
        }

        this.HandleActionRequest(e.FromPlayerID, message);
    }

    private void ProcessDeferredActionRequests()
    {
        if (!Context.IsMainPlayer
            || this.deferredActionRequests.Count == 0
            || this.IsBlockedGameState(blockForMenu: false))
        {
            return;
        }

        int requestsToProcess = Math.Min(8, this.deferredActionRequests.Count);
        for (int i = 0; i < requestsToProcess; i++)
        {
            DeferredActionRequest request = this.deferredActionRequests.Dequeue();
            this.HandleActionRequest(request.PlayerId, request.Message);
        }
    }

    private void HandleActionRequest(long playerId, SquadActionMessage message)
    {
        NPC? npc = Game1.getCharacterFromName(message.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null)
            return;

        switch (message.Action)
        {
            case "Recruit":
                this.TryRecruit(npc, playerId, showPrompt: false);
                break;

            case "Dismiss":
                this.DismissMember(npc.Name, ownerOverride: playerId);
                break;

            case "Wait":
                this.SetWaiting(npc.Name, playerId);
                break;

            case "Resume":
                this.ResumeFollowing(npc.Name, playerId);
                break;

            case "Recall":
                this.RecallCompanion(npc.Name, playerId, showMessage: false);
                break;
        }
    }

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
        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Recruit", npc.Name);
            this.Info("recruitment.request_sent", new { npc = npc.displayName });
            return;
        }

        EligibilityResult eligibility = this.CanRecruit(npc, ownerId);
        if (!eligibility.Allowed)
        {
            if (eligibility.ReasonKey == "recruitment.friendship_low")
                this.Say(npc, "FriendshipTooLow", force: true);

            this.Warn(eligibility.ReasonKey, new { npc = npc.displayName, required = this.config.FriendshipRequirement });
            return;
        }

        if (showPrompt)
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
                        // Run the full eligibility check again at the commit point.
                        this.TryRecruit(npc, ownerId, showPrompt: false);
                    }
                });
            return;
        }

        this.AddMember(npc, ownerId);
    }

    private EligibilityResult CanRecruit(NPC npc, long ownerId)
    {
        if (this.IsBlockedGameState(blockForMenu: false))
            return new EligibilityResult(false, "recruitment.blocked_state");

        if (this.members.TryGetValue(npc.Name, out SquadMemberState? existing))
        {
            return existing.OwnerId == ownerId
                ? new EligibilityResult(false, "recruitment.already_yours")
                : new EligibilityResult(false, "recruitment.already_other");
        }

        if (this.members.Values.Count(p => p.OwnerId == ownerId) >= this.config.MaxSquadSize)
            return new EligibilityResult(false, "recruitment.squad_full");

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return new EligibilityResult(false, "recruitment.unsupported");

        if (!this.IsSupportedTarget(npc, owner))
            return new EligibilityResult(false, "recruitment.unsupported");

        if (!this.MeetsFriendshipRequirement(npc, owner))
            return new EligibilityResult(false, "recruitment.friendship_low");

        return EligibilityResult.Success;
    }

    private void AddMember(NPC npc, long ownerId)
    {
        SquadMemberState member = new()
        {
            NpcName = npc.Name,
            DisplayName = npc.displayName,
            OwnerId = ownerId,
            Mode = CompanionMode.Following,
            OriginalLocationName = npc.currentLocation?.NameOrUniqueName
        };
        this.members[npc.Name] = member;
        this.RemovePendingTask(npc.Name);
        this.DisableNpcSchedule(npc, stopCurrentRoute: true);

        this.Info("recruitment.joined", new { npc = npc.displayName });
        this.Say(npc, "Recruit", force: true);
        this.UpdateFollower(member, npc, this.GetOwnerFarmer(ownerId) ?? Game1.player, forceCatchUp: true);
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

        if (this.config.UseSquadInventory || this.squadInventory.Count > 0)
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
        foreach (string npcName in this.members.Values.Where(p => p.OwnerId == ownerId).Select(p => p.NpcName).ToList())
            this.DismissMember(npcName, ownerOverride: ownerId);

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
        if (npc is not null)
        {
            npc.controller = null;
            ResetCompanionMovementSpeed(npc);
            npc.Halt();
            this.Say(npc, "Dismiss", force: true);
            this.RestoreNpcSchedule(npc);
        }

        this.members.Remove(npcName);
        this.ClearFollowState(npcName);
        this.RemovePendingTask(npcName);
        if (!silent)
            this.Info("recruitment.dismissed", new { npc = member.DisplayName });
    }

    private void SetWaiting(string npcName, long ownerId)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member) || !this.CanOwnerMutate(member, ownerId))
            return;

        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Wait", npcName);
            return;
        }

        this.RemovePendingTask(npcName);
        NPC? npc = Game1.getCharacterFromName(npcName, mustBeVillager: false, includeEventActors: false);
        if (npc is not null)
        {
            this.StoreWaitingPosition(member, npc);
            npc.controller = null;
            ResetCompanionMovementSpeed(npc);
            npc.Halt();
        }

        member.Mode = CompanionMode.Waiting;
        this.ClearCompanionTarget(member);
        this.SetCompanionActivity(member, "companion.status.waiting");
        this.Info("management.waiting", new { npc = member.DisplayName });
    }

    private void ResumeFollowing(string npcName, long ownerId)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member) || !this.CanOwnerMutate(member, ownerId))
            return;

        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Resume", npcName);
            return;
        }

        member.Mode = CompanionMode.Following;
        member.WaitingLocationName = null;
        member.ParkedAtUtcTicks = 0;
        this.SetCompanionActivity(member, "companion.status.following");
        this.Info("management.resumed", new { npc = member.DisplayName });
    }

    private void StoreWaitingPosition(SquadMemberState member, NPC npc)
    {
        member.WaitingLocationName = npc.currentLocation?.NameOrUniqueName;
        member.WaitingTileX = npc.Tile.X;
        member.WaitingTileY = npc.Tile.Y;
    }

    private void UpdateFollowers()
    {
        if (this.IsBlockedGameState(blockForMenu: true))
            return;

        foreach (SquadMemberState member in this.members.Values.ToList())
        {
            if (member.Mode == CompanionMode.ParkedForDisconnect)
            {
                Farmer? reconnectedOwner = this.GetOwnerFarmer(member.OwnerId);
                if (reconnectedOwner is null)
                    continue;

                member.Mode = CompanionMode.Following;
                member.ParkedAtUtcTicks = 0;
                member.WaitingLocationName = null;
                this.SetCompanionActivity(member, "companion.status.returning");
                this.ClearFollowState(member.NpcName);
            }

            if (member.Mode != CompanionMode.Following)
                continue;

            if (this.pendingTasks.ContainsKey(member.NpcName))
                continue;

            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
            if (npc is null)
            {
                this.Monitor.Log($"Dropping missing companion '{member.NpcName}' from state.", LogLevel.Warn);
                this.legacyOverflowItems.AddRange(member.Inventory);
                this.members.Remove(member.NpcName);
                this.ClearFollowState(member.NpcName);
                this.RemovePendingTask(member.NpcName);
                continue;
            }

            if (owner is null)
            {
                this.RemovePendingTask(member.NpcName);
                this.StoreWaitingPosition(member, npc);
                member.Mode = CompanionMode.ParkedForDisconnect;
                member.ParkedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
                npc.controller = null;
                ResetCompanionMovementSpeed(npc);
                npc.Halt();
                continue;
            }

            this.UpdateFollower(member, npc, owner, forceCatchUp: false);
        }
    }

    private void UpdateFollower(SquadMemberState member, NPC npc, Farmer owner, bool forceCatchUp)
    {
        this.RecordOwnerTrailPoint(owner);

        GameLocation ownerLocation = owner.currentLocation;
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        bool sameLocation = npc.currentLocation == ownerLocation;
        bool ownerStationary = sameLocation && this.IsOwnerStationary(owner);
        float ownerDistance = sameLocation ? Vector2.Distance(npcTile, ownerTile) : 99f;
        bool useOwnerTrail = !forceCatchUp
            && !ownerStationary
            && ownerDistance <= FollowTrailMaxOwnerDistance;
        Vector2 desiredTile = this.FindCompanionTile(
            ownerLocation,
            owner,
            this.GetOwnerSlot(member),
            useOwnerTrail,
            originTile: sameLocation ? npcTile : null);
        float distance = sameLocation ? GetFollowDistance(npc, desiredTile) : 99f;

        ResetCompanionMovementSpeed(npc);

        if (forceCatchUp)
        {
            this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks.Remove(member.NpcName);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions.Remove(member.NpcName);
        }

        if (this.followRecoveryUntilTick.TryGetValue(member.NpcName, out int recoveryUntilTick))
        {
            if (Game1.ticks >= recoveryUntilTick)
            {
                this.followRecoveryUntilTick.Remove(member.NpcName);
                if (member.CurrentActivityKey == "companion.status.stuck")
                    this.SetCompanionActivity(member, "companion.status.returning");
            }
        }

        if (!sameLocation)
        {
            this.activeRecallTargets.Remove(member.NpcName);
            this.PlaceNpc(npc, ownerLocation, desiredTile);
            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.lastFollowTargetDistances[member.NpcName] = 0f;
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions.Remove(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.following");
            this.ShowMovementDebugNotice(member, "companion.movement_debug.map_repositioned", new { npc = member.DisplayName });
            return;
        }

        bool recallActive = this.activeRecallTargets.ContainsKey(member.NpcName);
        if (recallActive && ownerDistance <= RecallArrivalDistance)
        {
            this.activeRecallTargets.Remove(member.NpcName);
            recallActive = false;
        }

        if (recallActive)
        {
            if (this.TryFindRecallTargetTile(ownerLocation, ownerTile, npcTile, out Vector2 recallTarget))
            {
                desiredTile = recallTarget;
                this.activeRecallTargets[member.NpcName] = recallTarget;
                distance = GetFollowDistance(npc, desiredTile);
            }
            else
            {
                this.activeRecallTargets.Remove(member.NpcName);
                recallActive = false;
            }
        }

        if (ownerStationary && ownerDistance <= MaxCompanionDistanceTiles && distance <= StartPathingDistance)
        {
            npc.controller = null;
            npc.Halt();
            this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks.Remove(member.NpcName);
            this.followRecoveryUntilTick.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
            this.lastFollowTargets[member.NpcName] = npcTile;
            this.lastFollowTargetDistances[member.NpcName] = ownerDistance;

            if (member.CurrentActivityKey == "companion.status.returning")
                this.SetCompanionActivity(member, "companion.status.following");
            else if (member.CurrentActivityKey == "companion.status.stuck")
                this.SetCompanionActivity(member, "companion.status.following");

            return;
        }

        if (!recallActive)
            desiredTile = this.GetStableFollowTarget(member, npc, ownerLocation, ownerTile, npcTile, desiredTile, forceCatchUp);

        distance = GetFollowDistance(npc, desiredTile);
        bool targetChanged = !this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 lastTarget) || lastTarget != desiredTile;
        bool desiredTileIsCurrentTile = desiredTile == npcTile;
        bool shouldMove = distance > StartPathingDistance || (ownerDistance > 1.5f && !desiredTileIsCurrentTile);
        this.UpdateFollowProgressCounter(member, npc, shouldMove);

        if (!this.followRecoveryUntilTick.ContainsKey(member.NpcName)
            && this.followNoProgressTicks.TryGetValue(member.NpcName, out int stalledTicks)
            && stalledTicks >= FollowNoProgressUpdatesThreshold)
        {
            this.followRecoveryUntilTick[member.NpcName] = Game1.ticks + FollowRecoveryDurationTicks;
            this.followNoProgressTicks[member.NpcName] = 0;
            this.activeRecallTargets.Remove(member.NpcName);
            this.lastFollowPathTicks.Remove(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.stuck");
            this.SetTaskFailure(member, "companion.task_failure.path_recovery");
            this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
        }

        bool isRecovery = this.followRecoveryUntilTick.TryGetValue(member.NpcName, out int activeRecoveryUntilTick) && Game1.ticks < activeRecoveryUntilTick;
        if (isRecovery)
        {
            this.activeRecallTargets.Remove(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.stuck");
            desiredTile = this.FindSafeCompanionTileNearOwner(ownerLocation, ownerTile, ownerTile, npcTile);
        }

        distance = GetFollowDistance(npc, desiredTile);
        targetChanged = !this.lastFollowTargets.TryGetValue(member.NpcName, out lastTarget) || lastTarget != desiredTile;

        desiredTileIsCurrentTile = desiredTile == npcTile;
        shouldMove = distance > StartPathingDistance || (ownerDistance > 1.5f && !desiredTileIsCurrentTile);
        int repathCooldown = isRecovery ? FollowRecoveryRepathCooldownTicks : FollowRepathCooldownTicks;
        bool pathCooldownElapsed = !this.lastFollowPathTicks.TryGetValue(member.NpcName, out int lastPathTick)
            || Game1.ticks - lastPathTick >= repathCooldown;
        bool needsRepath = shouldMove
            && (npc.controller is null || ((targetChanged || isRecovery) && pathCooldownElapsed));
        this.lastFollowTargetDistances[member.NpcName] = distance;

        if (needsRepath)
        {
            if (isRecovery && distance <= StartPathingDistance && !targetChanged)
                this.followRecoveryUntilTick.Remove(member.NpcName);

            if (!this.GetReachableTileDistances(ownerLocation, npcTile, MaxFollowReachabilitySearchTiles).ContainsKey(desiredTile))
            {
                npc.controller = null;
                this.activeRecallTargets.Remove(member.NpcName);
                this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
                return;
            }

            npc.controller = new StardewValley.Pathfinding.PathFindController(npc, ownerLocation, new Point((int)desiredTile.X, (int)desiredTile.Y), -1);
            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
        }
        else if (!shouldMove)
        {
            if (npc.controller is not null)
            {
                npc.controller = null;
                npc.Halt();
            }

            this.lastFollowTargets[member.NpcName] = desiredTile;
            this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
            this.activeRecallTargets.Remove(member.NpcName);
            this.followNoProgressTicks[member.NpcName] = 0;
            if (isRecovery)
                this.followRecoveryUntilTick.Remove(member.NpcName);
        }

        if (!shouldMove && (member.CurrentActivityKey == "companion.status.returning" || member.CurrentActivityKey == "companion.status.stuck"))
            this.SetCompanionActivity(member, "companion.status.following");
    }

    private static float GetFollowDistance(NPC npc, Vector2 desiredTile)
    {
        Vector2 npcPositionTile = npc.Position / 64f;
        return Vector2.Distance(npcPositionTile, NormalizeTile(desiredTile));
    }

    private Vector2 GetStableFollowTarget(
        SquadMemberState member,
        NPC npc,
        GameLocation location,
        Vector2 ownerTile,
        Vector2 npcTile,
        Vector2 proposedTile,
        bool forceCatchUp)
    {
        proposedTile = NormalizeTile(proposedTile);
        if (forceCatchUp || !this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 activeTarget))
            return proposedTile;

        activeTarget = NormalizeTile(activeTarget);
        if (activeTarget == proposedTile || activeTarget == npcTile)
            return proposedTile;

        if (!IsWithinCompanionDistance(ownerTile, activeTarget) || !this.IsTileSafe(location, activeTarget))
            return proposedTile;

        float activeDistance = GetFollowDistance(npc, activeTarget);
        if (activeDistance <= StartPathingDistance)
            return proposedTile;

        bool pathCooldownElapsed = !this.lastFollowPathTicks.TryGetValue(member.NpcName, out int lastPathTick)
            || Game1.ticks - lastPathTick >= FollowRepathCooldownTicks;
        bool targetMovedFarEnough = Vector2.Distance(activeTarget, proposedTile) >= FollowRetargetDistanceThreshold;
        if (pathCooldownElapsed && targetMovedFarEnough)
            return proposedTile;

        return activeTarget;
    }

    private void UpdateFollowProgressCounter(SquadMemberState member, NPC npc, bool shouldMove)
    {
        Vector2 positionTile = npc.Position / 64f;
        if (!shouldMove)
        {
            this.lastFollowProgressPositions[member.NpcName] = positionTile;
            this.followNoProgressTicks[member.NpcName] = 0;
            return;
        }

        if (this.lastFollowProgressPositions.TryGetValue(member.NpcName, out Vector2 lastPosition)
            && Vector2.Distance(positionTile, lastPosition) <= FollowPositionProgressTolerance)
        {
            this.followNoProgressTicks[member.NpcName] = this.followNoProgressTicks.TryGetValue(member.NpcName, out int stalled) ? stalled + 1 : 1;
        }
        else
        {
            this.followNoProgressTicks[member.NpcName] = 0;
        }

        this.lastFollowProgressPositions[member.NpcName] = positionTile;
    }

    private static void ResetCompanionMovementSpeed(NPC npc)
    {
        npc.speed = DefaultCompanionMovementSpeed;
        npc.addedSpeed = 0;
    }

    private void UpdateOwnerTrails()
    {
        HashSet<long> activeOwners = this.members.Values
            .Where(p => p.Mode == CompanionMode.Following)
            .Select(p => p.OwnerId)
            .ToHashSet();

        foreach (long ownerId in activeOwners)
        {
            Farmer? owner = this.GetOwnerFarmer(ownerId);
            if (owner is not null)
                this.RecordOwnerTrailPoint(owner);
        }

        foreach (long ownerId in this.ownerTrails.Keys.Where(p => !activeOwners.Contains(p)).ToList())
            this.ownerTrails.Remove(ownerId);

        foreach (long ownerId in this.ownerMovementSnapshots.Keys.Where(p => !activeOwners.Contains(p)).ToList())
            this.ownerMovementSnapshots.Remove(ownerId);
    }

    private bool IsOwnerStationary(Farmer owner)
    {
        if (owner.currentLocation is null)
            return false;

        long ownerId = owner.UniqueMultiplayerID;
        string locationName = owner.currentLocation.NameOrUniqueName;
        Vector2 position = owner.Position;

        if (!this.ownerMovementSnapshots.TryGetValue(ownerId, out OwnerMovementSnapshot snapshot))
        {
            this.ownerMovementSnapshots[ownerId] = new OwnerMovementSnapshot(locationName, position, Game1.ticks, Game1.ticks, false);
            return false;
        }

        if (snapshot.LastObservedTick == Game1.ticks)
            return snapshot.IsStationary;

        bool moved = snapshot.LocationName != locationName || Vector2.DistanceSquared(snapshot.Position, position) > 1f;
        int lastMoveTick = moved ? Game1.ticks : snapshot.LastMoveTick;
        bool isStationary = !moved && Game1.ticks - lastMoveTick >= OwnerStationaryThresholdTicks;

        this.ownerMovementSnapshots[ownerId] = new OwnerMovementSnapshot(locationName, position, lastMoveTick, Game1.ticks, isStationary);
        return isStationary;
    }

    private void RecordOwnerTrailPoint(Farmer owner)
    {
        if (owner.currentLocation is null)
            return;

        string locationName = owner.currentLocation.NameOrUniqueName;
        Vector2 tile = new((int)owner.Tile.X, (int)owner.Tile.Y);
        if (!this.ownerTrails.TryGetValue(owner.UniqueMultiplayerID, out List<FollowTrailPoint>? trail))
        {
            trail = new List<FollowTrailPoint>();
            this.ownerTrails[owner.UniqueMultiplayerID] = trail;
        }

        if (trail.Count > 0)
        {
            FollowTrailPoint last = trail[^1];
            if (last.LocationName != locationName || Vector2.Distance(last.Tile, tile) > 8f)
                trail.Clear();
            else if (last.Tile == tile)
                return;
        }

        trail.Add(new FollowTrailPoint(locationName, tile, Game1.ticks));
        if (trail.Count > MaxTrailPointsPerOwner)
            trail.RemoveRange(0, trail.Count - MaxTrailPointsPerOwner);
    }

    private Vector2 FindCompanionTile(GameLocation location, Farmer owner, int slot, bool useOwnerTrail = true, Vector2? originTile = null)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        if (this.config.CompanionFormationMode is CompanionFormationMode.Behind or CompanionFormationMode.Adaptive
            && useOwnerTrail
            && this.TryGetTrailTarget(location, owner, slot, originTile, out Vector2 trailTarget)
            && Vector2.Distance(ownerTile, trailTarget) <= FollowTrailMaxOwnerDistance)
        {
            return trailTarget;
        }

        Vector2 preferred = this.GetFormationPreferredTile(owner, slot);
        return this.FindSafeCompanionTileNearOwner(location, ownerTile, preferred, originTile);
    }

    private Vector2 GetFormationPreferredTile(Farmer owner, int slot)
    {
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Vector2 direction = owner.FacingDirection switch
        {
            0 => new Vector2(0, 1),
            1 => new Vector2(-1, 0),
            2 => new Vector2(0, -1),
            3 => new Vector2(1, 0),
            _ => new Vector2(0, 1)
        };

        Vector2 side = new(-direction.Y, direction.X);
        if (this.config.CompanionFormationMode == CompanionFormationMode.Adaptive)
        {
            // While moving, Adaptive uses the breadcrumb trail above. When the
            // owner stops (or a trail tile isn't safe), companions settle into a
            // readable crescent instead of stacking on the same follow tile.
            Vector2[] adaptiveOffsets =
            {
                direction,
                side,
                -side,
                direction + side,
                direction - side,
                -direction,
                direction * 2,
                side * 2,
                -side * 2,
                direction * 2 + side,
                direction * 2 - side,
                -direction + side
            };

            return ownerTile + adaptiveOffsets[Math.Clamp(slot, 0, adaptiveOffsets.Length - 1)];
        }

        if (this.config.CompanionFormationMode == CompanionFormationMode.Compact)
        {
            Vector2[] compactOffsets =
            {
                direction,
                side,
                -side,
                direction + side,
                direction - side,
                direction * 2,
                direction * 2 + side,
                direction * 2 - side,
                side * 2,
                -side * 2,
                direction * 3,
                direction * 2 + side * 2
            };

            return ownerTile + compactOffsets[Math.Clamp(slot, 0, compactOffsets.Length - 1)];
        }

        int row = slot / 3 + 1;
        int column = slot % 3 - 1;
        return ownerTile + direction * row + side * column;
    }

    private Vector2 FindSafeCompanionTileNearOwner(GameLocation location, Vector2 ownerTile, Vector2 preferredTile, Vector2? originTile = null)
    {
        ownerTile = NormalizeTile(ownerTile);
        preferredTile = NormalizeTile(preferredTile);
        originTile = originTile.HasValue ? NormalizeTile(originTile.Value) : null;
        Dictionary<Vector2, int>? reachableDistances = originTile.HasValue
            ? this.GetReachableTileDistances(location, originTile.Value, MaxFollowReachabilitySearchTiles)
            : null;

        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, MaxCompanionDistanceTiles)
            .Where(candidate => candidate != ownerTile)
            .Where(candidate => IsWithinCompanionDistance(ownerTile, candidate))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => reachableDistances is null || reachableDistances.ContainsKey(candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, preferredTile))
            .ThenBy(candidate => reachableDistances is not null && reachableDistances.TryGetValue(candidate, out int pathDistance) ? pathDistance : 0)
            .ThenBy(candidate => Vector2.Distance(candidate, ownerTile)))
        {
            return candidate;
        }

        return originTile ?? ownerTile;
    }

    private bool TryGetTrailTarget(GameLocation location, Farmer owner, int slot, Vector2? originTile, out Vector2 target)
    {
        target = default;
        originTile = originTile.HasValue ? NormalizeTile(originTile.Value) : null;
        Dictionary<Vector2, int>? reachableDistances = originTile.HasValue
            ? this.GetReachableTileDistances(location, originTile.Value, MaxFollowReachabilitySearchTiles)
            : null;

        if (!this.ownerTrails.TryGetValue(owner.UniqueMultiplayerID, out List<FollowTrailPoint>? trail) || trail.Count < 2)
            return false;

        string locationName = location.NameOrUniqueName;
        FollowTrailPoint latest = trail[^1];
        if (latest.LocationName != locationName || Game1.ticks - latest.Tick > StationaryTrailExpiryTicks)
            return false;

        int lag = Math.Min(trail.Count - 1, BaseTrailLag + slot * TrailLagPerFollower);
        int targetIndex = Math.Max(0, trail.Count - 1 - lag);

        for (int i = targetIndex; i >= 0; i--)
        {
            FollowTrailPoint point = trail[i];
            if (point.LocationName == locationName
                && this.IsTileSafe(location, point.Tile)
                && (reachableDistances is null || reachableDistances.ContainsKey(NormalizeTile(point.Tile))))
            {
                target = point.Tile;
                return true;
            }
        }

        return false;
    }

    private Dictionary<Vector2, int> GetReachableTileDistances(GameLocation location, Vector2 originTile, int maxVisitedTiles)
    {
        originTile = NormalizeTile(originTile);
        ReachabilityCacheKey cacheKey = new(
            location.NameOrUniqueName,
            (int)originTile.X,
            (int)originTile.Y,
            maxVisitedTiles);
        if (this.reachabilityCache.TryGetValue(cacheKey, out ReachabilityCacheEntry cached)
            && cached.Tick == Game1.ticks)
        {
            return cached.Distances;
        }

        if (this.reachabilityCache.Count > 128)
        {
            foreach (ReachabilityCacheKey staleKey in this.reachabilityCache
                .Where(p => p.Value.Tick != Game1.ticks)
                .Select(p => p.Key)
                .ToList())
            {
                this.reachabilityCache.Remove(staleKey);
            }
        }

        Dictionary<Vector2, int> distances = new()
        {
            [originTile] = 0
        };

        if (!this.IsTileInsideMap(location, originTile))
        {
            this.reachabilityCache[cacheKey] = new ReachabilityCacheEntry(Game1.ticks, distances);
            return distances;
        }

        Queue<Vector2> open = new();
        open.Enqueue(originTile);
        while (open.Count > 0 && distances.Count < maxVisitedTiles)
        {
            Vector2 current = open.Dequeue();
            int nextDistance = distances[current] + 1;
            foreach (Vector2 offset in CardinalTileOffsets)
            {
                Vector2 next = current + offset;
                if (distances.ContainsKey(next) || !this.IsTileSafe(location, next))
                    continue;

                distances[next] = nextDistance;
                open.Enqueue(next);
                if (distances.Count >= maxVisitedTiles)
                    break;
            }
        }

        this.reachabilityCache[cacheKey] = new ReachabilityCacheEntry(Game1.ticks, distances);
        return distances;
    }

    private IEnumerable<Vector2> GetNearbyTiles(Vector2 center, int radius)
    {
        yield return new Vector2((int)center.X, (int)center.Y);
        for (int distance = 1; distance <= radius; distance++)
        {
            for (int x = -distance; x <= distance; x++)
            {
                for (int y = -distance; y <= distance; y++)
                {
                    if (Math.Abs(x) != distance && Math.Abs(y) != distance)
                        continue;

                    yield return new Vector2((int)center.X + x, (int)center.Y + y);
                }
            }
        }
    }

    private bool IsTileSafe(GameLocation location, Vector2 tile)
    {
        tile = NormalizeTile(tile);
        if (!this.IsTileInsideMap(location, tile))
            return false;

        int x = (int)tile.X;
        int y = (int)tile.Y;
        return !location.isWaterTile(x, y)
            && !this.HasBlockingTileProperty(location, x, y)
            && location.isTileLocationOpen(tile)
            && !location.IsTileBlockedBy(tile);
    }

    private bool HasBlockingTileProperty(GameLocation location, int x, int y)
    {
        return this.HasTileProperty(location, x, y, "NPCBarrier")
            || this.HasTileProperty(location, x, y, "NoPath")
            || this.HasTileProperty(location, x, y, "NoPathing");
    }

    private bool HasTileProperty(GameLocation location, int x, int y, string propertyName)
    {
        foreach (string layer in TilePropertyLayers)
        {
            if (!string.IsNullOrWhiteSpace(location.doesTileHavePropertyNoNull(x, y, propertyName, layer)))
                return true;
        }

        return false;
    }

    private bool IsTileInsideMap(GameLocation location, Vector2 tile)
    {
        if (location.Map is null || location.Map.Layers.Count == 0)
            return false;

        int x = (int)tile.X;
        int y = (int)tile.Y;
        if (x < 0 || y < 0)
            return false;

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;

        return x < width && y < height;
    }

    private bool IsTileWithinOwnerRange(SquadMemberState member, GameLocation location, Vector2 tile)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        return owner is not null
            && owner.currentLocation == location
            && IsWithinCompanionDistance(owner.Tile, tile);
    }

    private static bool IsWithinCompanionDistance(Vector2 ownerTile, Vector2 candidateTile)
    {
        return Vector2.Distance(NormalizeTile(ownerTile), NormalizeTile(candidateTile)) <= MaxCompanionDistanceTiles;
    }

    private static bool IsWithinOwnerDistance(Vector2 ownerTile, Vector2 candidateTile, int maxDistance)
    {
        return Vector2.Distance(NormalizeTile(ownerTile), NormalizeTile(candidateTile)) <= maxDistance;
    }

    private static Vector2 NormalizeTile(Vector2 tile)
    {
        return new Vector2((int)tile.X, (int)tile.Y);
    }

    private void PlaceNpc(NPC npc, GameLocation location, Vector2 tile)
    {
        tile = NormalizeTile(tile);
        if (!this.IsTileSafe(location, tile) && !this.TryFindNearestSafeTile(location, tile, SafePlacementSearchRadius, out tile))
            return;

        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();

        GameLocation? oldLocation = npc.currentLocation;
        if (oldLocation != location)
        {
            oldLocation?.characters.Remove(npc);
            if (!location.characters.Contains(npc))
                location.characters.Add(npc);
            npc.currentLocation = location;
        }

        npc.Position = tile * 64f;
    }

    private bool TryFindNearestSafeTile(GameLocation location, Vector2 centerTile, int radius, out Vector2 safeTile)
    {
        centerTile = NormalizeTile(centerTile);
        foreach (Vector2 candidate in this.GetNearbyTiles(centerTile, radius)
            .Where(candidate => this.IsTileSafe(location, candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, centerTile)))
        {
            safeTile = candidate;
            return true;
        }

        safeTile = centerTile;
        return false;
    }

    private void TryManualTask(ICursorPosition cursor)
    {
        if (!this.AreTaskActionsSafe())
            return;

        SquadMemberState? member = this.GetAvailableLocalMember();
        if (member is null)
        {
            this.Warn("commands.no_followers");
            return;
        }

        if (!this.AreTasksEnabled(Game1.player.UniqueMultiplayerID))
        {
            this.Warn("tasks.disabled");
            return;
        }

        Vector2 tile = cursor.GrabTile;
        if (this.TryHarvestTile(Game1.currentLocation, tile, member, manual: true))
            return;

        if (this.TryPetAnimalAtTile(Game1.currentLocation, tile, member, manual: true))
            return;

        if (this.TryWaterTile(Game1.currentLocation, tile, member, manual: true))
            return;

        if (this.TryGatherTile(Game1.currentLocation, tile, member, manual: true))
            return;

        if (this.TryLumberTile(Game1.currentLocation, tile, member, manual: true))
            return;

        if (this.TryMiningTile(Game1.currentLocation, tile, member, manual: true))
            return;

        this.SetTaskFailure(member, "companion.task_failure.no_valid_target");
        this.Warn("tasks.no_valid_target");
    }

    private void PositionNpcForInstantTask(NPC npc, GameLocation location, Vector2 targetTile, SquadMemberState member)
    {
        if (!this.TryFindSafeAdjacentTile(location, NormalizeTile(targetTile), npc, member, MaxCompanionDistanceTiles, out Vector2 standTile))
            return;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        standTile = NormalizeTile(standTile);
        if (standTile == npcTile)
            return;

        if (!this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles).ContainsKey(standTile))
            return;

        ResetCompanionMovementSpeed(npc);
        npc.controller = new StardewValley.Pathfinding.PathFindController(npc, location, new Point((int)standTile.X, (int)standTile.Y), -1);
    }

    private void TryMimicToolUse(ICursorPosition cursor)
    {
        if (!this.AreTasksEnabled(Game1.player.UniqueMultiplayerID) || !this.AreTaskActionsSafe())
            return;

        SquadMemberState? member = this.GetAvailableLocalMember();
        if (member is null)
            return;

        if (Game1.player.CurrentTool is Axe && this.config.LumberingMode == TaskMode.Mimicking)
        {
            if (this.TryLumberTile(Game1.currentLocation, cursor.GrabTile, member, manual: false))
                return;

            this.TryLumberTile(Game1.currentLocation, this.GetFacingTile(Game1.player), member, manual: false);
            return;
        }

        if (Game1.player.CurrentTool is Pickaxe && this.config.MiningMode == TaskMode.Mimicking)
        {
            Vector2 aimedTile = NormalizeTile(cursor.GrabTile);
            if (!this.IsValidMiningTarget(Game1.currentLocation, aimedTile))
                aimedTile = NormalizeTile(this.GetFacingTile(Game1.player));

            if (!this.IsValidMiningTarget(Game1.currentLocation, aimedTile))
                return;

            foreach (Vector2 nearbyTile in Game1.currentLocation.Objects.Keys
                .Where(candidate => NormalizeTile(candidate) != aimedTile)
                .Where(candidate => IsWithinCompanionDistance(Game1.player.Tile, candidate))
                .Where(candidate => this.IsValidMiningTarget(Game1.currentLocation, candidate))
                .OrderBy(candidate => Vector2.Distance(aimedTile, NormalizeTile(candidate))))
            {
                if (this.TryMiningTile(Game1.currentLocation, nearbyTile, member, manual: false))
                    return;
            }

            return;
        }

        if (Game1.player.CurrentTool is WateringCan && this.config.WateringMode == TaskMode.Mimicking)
        {
            Vector2 aimedTile = NormalizeTile(cursor.GrabTile);
            HoeDirt? aimedDirt = Game1.currentLocation.GetHoeDirtAtTile(aimedTile);
            if (aimedDirt?.needsWatering() != true)
                return;

            foreach (Vector2 nearbyTile in this.GetNearbyTiles(Game1.player.Tile, MaxCompanionDistanceTiles)
                .Where(candidate => NormalizeTile(candidate) != aimedTile))
            {
                if (this.TryWaterTile(Game1.currentLocation, nearbyTile, member, manual: false))
                    return;
            }
        }
    }

    private void UpdateAutonomousTasks()
    {
        if (!this.AreTaskActionsSafe() || !this.AreTasksEnabled(Game1.player.UniqueMultiplayerID))
            return;

        foreach (SquadMemberState member in this.members.Values.Where(p => p.OwnerId == Game1.player.UniqueMultiplayerID && p.Mode == CompanionMode.Following && !this.pendingTasks.ContainsKey(p.NpcName)))
        {
            if (this.HasActiveWorkDirective(member) && this.TryAssignWorkDirectiveTask(member))
                continue;

            if (!this.HasActiveWorkDirective(member) && this.TryAssignConfiguredAutonomousTask(member))
                continue;

            if (this.config.PettingMode == TaskMode.Autonomous
                && this.TryPetNearestAnimal(Game1.currentLocation, member))
            {
                return;
            }

            if (this.config.HarvestingMode == TaskMode.Autonomous)
            {
                foreach (Vector2 tile in this.GetNearbyTiles(Game1.player.Tile, radius: MaxCompanionDistanceTiles))
                {
                    if (this.TryHarvestTile(Game1.currentLocation, tile, member, manual: false))
                        return;
                }
            }

            if (this.config.EnableGathering && this.config.ForagingMode == TaskMode.Autonomous)
            {
                foreach (Vector2 tile in this.GetNearbyTiles(Game1.player.Tile, radius: MaxCompanionDistanceTiles))
                {
                    if (this.TryGatherTile(Game1.currentLocation, tile, member, manual: false))
                        return;
                }
            }

            if (this.config.WateringMode == TaskMode.Autonomous)
            {
                foreach (Vector2 tile in this.GetNearbyTiles(Game1.player.Tile, radius: MaxCompanionDistanceTiles))
                {
                    if (this.TryWaterTile(Game1.currentLocation, tile, member, manual: false))
                        return;
                }
            }
        }
    }

    private bool TryWaterTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe() || location is null)
            return false;

        if (this.config.WateringMode == TaskMode.Disabled)
            return false;

        if (manual && this.config.WateringMode == TaskMode.Autonomous)
            return false;

        if (!this.IsTileWithinOwnerRange(member, location, tile))
            return false;

        HoeDirt? dirt = location.GetHoeDirtAtTile(tile);
        if (dirt is null || !dirt.needsWatering())
            return false;

        dirt.state.Value = HoeDirt.watered;
        this.SetTaskResult(member, "companion.task_result.watered");
        NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is not null)
        {
            this.PositionNpcForInstantTask(npc, location, tile, member);
            this.FaceTile(npc, tile);
            this.ShowCompanionWorkSignal(npc, location, tile, "water");
            this.Say(npc, "Watering", force: false);
        }

        this.Info("tasks.watered", new { npc = member.DisplayName });
        return true;
    }

    private bool TryGatherTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe() || location is null)
            return false;

        if (!this.config.EnableGathering || this.config.ForagingMode == TaskMode.Disabled)
            return false;

        if (!this.IsTileWithinOwnerRange(member, location, tile))
            return false;

        if (!location.Objects.TryGetValue(tile, out SObject? obj) || !obj.IsSpawnedObject)
            return false;

        Item item = obj.getOne();
        item.Stack = Math.Max(1, obj.Stack);
        this.RouteItemToInventoryOrDrop(item, location, tile);

        this.RecordCompanionLoot(member, item, "companion.loot_source.forage");
        location.Objects.Remove(tile);
        location.OnHarvestedForage(Game1.player, obj);
        this.SetTaskResult(member, "companion.task_result.gathered");

        NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is not null)
        {
            this.PositionNpcForInstantTask(npc, location, tile, member);
            this.FaceTile(npc, tile);
            this.ShowCompanionWorkSignal(npc, location, tile, "forage");
            this.Say(npc, "Foraging", force: false);
        }

        this.Info("tasks.gathered", new { npc = member.DisplayName, item = item.DisplayName });
        return true;
    }

    private bool TryLumberTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe() || location is null)
            return false;

        if (this.config.LumberingMode == TaskMode.Disabled)
            return false;

        Vector2 targetTile = new((int)tile.X, (int)tile.Y);
        if (!location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not Tree tree
            || !this.IsValidWoodTarget(location, targetTile))
            return false;

        if (Game1.player.CurrentTool is not Axe axe)
        {
            this.SetTaskFailure(member, "companion.task_failure.need_axe");
            if (manual)
                this.Warn("tasks.need_axe");

            return false;
        }

        NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.SetTaskFailure(member, "companion.task_failure.no_valid_target");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.currentLocation != location)
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_unavailable");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!IsWithinCompanionDistance(owner.Tile, npc.Tile))
        {
            this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, MaxCompanionDistanceTiles, out Vector2 standTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        if (!this.TryReserveWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_reserved");
            if (manual)
                this.Warn("tasks.no_valid_target");

            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.Lumbering,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            Manual = manual,
            RequiresPlayerTool = true,
            WorkRadius = MaxCompanionDistanceTiles,
            ReturnDistance = MaxCompanionDistanceTiles,
            StartedTick = Game1.ticks
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.working");
        this.SetCompanionTarget(member, CompanionTaskKind.Lumbering, targetTile);
        this.ShowCompanionWorkSignal(npc, location, targetTile, "target");
        this.Say(npc, "Lumbering", force: false);

        if (this.IsNpcAtTaskTile(npc, standTile))
        {
            bool finished = this.PerformLumberHit(location, tree, targetTile, member, npc, axe, manual);
            task.LastActionTick = Game1.ticks;
            if (finished)
                this.RemovePendingTask(task);

            return true;
        }

        this.RouteNpcToTaskTile(npc, location, standTile, task, force: true);
        return true;
    }

    private bool TryMiningTile(GameLocation location, Vector2 tile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe() || this.config.MiningMode == TaskMode.Disabled)
            return false;

        if (Game1.player.CurrentTool is not Pickaxe)
        {
            this.SetTaskFailure(member, "companion.task_failure.need_pickaxe");
            if (manual)
                this.Warn("tasks.need_pickaxe");

            return false;
        }

        Vector2 targetTile = NormalizeTile(tile);
        if (!location.Objects.TryGetValue(targetTile, out SObject? obj) || !this.IsSafeMineableObject(obj))
            return false;

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null || npc.currentLocation != location || owner.currentLocation != location)
            return false;

        if (!IsWithinCompanionDistance(owner.Tile, npc.Tile))
        {
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            return false;
        }

        bool queued = this.TryQueueDirectiveMiningTask(location, targetTile, member, npc, MaxCompanionDistanceTiles);
        if (!queued || !this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task))
            return false;

        task.Manual = manual;
        task.UsesWorkDirective = false;
        task.RequiresPlayerTool = true;
        task.WorkRadius = MaxCompanionDistanceTiles;
        task.ReturnDistance = MaxCompanionDistanceTiles;
        return true;
    }

    private void ProcessPendingTasks()
    {
        if (this.IsBlockedGameState(blockForMenu: true))
            return;

        foreach (PendingCompanionTask task in this.pendingTasks.Values.ToList())
        {
            if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member)
                || !this.AreTasksEnabled(member.OwnerId))
            {
                this.RemovePendingTask(task, "companion.task_failure.tasks_disabled", returning: true);
                continue;
            }

            bool taskModeDisabled = task.Kind switch
            {
                CompanionTaskKind.Lumbering => this.config.LumberingMode == TaskMode.Disabled,
                CompanionTaskKind.Mining => this.config.MiningMode == TaskMode.Disabled,
                _ => false
            };
            if (taskModeDisabled)
            {
                this.RemovePendingTask(task, "companion.task_failure.tasks_disabled", returning: true);
                continue;
            }

            switch (task.Kind)
            {
                case CompanionTaskKind.Lumbering:
                    this.ProcessPendingLumberTask(task);
                    break;

                case CompanionTaskKind.Mining:
                    this.ProcessPendingMiningTask(task);
                    break;
            }
        }
    }

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

    private void CancelPendingTasksForOwner(long ownerId, string failureKey)
    {
        foreach (PendingCompanionTask task in this.pendingTasks.Values
            .Where(task => this.members.TryGetValue(task.NpcName, out SquadMemberState? member) && member.OwnerId == ownerId)
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
            this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            this.SetTaskFailure(member, "companion.task_failure.owner_too_far");
            return false;
        }

        int radius = this.GetCompanionWorkRadius(member);
        WorkTarget? target = this.FindBestWorkTarget(member, npc, owner, location, radius);
        this.UpdateTargetPreview(member, this.BuildTargetPreview(member, null));
        if (target is null)
        {
            this.SetCompanionActivity(member, "companion.status.following");
            this.ClearCompanionTarget(member);
            return false;
        }

        return target.Value.Kind switch
        {
            CompanionTaskKind.Lumbering => this.TryQueueDirectiveLumberTask(location, target.Value.Tile, member, npc, radius),
            CompanionTaskKind.Mining => this.TryQueueDirectiveMiningTask(location, target.Value.Tile, member, npc, radius),
            _ => false
        };
    }

    private bool TryAssignConfiguredAutonomousTask(SquadMemberState member)
    {
        bool includeWood = this.config.LumberingMode == TaskMode.Autonomous;
        bool includeMining = this.config.MiningMode == TaskMode.Autonomous;
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
            CompanionTaskKind.Lumbering => this.TryQueueDirectiveLumberTask(owner.currentLocation, target.Value.Tile, member, npc, radius),
            CompanionTaskKind.Mining => this.TryQueueDirectiveMiningTask(owner.currentLocation, target.Value.Tile, member, npc, radius),
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
        bool includeWood = (member.SearchWood || member.ClearArea) && this.config.LumberingMode != TaskMode.Disabled;
        bool includeMining = (member.SearchMining || member.ClearArea) && this.config.MiningMode != TaskMode.Disabled;
        return this.FindBestWorkTarget(member, npc, owner, location, radius, includeWood, includeMining);
    }

    private WorkTarget? FindBestWorkTarget(SquadMemberState member, NPC npc, Farmer owner, GameLocation location, int radius, bool includeWood, bool includeMining)
    {
        if (!includeWood && !includeMining)
            return null;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        List<WorkTarget> targets = new();

        void TryAddTarget(CompanionTaskKind kind, Vector2 rawTile)
        {
            Vector2 tile = NormalizeTile(rawTile);
            float playerDistance = Vector2.Distance(ownerTile, tile);
            if (playerDistance > radius)
                return;

            if (this.IsTargetReserved(location, tile))
                return;

            float npcDistance = Vector2.Distance(npcTile, tile);
            if (this.TryFindSafeAdjacentTile(location, tile, npc, member, radius, out _))
                targets.Add(new WorkTarget(kind, tile, npcDistance, playerDistance));
        }

        // Enumerate actual world features instead of walking every tile in the
        // search square. Large work radii now scale with candidate count, not
        // map area, and reachability is reused by the tick-local cache.
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

        if (targets.Count == 0)
            return null;

        return targets
            .OrderBy(p => p.NpcDistance)
            .ThenBy(p => p.PlayerDistance)
            .FirstOrDefault();
    }

    private TargetPreview BuildTargetPreview(SquadMemberState member, CompanionDirective? simulatedDirective)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        NPC? npc = this.GetNpcByName(member.NpcName);
        string locationName = owner?.currentLocation?.NameOrUniqueName ?? "";
        Vector2 ownerTile = owner is null ? new Vector2(-1f, -1f) : NormalizeTile(owner.Tile);
        Vector2 npcTile = npc is null ? new Vector2(-1f, -1f) : NormalizeTile(npc.Tile);
        bool tasksEnabled = this.AreTasksEnabled(member.OwnerId);
        bool blocked = this.IsBlockedGameState(blockForMenu: false);
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

        if (this.IsBlockedGameState(blockForMenu: false))
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

        bool includeWood = (searchWood || clearArea) && this.config.LumberingMode != TaskMode.Disabled;
        bool includeMining = (searchMining || clearArea) && this.config.MiningMode != TaskMode.Disabled;
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

        string targetKey = target.Value.Kind switch
        {
            CompanionTaskKind.Lumbering => "companion.target.wood",
            CompanionTaskKind.Mining => "companion.target.mining",
            _ => ""
        };
        return new TargetPreview(true, targetKey, (int)target.Value.Tile.X, (int)target.Value.Tile.Y, "");
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
        TargetPreview preview = this.BuildTargetPreview(member, directive);
        if (preview.HasTarget)
        {
            return this.Tr("companion.preview.target", new
            {
                target = this.Tr(preview.TargetKey),
                x = preview.X,
                y = preview.Y
            });
        }

        return this.Tr("companion.preview.reason", new { reason = this.Tr(preview.ReasonKey) });
    }

    private bool IsTargetReserved(GameLocation location, Vector2 tile)
    {
        string locationName = location.NameOrUniqueName;
        return this.workTargetReservations.ContainsKey(this.GetWorkTargetKey(locationName, tile));
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

    private bool TryQueueDirectiveLumberTask(GameLocation location, Vector2 targetTile, SquadMemberState member, NPC npc, int radius)
    {
        if (this.config.LumberingMode == TaskMode.Disabled)
            return false;

        if (!location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not Tree
            || !this.IsValidWoodTarget(location, targetTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_lost");
            return false;
        }

        if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, radius, out Vector2 standTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            return false;
        }

        if (!this.TryReserveWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_reserved");
            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.Lumbering,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            UsesWorkDirective = true,
            RequiresPlayerTool = false,
            WorkRadius = radius,
            ReturnDistance = this.GetCompanionReturnDistance(member),
            StartedTick = Game1.ticks
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.working");
        this.SetCompanionTarget(member, CompanionTaskKind.Lumbering, targetTile);
        this.ShowCompanionWorkSignal(npc, location, targetTile, "target");
        this.Say(npc, "Lumbering", force: false);

        if (!this.IsNpcAtTaskTile(npc, standTile))
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: true);

        return true;
    }

    private bool TryQueueDirectiveMiningTask(GameLocation location, Vector2 targetTile, SquadMemberState member, NPC npc, int radius)
    {
        if (this.config.MiningMode == TaskMode.Disabled)
            return false;

        if (!location.Objects.TryGetValue(targetTile, out SObject? obj) || !this.IsSafeMineableObject(obj))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_lost");
            return false;
        }

        if (!this.TryFindSafeAdjacentTile(location, targetTile, npc, member, radius, out Vector2 standTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.no_safe_tile");
            return false;
        }

        if (!this.TryReserveWorkTarget(member.NpcName, location.NameOrUniqueName, targetTile))
        {
            this.SetTaskFailure(member, "companion.task_failure.target_reserved");
            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.Mining,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = targetTile,
            UsesWorkDirective = true,
            RequiresPlayerTool = false,
            WorkRadius = radius,
            ReturnDistance = this.GetCompanionReturnDistance(member),
            StartedTick = Game1.ticks
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.working");
        this.SetCompanionTarget(member, CompanionTaskKind.Mining, targetTile);
        this.ShowCompanionWorkSignal(npc, location, targetTile, "target");
        this.Say(npc, "Mining", force: false);

        if (!this.IsNpcAtTaskTile(npc, standTile))
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: true);

        return true;
    }

    private void ProcessPendingLumberTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member) || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (Game1.ticks - task.StartedTick > LumberTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout");
            if (task.Manual)
                this.Warn("tasks.no_valid_target");

            return;
        }

        GameLocation location = Game1.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        NPC? npc = Game1.getCharacterFromName(task.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        if (!location.terrainFeatures.TryGetValue(task.TargetTile, out TerrainFeature? feature) || feature is not Tree tree)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        if (tree.tapped.Value)
        {
            this.RemovePendingTask(task, "companion.task_failure.protected_target", returning: true);
            return;
        }

        // Let the vanilla falling animation finish before chopping the stump.
        // Otherwise the tree can reach its terminal health while still falling
        // and the task would wait forever for a completion signal.
        if (tree.falling.Value)
            return;

        Axe? axe = Game1.player.CurrentTool as Axe;
        if (task.RequiresPlayerTool && axe is null)
        {
            if (task.Manual)
                this.Warn("tasks.need_axe");

            this.RemovePendingTask(task, "companion.task_failure.need_axe");
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        int returnDistance = Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance);
        if (ownerDistance > returnDistance)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            if (!task.UsesWorkDirective)
                this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            return;
        }

        ResetCompanionMovementSpeed(npc);

        int workRadius = Math.Max(MaxCompanionDistanceTiles, task.WorkRadius);
        if (!this.TryFindSafeAdjacentTile(location, task.TargetTile, npc, member, workRadius, out Vector2 standTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.no_safe_tile", returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        if (member.CurrentActivityKey == "companion.status.stuck")
            this.SetCompanionActivity(member, "companion.status.working");

        if (Game1.ticks - task.LastActionTick < this.GetLumberHitCooldown(member))
            return;

        bool finished = this.PerformLumberHit(location, tree, task.TargetTile, member, npc, axe, task.Manual);
        task.LastActionTick = Game1.ticks;
        if (finished)
            this.RemovePendingTask(task);
    }

    private void ProcessPendingMiningTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member) || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (Game1.ticks - task.StartedTick > MiningTaskTimeoutTicks)
        {
            this.RemovePendingTask(task, "companion.task_failure.task_timeout");
            return;
        }

        GameLocation location = Game1.currentLocation;
        if (location.NameOrUniqueName != task.LocationName)
        {
            this.RemovePendingTask(task, "companion.task_failure.location_changed", returning: true);
            return;
        }

        NPC? npc = Game1.getCharacterFromName(task.NpcName, mustBeVillager: false, includeEventActors: false);
        if (npc is null || npc.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        if (!location.Objects.TryGetValue(task.TargetTile, out SObject? obj) || !this.IsSafeMineableObject(obj))
        {
            this.RemovePendingTask(task, "companion.task_failure.target_lost");
            return;
        }

        Pickaxe? pickaxe = Game1.player.CurrentTool as Pickaxe;
        if (task.RequiresPlayerTool && pickaxe is null)
        {
            this.RemovePendingTask(task, "companion.task_failure.need_pickaxe");
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.currentLocation != location)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_unavailable", returning: true);
            return;
        }

        float ownerDistance = Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(npc.Tile));
        int returnDistance = Math.Max(MaxCompanionDistanceTiles, task.ReturnDistance);
        if (ownerDistance > returnDistance)
        {
            this.RemovePendingTask(task, "companion.task_failure.owner_too_far", returning: true);
            if (!task.UsesWorkDirective)
                this.UpdateFollower(member, npc, owner, forceCatchUp: true);
            return;
        }

        ResetCompanionMovementSpeed(npc);

        int workRadius = Math.Max(MaxCompanionDistanceTiles, task.WorkRadius);
        if (!this.TryFindSafeAdjacentTile(location, task.TargetTile, npc, member, workRadius, out Vector2 standTile))
        {
            this.RemovePendingTask(task, "companion.task_failure.no_safe_tile", returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(npc, location, standTile, task, force: false);
            return;
        }

        if (member.CurrentActivityKey == "companion.status.stuck")
            this.SetCompanionActivity(member, "companion.status.working");

        if (Game1.ticks - task.LastActionTick < this.GetMiningHitCooldown(member))
            return;

        bool finished = this.PerformMiningHit(location, obj, task.TargetTile, member, npc, task, pickaxe);
        task.LastActionTick = Game1.ticks;
        if (finished)
            this.RemovePendingTask(task);
    }

    private bool TryFindSafeAdjacentTile(GameLocation location, Vector2 targetTile, NPC npc, SquadMemberState member, int maxOwnerDistance, out Vector2 standTile)
    {
        Vector2[] offsets =
        {
            new(0, 1),
            new(1, 0),
            new(-1, 0),
            new(0, -1)
        };

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        Vector2 npcTile = NormalizeTile(npc.Tile);
        if (owner is null || owner.currentLocation != location)
        {
            standTile = npcTile;
            return false;
        }

        Vector2 ownerTile = NormalizeTile(owner.Tile);
        Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles);
        if (Vector2.Distance(npcTile, targetTile) <= TaskArrivalDistance
            && IsWithinOwnerDistance(ownerTile, npcTile, maxOwnerDistance))
        {
            standTile = npcTile;
            return true;
        }

        foreach (Vector2 candidate in offsets
            .Select(offset => NormalizeTile(targetTile + offset))
            .Where(candidate => IsWithinOwnerDistance(ownerTile, candidate, maxOwnerDistance))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => reachableDistances.ContainsKey(candidate))
            .OrderBy(candidate => reachableDistances[candidate])
            .ThenBy(candidate => Vector2.Distance(candidate, npcTile)))
        {
            standTile = candidate;
            return true;
        }

        standTile = npcTile;
        return false;
    }

    private bool IsNpcAtTaskTile(NPC npc, Vector2 standTile)
    {
        return Vector2.Distance(npc.Tile, standTile) <= TaskArrivalDistance;
    }

    private void RouteNpcToTaskTile(NPC npc, GameLocation location, Vector2 standTile, PendingCompanionTask task, bool force)
    {
        int retryTicks = 30;
        this.members.TryGetValue(task.NpcName, out SquadMemberState? member);
        if (member is not null && this.HasSkill(member, "SKILL-UTILITY-001"))
            retryTicks = 24;

        Vector2 npcTile = NormalizeTile(npc.Tile);
        standTile = NormalizeTile(standTile);
        float distance = Vector2.Distance(npcTile, standTile);
        if (!force)
        {
            if (task.LastDistanceToStandTile >= 0f && distance >= task.LastDistanceToStandTile - FollowProgressTolerance)
                task.NoProgressTicks++;
            else
                task.NoProgressTicks = 0;

            if (task.NoProgressTicks >= TaskNoProgressUpdatesThreshold && member is not null)
            {
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
                task.NoProgressTicks = 0;
                task.LastPathTick = 0;
                force = true;
            }
        }
        else
        {
            task.NoProgressTicks = 0;
        }

        task.LastDistanceToStandTile = distance;

        if (!force && Game1.ticks - task.LastPathTick < retryTicks)
            return;

        if (!this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles).ContainsKey(standTile))
        {
            if (member is not null)
            {
                this.SetCompanionActivity(member, "companion.status.stuck");
                this.SetTaskFailure(member, "companion.task_failure.path_recovery");
                this.ShowMovementDebugNotice(member, "companion.movement_debug.path_recovery", new { npc = member.DisplayName });
            }

            return;
        }

        task.LastPathTick = Game1.ticks;
        if (member is not null && member.CurrentActivityKey == "companion.status.stuck")
            this.SetCompanionActivity(member, "companion.status.working");

        ResetCompanionMovementSpeed(npc);
        npc.controller = new StardewValley.Pathfinding.PathFindController(npc, location, new Point((int)standTile.X, (int)standTile.Y), -1);
    }

    private bool PerformLumberHit(GameLocation location, Tree tree, Vector2 tile, SquadMemberState member, NPC npc, Axe? axe, bool manual)
    {
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
        this.FaceTile(npc, tile);
        npc.Sprite.Animate(Game1.currentGameTime, npc.Sprite.currentFrame, 2, 80f);
        npc.jumpWithoutSound(4f);
        npc.shake(150);
        tree.shake(tile, doEvenIfStillShaking: true);
        this.ShowCompanionWorkSignal(npc, location, tile, "wood");
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null)
            return false;

        Axe effectiveAxe = axe ?? new Axe();
        if (this.HasSkill(member, "SKILL-LUMBER-001"))
        {
            if (axe?.getOne() is Axe copiedAxe)
                effectiveAxe = copiedAxe;

            effectiveAxe.UpgradeLevel = Math.Min(Tool.iridium, effectiveAxe.UpgradeLevel + 1);
        }

        effectiveAxe.lastUser = owner;
        bool removeFeature = tree.performToolAction(effectiveAxe, 0, tile);
        this.AddCompanionXp(member, 1);

        if (removeFeature)
        {
            location.terrainFeatures.Remove(tile);
            if (this.HasSkill(member, "SKILL-LUMBER-003") && Game1.random.NextDouble() < 0.35)
            {
                this.RouteTaskRewardOrDrop(
                    member,
                    ItemRegistry.Create("(O)388"),
                    location,
                    tile,
                    "companion.loot_source.wood");
            }

            this.AddCompanionXp(member, 8);
            this.SetTaskResult(member, "companion.task_result.lumbered");

            if (manual)
                this.Info("tasks.lumbered", new { npc = member.DisplayName });

            return true;
        }

        return false;
    }

    private bool PerformMiningHit(GameLocation location, SObject obj, Vector2 tile, SquadMemberState member, NPC npc, PendingCompanionTask task, Pickaxe? pickaxe)
    {
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
        this.FaceTile(npc, tile);
        npc.Sprite.Animate(Game1.currentGameTime, npc.Sprite.currentFrame, 2, 80f);
        npc.jumpWithoutSound(4f);
        npc.shake(150);
        this.ShowCompanionWorkSignal(npc, location, tile, "mining");
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null)
            return false;

        Pickaxe effectivePickaxe = pickaxe ?? new Pickaxe();
        if (this.HasSkill(member, "SKILL-MINING-001"))
        {
            if (pickaxe?.getOne() is Pickaxe copiedPickaxe)
                effectivePickaxe = copiedPickaxe;

            effectivePickaxe.UpgradeLevel = Math.Min(Tool.iridium, effectivePickaxe.UpgradeLevel + 1);
        }

        effectivePickaxe.lastUser = owner;
        bool destroyed = obj.performToolAction(effectivePickaxe);
        this.AddCompanionXp(member, 1);
        if (!destroyed)
            return false;

        location.Objects.Remove(tile);
        location.OnStoneDestroyed(obj.ItemId, (int)tile.X, (int)tile.Y, owner);

        if (this.HasSkill(member, "SKILL-MINING-003") && Game1.random.NextDouble() < 0.25)
        {
            string? bonusOreId = this.GetOreRewardId(this.GetObjectSearchText(obj));
            if (bonusOreId is not null)
            {
                this.RouteTaskRewardOrDrop(
                    member,
                    ItemRegistry.Create(bonusOreId),
                    location,
                    tile,
                    "companion.loot_source.mining");
            }
        }

        this.AddCompanionXp(member, 6);
        this.SetTaskResult(member, "companion.task_result.mined");
        return true;
    }

    private string GetObjectSearchText(SObject obj)
    {
        return string.Join(
            " ",
            obj.Name,
            obj.DisplayName,
            obj.ItemId,
            obj.QualifiedItemId,
            obj.ParentSheetIndex.ToString());
    }

    private string? GetOreRewardId(string text)
    {
        if (text.Contains("radioactive", StringComparison.OrdinalIgnoreCase) || text.Contains("909", StringComparison.OrdinalIgnoreCase))
            return "(O)909";

        if (text.Contains("iridium", StringComparison.OrdinalIgnoreCase) || text.Contains("386", StringComparison.OrdinalIgnoreCase))
            return "(O)386";

        if (text.Contains("gold", StringComparison.OrdinalIgnoreCase) || text.Contains("384", StringComparison.OrdinalIgnoreCase))
            return "(O)384";

        if (text.Contains("iron", StringComparison.OrdinalIgnoreCase) || text.Contains("380", StringComparison.OrdinalIgnoreCase))
            return "(O)380";

        if (text.Contains("copper", StringComparison.OrdinalIgnoreCase) || text.Contains("378", StringComparison.OrdinalIgnoreCase))
            return "(O)378";

        return null;
    }

    private int GetLumberHitCooldown(SquadMemberState member)
    {
        int cooldown = LumberHitCooldownTicks;
        if (this.HasSkill(member, "SKILL-LUMBER-002"))
            cooldown = (int)MathF.Round(cooldown * 0.9f);

        return Math.Max(20, cooldown);
    }

    private int GetMiningHitCooldown(SquadMemberState member)
    {
        int cooldown = MiningHitCooldownTicks;
        if (this.HasSkill(member, "SKILL-MINING-002"))
            cooldown = (int)MathF.Round(cooldown * 0.9f);

        return Math.Max(20, cooldown);
    }

    private int GetCompanionWorkRadius(SquadMemberState member)
    {
        int radius = this.config.CompanionWorkRadius;
        if (this.HasSkill(member, "SKILL-UTILITY-002"))
            radius++;

        return Math.Max(MaxCompanionDistanceTiles, radius);
    }

    private int GetCompanionReturnDistance(SquadMemberState member)
    {
        int distance = this.config.CompanionWorkReturnDistance;
        if (this.HasSkill(member, "SKILL-UTILITY-003"))
            distance = Math.Max(this.config.CompanionWorkRadius, distance - 1);

        return Math.Max(MaxCompanionDistanceTiles, distance);
    }

    private void FaceTile(NPC npc, Vector2 tile)
    {
        Vector2 delta = tile - npc.Tile;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
            npc.faceDirection(delta.X > 0 ? 1 : 3);
        else
            npc.faceDirection(delta.Y > 0 ? 2 : 0);
    }

    private void ToggleTasks(Farmer player)
    {
        bool enabled = !this.AreTasksEnabled(player.UniqueMultiplayerID);
        this.taskToggles[player.UniqueMultiplayerID] = enabled;
        if (!enabled)
            this.CancelPendingTasksForOwner(player.UniqueMultiplayerID, "companion.task_failure.tasks_disabled");

        this.InvalidateTargetPreviews();
        this.Info(enabled ? "tasks.enabled" : "tasks.disabled");
    }

    private bool AreTasksEnabled(long playerId)
    {
        return !this.taskToggles.TryGetValue(playerId, out bool enabled) || enabled;
    }

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
            && this.companionQuickHud is not null
            && this.companionQuickHud.TryHandleClick(screenPixels);
    }

    private bool IsCompanionQuickWorkActive(SquadMemberState member)
    {
        return this.HasActiveWorkDirective(member)
            || (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task) && !task.UsesConfiguredAutonomy);
    }

    private void ToggleCompanionQuickWork(SquadMemberState member)
    {
        if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            return;

        if (this.IsCompanionQuickWorkActive(member))
        {
            member.SearchWood = false;
            member.SearchMining = false;
            member.ClearArea = false;
            this.RemovePendingTask(member.NpcName);
            this.SetCompanionActivity(member, "companion.status.following");
            this.ClearCompanionTarget(member);
            this.InvalidateTargetPreviews();
            this.Info("companion.quick.work_disabled", new { npc = member.DisplayName });
            return;
        }

        if (!this.AreTasksEnabled(Game1.player.UniqueMultiplayerID))
            this.taskToggles[Game1.player.UniqueMultiplayerID] = true;

        if (member.Mode != CompanionMode.Following)
            this.ResumeFollowing(member.NpcName, Game1.player.UniqueMultiplayerID);

        if (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? autonomousTask)
            && autonomousTask.UsesConfiguredAutonomy)
        {
            this.RemovePendingTask(autonomousTask, returning: false);
        }

        this.ApplyPreferredWorkSpecialty(member);
        this.Info("companion.quick.work_enabled_specialty", new
        {
            npc = member.DisplayName,
            specialty = this.Tr($"companion.specialty.{member.PreferredWorkSpecialty}")
        });
    }

    private void FollowCompanionFromQuickHud(SquadMemberState member)
    {
        this.RecallCompanion(member.NpcName, Game1.player.UniqueMultiplayerID, showMessage: true);
    }

    private void RecallAllLocalCompanions()
    {
        List<SquadMemberState> localMembers = this.GetLocalMembers().ToList();
        if (localMembers.Count == 0)
        {
            this.Warn("commands.no_followers");
            return;
        }

        int recalled = 0;
        foreach (SquadMemberState member in localMembers)
        {
            if (this.RecallCompanion(member.NpcName, Game1.player.UniqueMultiplayerID, showMessage: false))
                recalled++;
        }

        if (recalled > 0)
            this.Info("companion.quick.recall_all", new { count = recalled });
    }

    private bool RecallCompanion(string npcName, long ownerId, bool showMessage)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member) || !this.CanOwnerMutate(member, ownerId))
            return false;

        if (!Context.IsMainPlayer && ownerId == Game1.player.UniqueMultiplayerID)
        {
            this.SendActionRequest("Recall", npcName);
            if (showMessage)
                this.Info("companion.quick.recall_sent", new { npc = member.DisplayName });

            return true;
        }

        bool hadPendingTask = this.pendingTasks.ContainsKey(member.NpcName);
        bool hadDirective = this.HasActiveWorkDirective(member);
        bool wasWaiting = member.Mode != CompanionMode.Following;
        bool wasStuck = member.CurrentActivityKey == "companion.status.stuck";
        bool wasReturning = member.CurrentActivityKey == "companion.status.returning";
        bool wasAway = this.IsCompanionAwayFromOwner(member);
        bool shouldReturn = hadPendingTask || hadDirective || wasWaiting || wasStuck || wasReturning || wasAway;

        member.SearchWood = false;
        member.SearchMining = false;
        member.ClearArea = false;
        this.RemovePendingTask(member.NpcName);
        this.ReleaseWorkTargetsForNpc(member.NpcName);
        this.ClearCompanionTarget(member);

        member.Mode = CompanionMode.Following;
        member.WaitingLocationName = null;
        member.ParkedAtUtcTicks = 0;
        this.SetCompanionActivity(member, shouldReturn ? "companion.status.returning" : "companion.status.following");

        if (hadPendingTask || hadDirective)
            this.SetTaskFailure(member, "companion.task_failure.recalled");

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        bool startedRecallPath = false;
        if (npc is not null && owner is not null)
        {
            npc.controller = null;
            startedRecallPath = this.TryStartRecallPath(member, npc, owner);
            if (!startedRecallPath)
                this.UpdateFollower(member, npc, owner, forceCatchUp: true);
        }

        shouldReturn = shouldReturn || startedRecallPath;

        if (showMessage && shouldReturn)
            this.Info("companion.quick.returning", new { npc = member.DisplayName });

        return shouldReturn;
    }

    private bool TryStartRecallPath(SquadMemberState member, NPC npc, Farmer owner)
    {
        if (npc.currentLocation != owner.currentLocation)
            return false;

        GameLocation location = owner.currentLocation;
        Vector2 npcTile = NormalizeTile(npc.Tile);
        Vector2 ownerTile = NormalizeTile(owner.Tile);
        if (!this.TryFindRecallTargetTile(location, ownerTile, npcTile, out Vector2 targetTile))
            return false;

        targetTile = NormalizeTile(targetTile);
        if (targetTile == npcTile)
            return false;

        this.ClearFollowState(member.NpcName);
        ResetCompanionMovementSpeed(npc);
        npc.controller = new StardewValley.Pathfinding.PathFindController(npc, location, new Point((int)targetTile.X, (int)targetTile.Y), -1);
        this.activeRecallTargets[member.NpcName] = targetTile;
        this.lastFollowTargets[member.NpcName] = targetTile;
        this.lastFollowTargetDistances[member.NpcName] = GetFollowDistance(npc, targetTile);
        this.lastFollowPathTicks[member.NpcName] = Game1.ticks;
        this.lastFollowProgressPositions[member.NpcName] = npc.Position / 64f;
        this.SetCompanionActivity(member, "companion.status.returning");
        return true;
    }

    private bool TryFindRecallTargetTile(GameLocation location, Vector2 ownerTile, Vector2 npcTile, out Vector2 targetTile)
    {
        ownerTile = NormalizeTile(ownerTile);
        npcTile = NormalizeTile(npcTile);
        Dictionary<Vector2, int> reachableDistances = this.GetReachableTileDistances(location, npcTile, MaxFollowReachabilitySearchTiles);

        foreach (Vector2 candidate in this.GetNearbyTiles(ownerTile, MaxCompanionDistanceTiles)
            .Where(candidate => candidate != ownerTile && candidate != npcTile)
            .Where(candidate => IsWithinCompanionDistance(ownerTile, candidate))
            .Where(candidate => this.IsTileSafe(location, candidate))
            .Where(candidate => reachableDistances.ContainsKey(candidate))
            .OrderBy(candidate => Vector2.Distance(candidate, ownerTile))
            .ThenBy(candidate => reachableDistances[candidate]))
        {
            targetTile = candidate;
            return true;
        }

        foreach (Vector2 candidate in reachableDistances.Keys
            .Where(candidate => candidate != ownerTile && candidate != npcTile)
            .OrderBy(candidate => Vector2.Distance(candidate, ownerTile))
            .ThenBy(candidate => reachableDistances[candidate]))
        {
            targetTile = candidate;
            return true;
        }

        targetTile = npcTile;
        return false;
    }

    private bool IsCompanionAwayFromOwner(SquadMemberState member)
    {
        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null)
            return false;

        if (npc.currentLocation != owner.currentLocation)
            return true;

        return Vector2.Distance(NormalizeTile(npc.Tile), NormalizeTile(owner.Tile)) > MaxCompanionDistanceTiles;
    }

    private void OpenCompanionPanel(string? selectedNpcName = null)
    {
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
            toggleWaiting: this.ToggleWaitingFromPanel,
            recallMember: member => this.RecallCompanion(member.NpcName, member.OwnerId, showMessage: true),
            dismissMember: this.ConfirmDismissFromPanel,
            inventorySlots: this.config.CompanionInventorySlots);
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

        if (member.Mode == CompanionMode.Waiting)
            return this.Tr("companion.status.waiting");

        if (member.Mode == CompanionMode.ParkedForDisconnect)
            return this.Tr("companion.status.parked");

        if (member.CurrentActivityKey == "companion.status.stuck")
            return this.Tr("companion.status.stuck");

        if (this.pendingTasks.ContainsKey(member.NpcName))
            return this.Tr("companion.status.working");

        if (member.CurrentActivityKey == "companion.status.returning")
            return this.Tr("companion.status.returning");

        if (member.Inventory.Count >= this.config.CompanionInventorySlots)
            return this.Tr("companion.status.inventory_full");

        return this.Tr("companion.status.following");
    }

    private IReadOnlyList<string> GetCompanionPanelSummaryLines()
    {
        List<SquadMemberState> localMembers = this.GetLocalMembers().ToList();
        int working = localMembers.Count(p => this.pendingTasks.ContainsKey(p.NpcName));
        int fullInventories = localMembers.Count(p => p.Inventory.Count >= this.config.CompanionInventorySlots);
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
        else if (this.pendingTasks.ContainsKey(member.NpcName))
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
        TargetPreview preview = this.BuildTargetPreview(member, null);
        this.UpdateTargetPreview(member, preview);

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
        if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            return false;

        if (index < 0 || index >= member.Inventory.Count)
            return false;

        SavedItemStack saved = member.Inventory[index];
        Item? item = this.TryCreateItem(saved);
        if (item is null)
        {
            member.Inventory.RemoveAt(index);
            return true;
        }

        Item? notAdded = Game1.player.addItemToInventory(item);
        if (notAdded is null)
        {
            member.Inventory.RemoveAt(index);
            this.Info("companion.inventory.withdraw_complete", new { item = item.DisplayName });
            return true;
        }

        SavedItemStack? remaining = this.ToSavedItem(notAdded);
        if (remaining is not null)
            member.Inventory[index] = remaining;

        this.Warn("companion.inventory.withdraw_partial");
        return false;
    }

    private bool WithdrawAllCompanionInventoryItems(SquadMemberState member)
    {
        if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            return false;

        if (member.Inventory.Count == 0)
        {
            this.Info("companion.inventory.empty");
            return false;
        }

        List<SavedItemStack> remaining = new();
        bool movedAny = false;
        foreach (SavedItemStack saved in member.Inventory)
        {
            Item? item = this.TryCreateItem(saved);
            if (item is null)
                continue;

            Item? notAdded = Game1.player.addItemToInventory(item);
            if (notAdded is null)
            {
                movedAny = true;
                continue;
            }

            if (notAdded.Stack < item.Stack)
                movedAny = true;

            SavedItemStack? remainingStack = this.ToSavedItem(notAdded);
            if (remainingStack is not null)
                remaining.Add(remainingStack);
        }

        member.Inventory = remaining;
        if (remaining.Count == 0)
        {
            this.Info("companion.inventory.withdraw_all_complete", new { npc = member.DisplayName });
            return movedAny;
        }

        this.Warn("companion.inventory.withdraw_partial");
        return movedAny;
    }

    private void ToggleCompanionDirective(SquadMemberState member, CompanionDirective directive)
    {
        if (!this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            return;

        switch (directive)
        {
            case CompanionDirective.SearchWood:
                member.SearchWood = !member.SearchWood;
                if (member.SearchWood)
                    member.PreferredWorkSpecialty = CompanionWorkSpecialty.Wood;
                break;

            case CompanionDirective.SearchMining:
                member.SearchMining = !member.SearchMining;
                if (member.SearchMining)
                    member.PreferredWorkSpecialty = CompanionWorkSpecialty.Mining;
                break;

            case CompanionDirective.ClearArea:
                member.ClearArea = !member.ClearArea;
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
            this.RemovePendingTask(pending, "companion.task_failure.directive_disabled", returning: true);
        }

        this.InvalidateTargetPreviews();
    }

    private bool TryUnlockCompanionSkill(SquadMemberState member, string skillId)
    {
        if (!this.config.EnableCompanionProgression || !this.CanOwnerMutate(member, Game1.player.UniqueMultiplayerID))
            return false;

        CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skillId);
        if (skill is null)
            return false;

        if (member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(skill.PrerequisiteId)
            && !member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase))
        {
            this.Warn("companion.skill.locked");
            return false;
        }

        if (member.UnspentSkillPoints < skill.Cost)
        {
            this.Warn("companion.skill.no_points");
            return false;
        }

        member.UnspentSkillPoints -= skill.Cost;
        member.UnlockedSkillIds.Add(skill.Id);
        this.Info("companion.skill.unlocked", new { skill = this.Tr(skill.NameKey), npc = member.DisplayName });
        return true;
    }

    private void OpenSquadInventoryMenu()
    {
        if (!this.config.UseSquadInventory && this.squadInventory.Count == 0)
        {
            this.Warn("squad.inventory_disabled_empty");
            return;
        }

        string summary = this.squadInventory.Count == 0
            ? this.Tr("squad.inventory_empty")
            : string.Join(Environment.NewLine, this.squadInventory.Select(p => this.Tr("squad.inventory_line", new { item = p.DisplayName, count = p.Stack })));

        List<Response> responses = new()
        {
            new Response("Withdraw", this.Tr("squad.withdraw_all")),
            new Response("Cancel", this.Tr("generic.cancel"))
        };

        Game1.currentLocation.createQuestionDialogue(
            this.Tr("squad.inventory_title") + Environment.NewLine + summary,
            responses.ToArray(),
            (_, answer) =>
            {
                if (answer == "Withdraw")
                    this.WithdrawSquadInventory();
            });
    }

    private void WithdrawSquadInventory()
    {
        if (this.squadInventory.Count == 0)
        {
            this.Info("squad.inventory_empty");
            return;
        }

        List<Item> remaining = new();
        foreach (Item item in this.squadInventory)
        {
            Item copy = item.getOne();
            copy.Stack = item.Stack;
            Item? notAdded = Game1.player.addItemToInventory(copy);
            if (notAdded is not null)
                remaining.Add(notAdded);
        }

        this.squadInventory.Clear();
        this.squadInventory.AddRange(remaining);

        if (remaining.Count == 0)
            this.Info("squad.withdraw_complete");
        else
            this.Warn("squad.withdraw_partial");
    }

    private bool RouteItemToInventoryOrDrop(Item item, GameLocation location, Vector2 tile)
    {
        Item? notAdded;
        if (this.config.UseSquadInventory)
            notAdded = this.AddToSquadInventory(item);
        else
            notAdded = this.AddToPlayerInventory(item);

        if (notAdded is null)
            return true;

        Game1.createItemDebris(notAdded, tile * 64f, Game1.player.FacingDirection, location);
        return true;
    }

    private bool TryAddToSquadInventory(Item item, out Item? notAdded)
    {
        notAdded = this.AddToSquadInventory(item);
        return notAdded is null;
    }

    private Item? AddToSquadInventory(Item item)
    {
        Item copy = CloneItemStack(item);
        foreach (Item existing in this.squadInventory)
        {
            if (existing.canStackWith(copy))
            {
                int remainder = existing.addToStack(copy);
                if (remainder <= 0)
                    return null;

                copy.Stack = remainder;
            }
        }

        if (this.squadInventory.Count >= SquadInventoryCapacity)
            return copy;

        this.squadInventory.Add(copy);
        return null;
    }

    private Item? AddToCompanionInventory(SquadMemberState member, Item item)
    {
        Item copy = CloneItemStack(item);

        for (int i = 0; i < member.Inventory.Count; i++)
        {
            Item? existing = this.TryCreateItem(member.Inventory[i]);
            if (existing is null)
                continue;

            if (!existing.canStackWith(copy))
                continue;

            int remainder = existing.addToStack(copy);
            SavedItemStack? savedExisting = this.ToSavedItem(existing);
            if (savedExisting is not null)
                member.Inventory[i] = savedExisting;

            if (remainder <= 0)
                return null;

            copy.Stack = remainder;
        }

        if (member.Inventory.Count >= this.config.CompanionInventorySlots)
            return copy;

        SavedItemStack? saved = this.ToSavedItem(copy);
        if (saved is null)
            return copy;

        member.Inventory.Add(saved);
        return null;
    }

    private void RouteTaskRewardOrDrop(SquadMemberState member, Item item, GameLocation location, Vector2 tile, string sourceKey)
    {
        this.RecordCompanionLoot(member, item, sourceKey);

        Item? notAdded = this.AddToCompanionInventory(member, item);
        if (notAdded is null)
            return;

        notAdded = this.AddToPlayerInventory(notAdded);
        if (notAdded is null)
            return;

        Game1.createItemDebris(notAdded, tile * 64f, Game1.player.FacingDirection, location);
        this.SetTaskFailure(member, "companion.task_failure.inventory_full_world_drop");
        this.Warn("companion.inventory.full", new { npc = member.DisplayName });
    }

    private Item? AddToPlayerInventory(Item item)
    {
        return Game1.player.addItemToInventory(CloneItemStack(item));
    }

    private static Item CloneItemStack(Item item)
    {
        Item copy = item.getOne();
        copy.Stack = Math.Max(1, item.Stack);
        return copy;
    }

    private void AddCompanionXp(SquadMemberState member, int amount)
    {
        if (!this.config.EnableCompanionProgression || amount <= 0)
            return;

        int oldLevel = member.Level <= 0 ? 1 : member.Level;
        member.Xp = Math.Max(0, member.Xp + amount);
        int newLevel = CompanionProgression.GetLevelForXp(member.Xp);
        if (newLevel <= oldLevel)
        {
            member.Level = oldLevel;
            return;
        }

        member.Level = newLevel;
        member.UnspentSkillPoints += newLevel - oldLevel;
        if (oldLevel < CompanionProgression.MaxLevel
            && newLevel >= CompanionProgression.MaxLevel
            && !member.BonusLevelTenPointGranted)
        {
            member.UnspentSkillPoints++;
            member.BonusLevelTenPointGranted = true;
        }

        if (this.config.ShowCompanionLevelUpHud)
        {
            this.ShowCompanionHudNotice(
                member,
                this.Tr("companion.level_up", new { npc = member.DisplayName, level = newLevel, points = member.UnspentSkillPoints }),
                itemQualifiedId: null,
                new Color(96, 165, 220));
            Game1.playSound("newArtifact");
        }
    }

    private void RecordCompanionLoot(SquadMemberState member, Item item, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(item.QualifiedItemId))
            return;

        RecentCompanionLoot loot = new()
        {
            QualifiedItemId = item.QualifiedItemId,
            DisplayName = item.DisplayName,
            Stack = Math.Max(1, item.Stack),
            SourceKey = sourceKey,
            AddedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks
        };

        member.RecentLoot.Insert(0, loot);
        if (member.RecentLoot.Count > RecentLootLimit)
            member.RecentLoot.RemoveRange(RecentLootLimit, member.RecentLoot.Count - RecentLootLimit);

        if (this.IsImportantLoot(item))
        {
            this.ShowCompanionHudNotice(
                member,
                this.Tr("companion.loot.important", new { npc = member.DisplayName, item = item.DisplayName, count = item.Stack }),
                item.QualifiedItemId,
                new Color(218, 170, 65));
            Game1.playSound("discoverMineral");
        }
    }

    private bool IsImportantLoot(Item item)
    {
        string id = item.QualifiedItemId;
        if (id is "(O)909" or "(O)386" or "(O)384" or "(O)74" or "(O)72" or "(O)60" or "(O)62" or "(O)64" or "(O)66" or "(O)68" or "(O)70")
            return true;

        try
        {
            return item.sellToStorePrice(Game1.player.UniqueMultiplayerID) >= 250;
        }
        catch
        {
            return false;
        }
    }

    private void ShowCompanionHudNotice(SquadMemberState member, string text, string? itemQualifiedId, Color accent)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        int now = Game1.ticks;
        this.companionHudNotices.RemoveAll(p => now - p.StartedTick > p.DurationTicks);
        this.companionHudNotices.Add(new CompanionHudNotice(member.NpcName, text, itemQualifiedId, now, CompanionHudNoticeDurationTicks, accent));
        if (this.companionHudNotices.Count > 4)
            this.companionHudNotices.RemoveRange(0, this.companionHudNotices.Count - 4);
    }

    private void ShowMovementDebugNotice(SquadMemberState member, string key, object? tokens = null)
    {
        if (!this.config.ShowCompanionMovementDebug || member.OwnerId != Game1.player.UniqueMultiplayerID)
            return;

        int now = Game1.ticks;
        if (this.lastMovementDebugNoticeTicks.TryGetValue(member.NpcName, out int lastTick)
            && now - lastTick < CompanionHudNoticeDurationTicks)
        {
            return;
        }

        this.lastMovementDebugNoticeTicks[member.NpcName] = now;
        this.ShowCompanionHudNotice(member, this.Tr(key, tokens), itemQualifiedId: null, new Color(90, 165, 220));
    }

    private void DrawCompanionHudNotices(SpriteBatch b)
    {
        int now = Game1.ticks;
        this.companionHudNotices.RemoveAll(p => now - p.StartedTick > p.DurationTicks);
        if (this.companionHudNotices.Count == 0)
            return;

        const int width = 360;
        const int height = 72;
        int x = Math.Max(20, Game1.uiViewport.Width - width - 28);
        int y = 82;
        foreach (CompanionHudNotice notice in this.companionHudNotices.TakeLast(3))
        {
            float age = Math.Clamp((now - notice.StartedTick) / (float)Math.Max(1, notice.DurationTicks), 0f, 1f);
            float alpha = age < 0.78f ? 1f : Math.Clamp(1f - (age - 0.78f) / 0.22f, 0f, 1f);
            Rectangle bounds = new(x, y, width, height);
            b.Draw(Game1.staminaRect, bounds, Color.Black * 0.45f * alpha);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6), new Color(248, 238, 216) * alpha);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, 6, bounds.Height), notice.Accent * alpha);

            Rectangle iconBounds = new(bounds.X + 14, bounds.Y + 10, 52, 52);
            b.Draw(Game1.staminaRect, iconBounds, new Color(255, 247, 226) * alpha);
            if (!string.IsNullOrWhiteSpace(notice.ItemQualifiedId))
                this.DrawHudItemIcon(b, notice.ItemQualifiedId, iconBounds, alpha);
            else
                this.DrawHudPortrait(b, this.GetNpcByName(notice.NpcName), iconBounds, alpha);

            Utility.drawTextWithShadow(
                b,
                this.FitHudText(notice.Text, Game1.smallFont, width - 86),
                Game1.smallFont,
                new Vector2(bounds.X + 78, bounds.Y + 23),
                new Color(62, 42, 27) * alpha);
            y += height + 8;
        }
    }

    private void DrawCompanionMovementDebug(SpriteBatch b)
    {
        if (!this.config.ShowCompanionMovementDebug || Game1.activeClickableMenu is not null)
            return;

        List<SquadMemberState> localMembers = this.GetLocalMembers().Take(6).ToList();
        if (localMembers.Count == 0)
            return;

        List<string> lines = new()
        {
            this.Tr("companion.movement_debug.title", new { formation = this.Tr($"config.enum.{this.config.CompanionFormationMode}") })
        };

        foreach (SquadMemberState member in localMembers)
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
            string distance = "--";
            if (npc is not null && owner is not null && npc.currentLocation == owner.currentLocation)
                distance = Vector2.Distance(NormalizeTile(npc.Tile), NormalizeTile(owner.Tile)).ToString("0.0");

            string target = "--";
            if (this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 followTarget))
                target = $"{(int)followTarget.X},{(int)followTarget.Y}";
            else if (member.CurrentTargetX >= 0 && member.CurrentTargetY >= 0)
                target = $"{member.CurrentTargetX},{member.CurrentTargetY}";

            string recovery = this.followRecoveryUntilTick.ContainsKey(member.NpcName)
                ? this.Tr("companion.movement_debug.recovery")
                : "";
            lines.Add(this.Tr("companion.movement_debug.line", new
            {
                npc = member.DisplayName,
                status = this.GetCompanionStatusText(member),
                distance,
                target,
                recovery
            }));
        }

        int width = Math.Clamp((int)lines.Max(p => Game1.smallFont.MeasureString(p).X) + 24, 280, 520);
        int height = 18 + lines.Count * 24;
        int x = Math.Max(20, Game1.uiViewport.Width - width - 28);
        int y = Math.Max(20, Game1.uiViewport.Height - height - 28);
        Rectangle bounds = new(x, y, width, height);

        b.Draw(Game1.staminaRect, bounds, Color.Black * 0.58f);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, bounds.Width, 3), new Color(90, 165, 220) * 0.9f);

        int textY = bounds.Y + 10;
        foreach (string line in lines)
        {
            Utility.drawTextWithShadow(
                b,
                this.FitHudText(line, Game1.smallFont, width - 22),
                Game1.smallFont,
                new Vector2(bounds.X + 12, textY),
                Color.White);
            textY += 24;
        }
    }

    private void DrawHudItemIcon(SpriteBatch b, string itemQualifiedId, Rectangle bounds, float alpha)
    {
        try
        {
            Item item = ItemRegistry.Create(itemQualifiedId, allowNull: false);
            item.drawInMenu(b, new Vector2(bounds.X + 4, bounds.Y + 4), 0.8f);
        }
        catch
        {
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 12, bounds.Y + 12, bounds.Width - 24, bounds.Height - 24), Color.Gold * alpha);
        }
    }

    private void DrawHudPortrait(SpriteBatch b, NPC? npc, Rectangle bounds, float alpha)
    {
        if (npc is not null)
        {
            try
            {
                Texture2D portrait = Game1.content.Load<Texture2D>($"Portraits/{npc.Name}");
                b.Draw(portrait, new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8), new Rectangle(0, 0, 64, 64), Color.White * alpha);
                return;
            }
            catch
            {
                // Some companions are pets or custom actors without portraits.
            }
        }

        if (npc?.Sprite?.Texture is not null)
            b.Draw(npc.Sprite.Texture, new Rectangle(bounds.X + 12, bounds.Y + 5, 28, 42), npc.Sprite.SourceRect, Color.White * alpha);
    }

    private string FitHudText(string text, SpriteFont font, int width)
    {
        if (width <= 0)
            return "";

        if (font.MeasureString(text).X <= width)
            return text;

        const string suffix = "...";
        while (text.Length > 0 && font.MeasureString(text + suffix).X > width)
            text = text[..^1];

        return text.Length == 0 ? "" : text + suffix;
    }

    private void ShowCompanionWorkSignal(NPC npc, GameLocation location, Vector2 tile, string kind)
    {
        Color color = kind switch
        {
            "wood" => new Color(111, 143, 76),
            "mining" => new Color(126, 132, 150),
            "forage" => new Color(91, 160, 90),
            "water" => new Color(80, 145, 210),
            "harvest" => new Color(196, 145, 62),
            "pet" => new Color(218, 126, 154),
            "return" => new Color(90, 165, 220),
            _ => new Color(225, 176, 76)
        };

        try
        {
            location.temporarySprites.Add(new TemporaryAnimatedSprite(10, tile * 64f + new Vector2(20f, -18f), color, 8, false, 45f)
            {
                alphaFade = 0.025f,
                motion = new Vector2(0f, -0.35f),
                scale = 0.8f,
                layerDepth = Math.Clamp((tile.Y * 64f + 96f) / 10000f, 0f, 1f)
            });
        }
        catch
        {
            // Visual signal is non-critical; keep the task running if a custom map rejects sprites.
        }

        if (kind is "forage" or "water" or "harvest" or "pet" or "return")
        {
            npc.jumpWithoutSound(2f);
            npc.shake(90);
        }
    }

    private bool HasSkill(SquadMemberState member, string skillId)
    {
        return member.UnlockedSkillIds.Contains(skillId, StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateAmbientDialogue()
    {
        if (!this.config.EnableCommunication || this.config.DialogueCooldownSeconds <= 0 || this.IsBlockedGameState(blockForMenu: true))
            return;

        long now = DateTimeOffset.UtcNow.UtcTicks;
        long cooldownTicks = TimeSpan.FromSeconds(this.config.DialogueCooldownSeconds).Ticks;

        foreach (SquadMemberState member in this.members.Values.Where(p => p.OwnerId == Game1.player.UniqueMultiplayerID && p.Mode == CompanionMode.Following))
        {
            if (now - member.LastDialogueUtcTicks < cooldownTicks)
                continue;

            if (this.random.NextDouble() > 0.35)
                continue;

            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            if (npc is null || npc.currentLocation != Game1.currentLocation)
                continue;

            if (this.Say(npc, "Idle", force: false))
                member.LastDialogueUtcTicks = now;
        }
    }

    private bool Say(NPC npc, string category, bool force)
    {
        if (!force && !this.config.EnableCommunication)
            return false;

        string? key = this.PickDialogueKey(npc, category);
        if (string.IsNullOrWhiteSpace(key))
            key = $"dialogue.{category}.generic";

        string line = this.Tr(key, new
        {
            npc = npc.displayName,
            player = Game1.player.displayName,
            hearts = this.GetFriendshipHearts(npc)
        });

        if (string.IsNullOrWhiteSpace(line) || line == key)
            return false;

        npc.showTextAboveHead(line);
        return true;
    }

    private string? PickDialogueKey(NPC npc, string category)
    {
        List<CompanionDialogueLine> candidates = new();
        foreach (string profileKey in this.GetProfileKeys(npc))
        {
            if (this.npcProfiles.TryGetValue(profileKey, out NpcCompanionProfile? profile)
                && profile.Dialogue.TryGetValue(category, out List<CompanionDialogueLine>? lines))
            {
                candidates.AddRange(lines.Where(p => this.ConditionMatches(npc, p.Condition)));
            }
        }

        return candidates.Count == 0 ? null : candidates[this.random.Next(candidates.Count)].TextKey;
    }

    private IEnumerable<string> GetProfileKeys(NPC npc)
    {
        yield return "Generic";

        if (npc is Pet)
        {
            string name = npc.Name;
            if (name.Contains("Cat", StringComparison.OrdinalIgnoreCase))
                yield return "All_Cat";
            else if (name.Contains("Dog", StringComparison.OrdinalIgnoreCase))
                yield return "All_Dog";
            else if (name.Contains("Turtle", StringComparison.OrdinalIgnoreCase))
                yield return "All_Turtle";
        }

        yield return npc.Name;
    }

    private bool ConditionMatches(NPC npc, string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        foreach (string rawToken in condition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool invert = rawToken.StartsWith('!');
            string token = invert ? rawToken[1..] : rawToken;
            bool result = token switch
            {
                "spouse" => Game1.player.spouse == npc.Name,
                "night" => Game1.timeOfDay >= 1800,
                "farm" => Game1.currentLocation?.Name == "Farm",
                "mine" => Game1.currentLocation is StardewValley.Locations.MineShaft,
                "volcano" => Game1.currentLocation is StardewValley.Locations.VolcanoDungeon,
                "pet" => npc is Pet,
                "villager" => npc.IsVillager,
                _ when token.StartsWith("hearts>=", StringComparison.OrdinalIgnoreCase)
                    => this.GetFriendshipHearts(npc) >= this.ParseTrailingInt(token),
                _ when token.StartsWith("hearts<", StringComparison.OrdinalIgnoreCase)
                    => this.GetFriendshipHearts(npc) < this.ParseTrailingInt(token),
                _ => false
            };

            if (invert)
                result = !result;

            if (!result)
                return false;
        }

        return true;
    }

    private void UpdateDisconnectTimeouts()
    {
        if (!Context.IsMainPlayer || this.config.ParkTimeoutMinutes <= 0)
            return;

        long now = DateTimeOffset.UtcNow.UtcTicks;
        long timeoutTicks = TimeSpan.FromMinutes(this.config.ParkTimeoutMinutes).Ticks;
        foreach (SquadMemberState member in this.members.Values.Where(p => p.Mode == CompanionMode.ParkedForDisconnect).ToList())
        {
            if (member.ParkedAtUtcTicks > 0 && now - member.ParkedAtUtcTicks >= timeoutTicks)
                this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
        }
    }

    private NPC? FindTargetNpc(ICursorPosition cursor)
    {
        GameLocation location = Game1.currentLocation;
        Vector2 cursorTile = cursor.GrabTile;
        Vector2 facingTile = this.GetFacingTile(Game1.player);
        Vector2 playerTile = Game1.player.Tile;

        return location.characters
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !p.IsInvisible)
            .Select(p => new
            {
                Npc = p,
                Distance = Math.Min(Vector2.Distance(p.Tile, cursorTile), Math.Min(Vector2.Distance(p.Tile, facingTile), Vector2.Distance(p.Tile, playerTile)))
            })
            .Where(p => p.Distance <= 2.25f)
            .OrderBy(p => p.Distance)
            .Select(p => p.Npc)
            .FirstOrDefault();
    }

    private Vector2 GetFacingTile(Farmer player)
    {
        Vector2 tile = player.Tile;
        return player.FacingDirection switch
        {
            0 => tile + new Vector2(0, -1),
            1 => tile + new Vector2(1, 0),
            2 => tile + new Vector2(0, 1),
            3 => tile + new Vector2(-1, 0),
            _ => tile
        };
    }

    private bool IsSupportedTarget(NPC npc, Farmer requester)
    {
        if (npc is Pet)
            return true;

        if (this.config.RecruitAllNpcs)
            return !npc.IsMonster && !npc.IsInvisible;

        return npc.CanSocialize || requester.friendshipData.ContainsKey(npc.Name) || this.npcProfiles.ContainsKey(npc.Name);
    }

    private bool MeetsFriendshipRequirement(NPC npc, Farmer requester)
    {
        if (npc is Pet || requester.spouse == npc.Name)
            return true;

        return this.GetFriendshipHearts(npc, requester) >= this.config.FriendshipRequirement;
    }

    private int GetFriendshipHearts(NPC npc)
    {
        return this.GetFriendshipHearts(npc, Game1.player);
    }

    private int GetFriendshipHearts(NPC npc, Farmer player)
    {
        return player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship)
            ? friendship.Points / 250
            : 0;
    }

    private bool CanOwnerMutate(SquadMemberState member, long ownerId)
    {
        if (member.OwnerId == ownerId)
            return true;

        this.Warn("recruitment.not_owner", new { npc = member.DisplayName });
        return false;
    }

    private int GetOwnerSlot(SquadMemberState member)
    {
        return this.members.Values
            .Where(p => p.OwnerId == member.OwnerId)
            .OrderBy(p => p.NpcName, StringComparer.OrdinalIgnoreCase)
            .TakeWhile(p => p.NpcName != member.NpcName)
            .Count();
    }

    private SquadMemberState? GetAvailableLocalMember()
    {
        return this.members.Values.FirstOrDefault(p => p.OwnerId == Game1.player.UniqueMultiplayerID && p.Mode == CompanionMode.Following && !this.pendingTasks.ContainsKey(p.NpcName));
    }

    private Farmer? GetOwnerFarmer(long ownerId)
    {
        if (Game1.player.UniqueMultiplayerID == ownerId)
            return Game1.player;

        return Game1.getOnlineFarmers().FirstOrDefault(p => p.UniqueMultiplayerID == ownerId);
    }

    private bool IsBlockedGameState(bool blockForMenu)
    {
        return !Context.IsWorldReady
            || Game1.currentLocation is null
            || Game1.eventUp
            || Game1.CurrentEvent is not null
            || Game1.fadeToBlack
            || Game1.currentMinigame is not null
            || Game1.isFestival()
            || (blockForMenu && Game1.activeClickableMenu is not null);
    }

    private bool AreTaskActionsSafe()
    {
        return !this.IsBlockedGameState(blockForMenu: true);
    }

    private void MaintainCompanionScheduleLocks(bool stopCurrentRoutes)
    {
        if (!Context.IsWorldReady)
            return;

        foreach (SquadMemberState member in this.members.Values)
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
                this.DisableNpcSchedule(npc, stopCurrentRoutes);
        }
    }

    private void DisableNpcSchedule(NPC npc, bool stopCurrentRoute)
    {
        npc.ignoreScheduleToday = true;
        if (!stopCurrentRoute)
            return;

        npc.ClearSchedule();
        npc.DirectionsToNewLocation = null;
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
    }

    private void RestoreNpcSchedule(NPC npc)
    {
        npc.ignoreScheduleToday = false;
        npc.DirectionsToNewLocation = null;
        npc.ClearSchedule();

        if (Context.IsWorldReady && !Game1.eventUp && Game1.CurrentEvent is null && !Game1.isFestival())
            npc.checkSchedule(Game1.timeOfDay);
    }

    private void SetCompanionActivity(SquadMemberState member, string activityKey)
    {
        member.CurrentActivityKey = string.IsNullOrWhiteSpace(activityKey)
            ? "companion.status.following"
            : activityKey;
    }

    private void SetCompanionTarget(SquadMemberState member, CompanionTaskKind kind, Vector2 tile)
    {
        member.CurrentTargetKey = kind switch
        {
            CompanionTaskKind.Lumbering => "companion.target.wood",
            CompanionTaskKind.Mining => "companion.target.mining",
            _ => ""
        };
        member.CurrentTargetX = (int)tile.X;
        member.CurrentTargetY = (int)tile.Y;
    }

    private void ClearCompanionTarget(SquadMemberState member)
    {
        member.CurrentTargetKey = "";
        member.CurrentTargetX = -1;
        member.CurrentTargetY = -1;
    }

    private void SetTaskResult(SquadMemberState member, string resultKey)
    {
        member.LastTaskResultKey = resultKey;
        member.LastFailureReasonKey = "";
    }

    private void SetTaskFailure(SquadMemberState member, string failureKey)
    {
        member.LastFailureReasonKey = failureKey;
    }

    private string GetWorkTargetKey(string locationName, Vector2 tile)
    {
        Vector2 normalized = NormalizeTile(tile);
        return $"{locationName}|{(int)normalized.X}|{(int)normalized.Y}";
    }

    private bool TryReserveWorkTarget(string npcName, string locationName, Vector2 tile)
    {
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workTargetReservations.TryGetValue(key, out string? owner) && !string.Equals(owner, npcName, StringComparison.OrdinalIgnoreCase))
            return false;

        this.workTargetReservations[key] = npcName;
        this.InvalidateTargetPreviews();
        return true;
    }

    private void ReleaseWorkTarget(string locationName, Vector2 tile)
    {
        if (this.workTargetReservations.Remove(this.GetWorkTargetKey(locationName, tile)))
            this.InvalidateTargetPreviews();
    }

    private void ReleaseWorkTargetsForNpc(string npcName)
    {
        bool removedAny = false;
        foreach (KeyValuePair<string, string> reservation in this.workTargetReservations.ToList())
        {
            if (string.Equals(reservation.Value, npcName, StringComparison.OrdinalIgnoreCase))
            {
                this.workTargetReservations.Remove(reservation.Key);
                removedAny = true;
            }
        }

        if (removedAny)
            this.InvalidateTargetPreviews();
    }

    private void RemovePendingTask(string npcName)
    {
        if (!this.pendingTasks.TryGetValue(npcName, out PendingCompanionTask? task))
            return;

        this.RemovePendingTask(task);
    }

    private void RemovePendingTask(PendingCompanionTask task, string? failureKey = null, bool returning = false)
    {
        this.pendingTasks.Remove(task.NpcName);
        this.ReleaseWorkTarget(task.LocationName, task.TargetTile);
        if (this.members.TryGetValue(task.NpcName, out SquadMemberState? member))
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
            {
                npc.controller = null;
                ResetCompanionMovementSpeed(npc);
                npc.Halt();
            }

            this.ClearCompanionTarget(member);
            if (!string.IsNullOrWhiteSpace(failureKey))
                this.SetTaskFailure(member, failureKey);

            this.SetCompanionActivity(member, returning ? "companion.status.returning" : "companion.status.following");
            if (returning)
            {
                if (npc?.currentLocation is not null)
                    this.ShowCompanionWorkSignal(npc, npc.currentLocation, npc.Tile, "return");
            }
        }
    }

    private void ValidateLoadedMembers()
    {
        foreach (SquadMemberState member in this.members.Values.ToList())
        {
            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            if (npc is null)
            {
                this.Monitor.Log($"Saved companion '{member.NpcName}' no longer exists and was removed from Pelican Companions state.", LogLevel.Warn);
                this.legacyOverflowItems.AddRange(member.Inventory);
                this.members.Remove(member.NpcName);
                this.ClearFollowState(member.NpcName);
                continue;
            }

            member.DisplayName = npc.displayName;
        }
    }

    private void RestorePersistedMemberPositions()
    {
        foreach (SquadMemberState member in this.members.Values)
        {
            if (member.Mode is not (CompanionMode.Waiting or CompanionMode.ParkedForDisconnect)
                || string.IsNullOrWhiteSpace(member.WaitingLocationName))
            {
                continue;
            }

            NPC? npc = this.GetNpcByName(member.NpcName);
            GameLocation? location = Game1.getLocationFromName(member.WaitingLocationName);
            if (npc is null || location is null)
            {
                this.Monitor.Log(
                    $"Could not restore waiting position for '{member.NpcName}' in '{member.WaitingLocationName}'. The saved state was kept.",
                    LogLevel.Warn);
                continue;
            }

            Vector2 waitingTile = NormalizeTile(new Vector2(member.WaitingTileX, member.WaitingTileY));
            if (npc.currentLocation != location || NormalizeTile(npc.Tile) != waitingTile)
                this.PlaceNpc(npc, location, waitingTile);

            npc.controller = null;
            npc.Halt();
        }
    }

    private void ClearFollowState(string npcName)
    {
        this.lastFollowTargets.Remove(npcName);
        this.lastFollowTargetDistances.Remove(npcName);
        this.lastFollowPathTicks.Remove(npcName);
        this.lastFollowProgressPositions.Remove(npcName);
        this.activeRecallTargets.Remove(npcName);
        this.followNoProgressTicks.Remove(npcName);
        this.followRecoveryUntilTick.Remove(npcName);
        this.lastMovementDebugNoticeTicks.Remove(npcName);
    }

    private List<SavedItemStack> NormalizeLoadedMember(SquadMemberState member)
    {
        if (!Enum.IsDefined(member.PreferredWorkSpecialty))
            member.PreferredWorkSpecialty = CompanionWorkSpecialty.ClearArea;

        if (!member.ClearArea)
        {
            if (member.SearchWood && !member.SearchMining)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Wood;
            else if (member.SearchMining && !member.SearchWood)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Mining;
        }

        member.Level = Math.Clamp(member.Level <= 0 ? CompanionProgression.GetLevelForXp(member.Xp) : member.Level, 1, CompanionProgression.MaxLevel);
        member.Xp = Math.Max(0, member.Xp);
        int actualLevel = CompanionProgression.GetLevelForXp(member.Xp);
        if (actualLevel > member.Level)
        {
            member.UnspentSkillPoints += actualLevel - member.Level;
            member.Level = actualLevel;
        }

        if (member.Level >= CompanionProgression.MaxLevel && !member.BonusLevelTenPointGranted)
        {
            member.UnspentSkillPoints++;
            member.BonusLevelTenPointGranted = true;
        }

        member.UnspentSkillPoints = Math.Max(0, member.UnspentSkillPoints);
        member.UnlockedSkillIds = member.UnlockedSkillIds
            .Where(p => CompanionProgression.Skills.Any(skill => skill.Id == p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<SavedItemStack> validInventory = new();
        List<SavedItemStack> overflow = new();
        foreach (SavedItemStack saved in member.Inventory.Where(p => !string.IsNullOrWhiteSpace(p.QualifiedItemId) && p.Stack > 0))
        {
            // Keep unresolved custom items in the persisted overflow instead of
            // leaving invisible holes which would shift panel click indices.
            if (this.TryCreateItem(saved) is null)
                overflow.Add(saved);
            else
                validInventory.Add(saved);
        }

        overflow.AddRange(validInventory.Skip(this.config.CompanionInventorySlots));
        member.Inventory = validInventory.Take(this.config.CompanionInventorySlots).ToList();
        member.CurrentActivityKey = string.IsNullOrWhiteSpace(member.CurrentActivityKey)
            ? "companion.status.following"
            : member.CurrentActivityKey;
        member.LastTaskResultKey ??= "";
        member.LastFailureReasonKey ??= "";
        if (string.IsNullOrWhiteSpace(member.CurrentTargetKey))
        {
            member.CurrentTargetKey = "";
            member.CurrentTargetX = -1;
            member.CurrentTargetY = -1;
        }

        if (string.IsNullOrWhiteSpace(member.PreviewTargetKey))
        {
            member.PreviewTargetKey = "";
            member.PreviewTargetX = -1;
            member.PreviewTargetY = -1;
        }

        member.PreviewReasonKey ??= "";
        member.RecentLoot = (member.RecentLoot ?? new List<RecentCompanionLoot>())
            .Where(p => !string.IsNullOrWhiteSpace(p.QualifiedItemId) && p.Stack > 0)
            .OrderByDescending(p => p.AddedAtUtcTicks)
            .Take(RecentLootLimit)
            .ToList();

        return overflow;
    }

    private void ReloadOverflowInventoryIntoSquad()
    {
        if (this.legacyOverflowItems.Count == 0)
            return;

        List<SavedItemStack> remaining = new();
        foreach (SavedItemStack saved in this.legacyOverflowItems)
        {
            Item? item = this.TryCreateItem(saved);
            if (item is null)
            {
                // The providing content mod may be temporarily missing. Keep the
                // raw stack in save data so reinstalling that content can restore it.
                remaining.Add(saved);
                continue;
            }

            if (this.TryAddToSquadInventory(item, out Item? notAdded))
            {
                continue;
            }

            SavedItemStack? notAddedStack = this.ToSavedItem(notAdded ?? item);
            if (notAddedStack is not null)
                remaining.Add(notAddedStack);
        }

        this.legacyOverflowItems.Clear();
        this.legacyOverflowItems.AddRange(remaining);
    }

    private void LoadNpcProfiles()
    {
        try
        {
            this.npcProfiles = this.Helper.GameContent.Load<Dictionary<string, NpcCompanionProfile>>(NpcConfigAssetKey);
        }
        catch (Exception ex)
        {
            this.npcProfiles = new Dictionary<string, NpcCompanionProfile>(StringComparer.OrdinalIgnoreCase);
            this.Monitor.Log($"Failed loading NPC companion config. Generic dialogue fallback will be used. {ex}", LogLevel.Warn);
        }
    }

    private SavedModState BuildSaveData()
    {
        return new SavedModState
        {
            Version = 3,
            Members = this.members.Values.ToList(),
            TaskTogglesByPlayer = this.taskToggles.ToDictionary(p => p.Key.ToString(), p => p.Value),
            SquadInventory = this.squadInventory.Select(this.ToSavedItem).Where(p => p is not null).Cast<SavedItemStack>().ToList(),
            LegacyOverflowItems = this.legacyOverflowItems.ToList()
        };
    }

    private SavedItemStack? ToSavedItem(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.QualifiedItemId))
            return null;

        return new SavedItemStack
        {
            QualifiedItemId = item.QualifiedItemId,
            Stack = Math.Max(1, item.Stack),
            Quality = item.Quality
        };
    }

    private Item? TryCreateItem(SavedItemStack saved)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(saved.QualifiedItemId))
                return null;

            return ItemRegistry.Create(saved.QualifiedItemId, Math.Max(1, saved.Stack), saved.Quality, allowNull: true);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not restore squad inventory item '{saved.QualifiedItemId}': {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    private void SendActionRequest(string action, string npcName)
    {
        this.Helper.Multiplayer.SendMessage(
            new SquadActionMessage { Action = action, NpcName = npcName },
            MessageActionRequest,
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    private void RegisterGenericModConfigMenu()
    {
        IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(
            this.ModManifest,
            reset: () => this.config = new ModConfig(),
            save: () =>
            {
                this.config.Validate();
                this.Helper.WriteConfig(this.config);
            },
            titleScreenOnly: false);

        this.AddParagraph(gmcm, "config.overview");

        this.AddSection(gmcm, "config.section.controls");
        this.AddParagraph(gmcm, "config.section.controls.description");
        this.AddKeybindOption(gmcm, "recruitKey", () => this.config.RecruitKey, value => this.config.RecruitKey = value);
        this.AddKeybindOption(gmcm, "manualTaskKey", () => this.config.ManualTaskKey, value => this.config.ManualTaskKey = value);
        this.AddKeybindOption(gmcm, "openSquadInventoryKey", () => this.config.OpenSquadInventoryKey, value => this.config.OpenSquadInventoryKey = value);
        this.AddKeybindOption(gmcm, "tasksToggleKey", () => this.config.TasksToggleKey, value => this.config.TasksToggleKey = value);
        this.AddKeybindOption(gmcm, "openCompanionPanelKey", () => this.config.OpenCompanionPanelKey, value => this.config.OpenCompanionPanelKey = value);
        this.AddKeybindOption(gmcm, "recallAllCompanionsKey", () => this.config.RecallAllCompanionsKey, value => this.config.RecallAllCompanionsKey = value);

        this.AddSection(gmcm, "config.section.interface");
        this.AddParagraph(gmcm, "config.section.interface.description");
        this.AddBoolOption(gmcm, "showCompanionQuickHud", () => this.config.ShowCompanionQuickHud, value => this.config.ShowCompanionQuickHud = value);
        this.AddEnumOption(gmcm, "companionQuickHudMode", () => this.config.CompanionQuickHudMode, value => this.config.CompanionQuickHudMode = value);
        this.AddBoundedIntOption(gmcm, "companionQuickHudMaxRows", () => this.config.CompanionQuickHudMaxRows, value => this.config.CompanionQuickHudMaxRows = value, 1, 12);
        this.AddBoolOption(gmcm, "showCompanionLevelUpHud", () => this.config.ShowCompanionLevelUpHud, value => this.config.ShowCompanionLevelUpHud = value);

        this.AddSection(gmcm, "config.section.movement");
        this.AddEnumOption(gmcm, "companionFormationMode", () => this.config.CompanionFormationMode, value => this.config.CompanionFormationMode = value);
        this.AddBoolOption(gmcm, "showCompanionMovementDebug", () => this.config.ShowCompanionMovementDebug, value => this.config.ShowCompanionMovementDebug = value);

        this.AddSection(gmcm, "config.section.recruitment");
        this.AddBoundedIntOption(gmcm, "friendshipRequirement", () => this.config.FriendshipRequirement, value => this.config.FriendshipRequirement = value, 0, 14);
        this.AddIntOption(gmcm, "friendshipPointsPerHour", () => this.config.FriendshipPointsPerHour, value => this.config.FriendshipPointsPerHour = value);
        this.AddBoundedIntOption(gmcm, "maxSquadSize", () => this.config.MaxSquadSize, value => this.config.MaxSquadSize = value, 1, 12);
        this.AddBoolOption(gmcm, "recruitAllNpcs", () => this.config.RecruitAllNpcs, value => this.config.RecruitAllNpcs = value);
        this.AddEnumOption(gmcm, "disableInteraction", () => this.config.DisableInteraction, value => this.config.DisableInteraction = value);
        this.AddEnumOption(gmcm, "disableTrashRummagingReaction", () => this.config.DisableTrashRummagingReaction, value => this.config.DisableTrashRummagingReaction = value);

        this.AddSection(gmcm, "config.section.inventory");
        this.AddBoolOption(gmcm, "useSquadInventory", () => this.config.UseSquadInventory, value => this.config.UseSquadInventory = value);
        this.AddBoolOption(gmcm, "useVanillaDialogueUi", () => this.config.UseVanillaDialogueUi, value => this.config.UseVanillaDialogueUi = value);
        this.AddBoolOption(gmcm, "enableCompanionProgression", () => this.config.EnableCompanionProgression, value => this.config.EnableCompanionProgression = value);
        this.AddBoundedIntOption(gmcm, "companionInventorySlots", () => this.config.CompanionInventorySlots, value => this.config.CompanionInventorySlots = value, 1, 10);
        this.AddBoundedIntOption(gmcm, "companionWorkRadius", () => this.config.CompanionWorkRadius, value => this.config.CompanionWorkRadius = value, 3, 20);
        this.AddBoundedIntOption(gmcm, "companionWorkReturnDistance", () => this.config.CompanionWorkReturnDistance, value => this.config.CompanionWorkReturnDistance = value, 3, 40);

        this.AddSection(gmcm, "config.section.dialogue");
        this.AddBoolOption(gmcm, "enableCommunication", () => this.config.EnableCommunication, value => this.config.EnableCommunication = value);
        this.AddIntOption(gmcm, "dialogueCooldownSeconds", () => this.config.DialogueCooldownSeconds, value => this.config.DialogueCooldownSeconds = value);
        this.AddBoolOption(gmcm, "enableIdleAnimations", () => this.config.EnableIdleAnimations, value => this.config.EnableIdleAnimations = value);

        this.AddSection(gmcm, "config.section.tasks");
        this.AddBoolOption(gmcm, "enableGathering", () => this.config.EnableGathering, value => this.config.EnableGathering = value);
        this.AddEnumOption(gmcm, "attackingMode", () => this.config.AttackingMode, value => this.config.AttackingMode = value);
        this.AddEnumOption(gmcm, "harvestingMode", () => this.config.HarvestingMode, value => this.config.HarvestingMode = value);
        this.AddIntOption(gmcm, "protectBeehouseFlowers", () => this.config.ProtectBeehouseFlowers, value => this.config.ProtectBeehouseFlowers = value);
        this.AddEnumOption(gmcm, "foragingMode", () => this.config.ForagingMode, value => this.config.ForagingMode = value);
        this.AddEnumOption(gmcm, "lumberingMode", () => this.config.LumberingMode, value => this.config.LumberingMode = value);
        this.AddEnumOption(gmcm, "miningMode", () => this.config.MiningMode, value => this.config.MiningMode = value);
        this.AddEnumOption(gmcm, "wateringMode", () => this.config.WateringMode, value => this.config.WateringMode = value);
        this.AddEnumOption(gmcm, "fishingMode", () => this.config.FishingMode, value => this.config.FishingMode = value);
        this.AddEnumOption(gmcm, "pettingMode", () => this.config.PettingMode, value => this.config.PettingMode = value);
        this.AddEnumOption(gmcm, "shearingMode", () => this.config.ShearingMode, value => this.config.ShearingMode = value);
        this.AddEnumOption(gmcm, "milkingMode", () => this.config.MilkingMode, value => this.config.MilkingMode = value);

        this.AddSection(gmcm, "config.section.presentation");
        this.AddBoolOption(gmcm, "enableRiding", () => this.config.EnableRiding, value => this.config.EnableRiding = value);
        this.AddBoolOption(gmcm, "enableSitting", () => this.config.EnableSitting, value => this.config.EnableSitting = value);

        this.AddSection(gmcm, "config.section.multiplayer");
        this.AddBoolOption(gmcm, "warpHomeOnDisconnect", () => this.config.WarpHomeOnDisconnect, value => this.config.WarpHomeOnDisconnect = value);
        this.AddIntOption(gmcm, "parkTimeoutMinutes", () => this.config.ParkTimeoutMinutes, value => this.config.ParkTimeoutMinutes = value);
    }

    private void AddSection(IGenericModConfigMenuApi gmcm, string key)
    {
        gmcm.AddSectionTitle(this.ModManifest, () => this.Tr(key), tooltip: null);
    }

    private void AddParagraph(IGenericModConfigMenuApi gmcm, string key)
    {
        gmcm.AddParagraph(this.ModManifest, () => this.Tr(key));
    }

    private void AddBoolOption(IGenericModConfigMenuApi gmcm, string key, Func<bool> get, Action<bool> set)
    {
        gmcm.AddBoolOption(this.ModManifest, get, set, () => this.Tr($"config.{key}.name"), () => this.Tr($"config.{key}.description"), key);
    }

    private void AddBoundedIntOption(IGenericModConfigMenuApi gmcm, string key, Func<int> get, Action<int> set, int min, int max)
    {
        string[] values = Enumerable.Range(min, max - min + 1)
            .Select(value => value.ToString())
            .ToArray();

        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (int.TryParse(value, out int parsed))
                {
                    set(Math.Clamp(parsed, min, max));
                    this.config.Validate();
                }
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            values,
            value => value,
            fieldId: key);
    }

    private void AddIntOption(IGenericModConfigMenuApi gmcm, string key, Func<int> get, Action<int> set)
    {
        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (int.TryParse(value, out int parsed))
                {
                    set(parsed);
                    this.config.Validate();
                }
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            allowedValues: null,
            formatAllowedValue: null,
            fieldId: key);
    }

    private void AddKeybindOption(IGenericModConfigMenuApi gmcm, string key, Func<KeybindList> get, Action<KeybindList> set)
    {
        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (KeybindList.TryParse(value, out KeybindList? parsed, out _) && parsed is not null)
                    set(parsed);
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            allowedValues: null,
            formatAllowedValue: null,
            fieldId: key);
    }

    private void AddEnumOption<TEnum>(IGenericModConfigMenuApi gmcm, string key, Func<TEnum> get, Action<TEnum> set)
        where TEnum : struct, Enum
    {
        string[] values = Enum.GetNames<TEnum>();
        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
                    set(parsed);
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            values,
            value => this.Tr($"config.enum.{value}"),
            key);
    }

    private int ParseTrailingInt(string token)
    {
        string digits = new(token.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int parsed) ? parsed : 0;
    }

    private void Info(string key, object? tokens = null)
    {
        Game1.addHUDMessage(new HUDMessage(this.Tr(key, tokens)));
    }

    private void Warn(string key, object? tokens = null)
    {
        Game1.addHUDMessage(new HUDMessage(this.Tr(key, tokens), HUDMessage.error_type));
    }

    private string Tr(string key, object? tokens = null)
    {
        return tokens is null
            ? this.Helper.Translation.Get(key)
            : this.Helper.Translation.Get(key, tokens);
    }
}
