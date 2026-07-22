using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
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
    private const int CompanionProfilesSaveVersion = 11;
    private const int OperationalProfilesSaveVersion = 13;
    private const int CurrentSaveVersion = 13;
    private const string NpcConfigAssetKey = "Lucas.PelicanCompanions/NpcConfig";
    private const string MessageActionRequest = "CompanionActionRequest";
    private const string MessageStateRequest = "CompanionStateRequest";
    private const string MessageStateSnapshot = "CompanionStateSnapshot";
    private const string MessageStateUnavailable = "CompanionStateUnavailable";
    private const string MessageCommandFeedback = "CompanionCommandFeedback";
    private const string MessageCompanionExpression = "CompanionExpression";
    private const string MessageCompanionWorkVisual = "CompanionWorkVisual";
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
    // This is the stand-to-target adjacency radius. Reaching the stand itself
    // is checked by tile equality so a companion can't work from two tiles away.
    private const float TaskArrivalDistance = 1.1f;
    private const float FollowTrailMaxOwnerDistance = 2.25f;
    private const int FollowRepathCooldownTicks = 45;
    private const int FollowRecoveryRepathCooldownTicks = FollowRepathCooldownTicks;
    private const int FollowDisconnectedProbeCooldownTicks = 60;
    private const int FollowDisconnectedBackoffTicks = 300;
    private const int FollowTargetFailureBackoffTicks = 300;
    private const int FollowPathStartBudgetPerUpdate = 2;
    private const int OwnerStationaryThresholdTicks = 20;
    private const int FollowNoProgressUpdatesThreshold = 18;
    private const int FollowRecoveryDurationTicks = 90;
    private const int MaxFollowReachabilitySearchTiles = 2048;
    private const int ReachabilityCacheTtlTicks = 15;
    private const int TaskPlanningBudgetPerScan = 3;
    private const int TaskPathStartBudgetPerUpdate = 2;
    private const int TaskNoProgressUpdatesThreshold = 18;
    private const int RecentLootLimit = 5;
    private const int CompanionHudNoticeDurationTicks = 260;
    private const int DeferredActionMaxAgeTicks = 600;
    private const int FailedWorkTargetBackoffTicks = 600;
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
    private readonly Dictionary<string, CompanionProfileState> companionProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CompanionOperationalProfileKey, CompanionOperationalProfileState> operationalProfiles = new();
    private readonly Dictionary<long, CompanionOwnerLogisticsState> ownerLogistics = new();
    private readonly Dictionary<string, PendingCompanionTask> pendingTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> workTargetReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SharedWorkTargetReservation> sharedWorkTargetReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> workStandReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> followDestinationsThisUpdate = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeferredNpcRestoreState> deferredNpcRestores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NpcCosmeticState> npcCosmetics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NpcHatCacheEntry> npcHatCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, bool> taskToggles = new();
    private readonly Dictionary<long, List<FollowTrailPoint>> ownerTrails = new();
    private readonly Dictionary<string, Vector2> lastFollowTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> lastFollowTargetDistances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastFollowPathTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vector2> lastFollowProgressPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vector2> activeRecallTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> activeRecallActivatedTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vector2> recoveredFollowTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> followNoProgressTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastDisconnectedProbeTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> followRecoveryUntilTick = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastMovementDebugNoticeTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CompanionMovementControllerState> companionMovementControllers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> workTargetRetryAfterTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> priorityTaskPlanningMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> workAreaPositionRecoveryNeeded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<NPC, int> suppressedVanillaArrivals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ReachabilityCacheKey, ReachabilityCacheEntry> reachabilityCache = new();
    private readonly Dictionary<TargetPreviewCacheKey, TargetPreviewCacheEntry> targetPreviewCache = new();
    private readonly Dictionary<long, OwnerMovementSnapshot> ownerMovementSnapshots = new();
    private readonly List<Item> squadInventory = new();
    private readonly List<SavedItemStack> legacyOverflowItems = new();
    private readonly List<CompanionHudNotice> companionHudNotices = new();
    private readonly Queue<DeferredActionRequest> deferredActionRequests = new();
    private readonly CommandReplayGuard commandReplayGuard = new();
    private readonly Dictionary<NPC, int> vanillaMovementAllowances = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<NPC> controlledNpcLeases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<long, LocalSimulationBlockState> localOwnerSimulationBlocks = new();
    private readonly Random random = new();

    private ModConfig config = new();
    private PerScreen<CompanionQuickHud>? companionQuickHuds;
    private PerScreen<CompanionActionWheel>? companionActionWheels;
    private Dictionary<string, NpcCompanionProfile> npcProfiles = new(StringComparer.OrdinalIgnoreCase);
    private CompanionHostRules? replicatedHostRules;
    private int nextTaskScanTick;
    private long stateRevision;
    private long lastAppliedStateRevision = -1;
    private bool stateSnapshotDirty = true;
    private bool stateSnapshotFailureLogged;
    private int nextStateSnapshotRetryTick;
    private bool saveWritesBlocked;
    private bool planningFollowDestinations;
    private int followPathStartsRemaining;
    private int taskPathStartsRemaining;
    private int taskPlanningCursor;
    private int taskPreviewCursor;
    private bool pendingDailyCompanionRefresh;
    private long? commandFeedbackTargetPlayerId;
    private string? commandFeedbackAction;
    private string? commandFeedbackCommandId;
    private Harmony? harmony;

    private readonly record struct FollowTrailPoint(string LocationName, Vector2 Tile, int Tick);
    private readonly record struct OwnerMovementSnapshot(string LocationName, Vector2 Position, int LastMoveTick, int LastObservedTick, bool IsStationary);
    private readonly record struct WorkTarget(
        CompanionTaskKind Kind,
        Vector2 Tile,
        Vector2 StandTile,
        float NpcDistance,
        float PlayerDistance);
    private readonly record struct TargetPreview(bool HasTarget, string TargetKey, int X, int Y, string ReasonKey);
    private readonly record struct CompanionHudNotice(string NpcName, string Text, string? ItemQualifiedId, int StartedTick, int DurationTicks, Color Accent);
    private readonly record struct NpcHatCacheEntry(StardewValley.Objects.Hat? Hat, int CheckedAtTick);
    private readonly record struct ReachabilityCacheKey(string LocationName, int OriginX, int OriginY, int MaxVisitedTiles);
    private readonly record struct ReachabilityCacheEntry(int Tick, Dictionary<Vector2, int> Distances);
    private readonly record struct TargetPreviewCacheKey(
        string NpcName,
        CompanionDirective? SimulatedDirective,
        string LocationName,
        string NpcLocationName,
        int OwnerX,
        int OwnerY,
        int NpcX,
        int NpcY,
        bool SearchWood,
        bool SearchMining,
        bool SearchWatering,
        bool ClearArea,
        bool WorkAreaActive,
        string WorkAreaLocationName,
        int WorkAreaCenterX,
        int WorkAreaCenterY,
        int WorkAreaRadius,
        CompanionWorkSpecialty WorkAreaSpecialty,
        bool TasksEnabled,
        bool Blocked,
        bool WorkAreaRecoveryNeeded,
        CompanionMode Mode);
    private readonly record struct TargetPreviewCacheEntry(int Tick, TargetPreview Preview);
    private readonly record struct DeferredActionRequest(long PlayerId, SquadActionMessage Message, int ReceivedTick);
    private readonly record struct LocalSimulationBlockState(bool WithoutMenu, bool WithMenu, int Tick);
    private readonly record struct CompanionMovementControllerState(
        CompanionPathFindController Controller,
        CompanionMovementIntent Intent,
        string LocationName,
        Vector2 TargetTile);

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        this.MigrateConfig();
        this.config.Validate();
        helper.WriteConfig(this.config);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Input.MouseWheelScrolled += this.OnMouseWheelScrolled;
        helper.Events.Display.RenderedHud += this.OnRenderedHud;
        helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
        helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
        helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;

        CompanionBehaviorPatches.IsControlled = npc => Context.IsOnHostComputer
            && !this.pendingDailyCompanionRefresh
            && this.members.TryGetValue(npc.Name, out SquadMemberState? controlledMember)
            && controlledMember.Mode != CompanionMode.OriginalRoutine;
        CompanionBehaviorPatches.IsVanillaMovementExplicitlyAllowed = this.IsVanillaMovementAllowed;
        CompanionBehaviorPatches.ShouldSuppressVanillaArrival = npc => this.suppressedVanillaArrivals.ContainsKey(npc);
        CompanionBehaviorPatches.NeutralizeVanillaBedtimeController = this.NeutralizeVanillaBedtimeController;
        CompanionBehaviorPatches.DrawCosmeticHat = this.DrawNpcCosmeticHat;
        CompanionTaskDropPatches.BeforeTrackedTreeTick = this.BeforeTrackedTreeTick;
        CompanionTaskDropPatches.AfterTrackedTreeTick = this.AfterTrackedTreeTick;
        this.harmony = new Harmony(this.ModManifest.UniqueID);
        try
        {
            this.harmony.PatchAll(typeof(ModEntry).Assembly);
        }
        catch (Exception ex)
        {
            // Keep the core controller available if a future game update renames
            // one of the optional spouse/pet hooks. Harmony may already have
            // installed the compatible subset before reporting the bad target.
            this.Monitor.Log($"Some companion behavior guards could not be installed. Movement control may be reduced until the mod is updated. {ex}", LogLevel.Error);
        }

        this.companionQuickHuds = new PerScreen<CompanionQuickHud>(() => new CompanionQuickHud(
            getMembers: () => this.GetLocalMembers().ToList(),
            getNpc: this.GetNpcByName,
            translate: this.Tr,
            getStatusText: this.GetCompanionStatusText,
            isWorkActive: this.IsCompanionQuickWorkActive,
            getMode: () => this.config.CompanionQuickHudMode,
            getSide: () => this.config.CompanionQuickHudSide,
            getMaxVisibleRows: () => this.config.CompanionQuickHudMaxRows,
            getInventorySlotCount: this.GetCompanionInventoryCapacity,
            toggleWork: this.ToggleCompanionQuickWork,
            follow: this.FollowCompanionFromQuickHud,
            openPanel: member => this.OpenCompanionPanel(member.NpcName)));

        this.companionActionWheels = new PerScreen<CompanionActionWheel>(() => new CompanionActionWheel());
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

        if (this.config.ConfigVersion < 5)
        {
            this.config.CompanionQuickHudSide = CompanionQuickHudSide.Left;
            this.config.ConfigVersion = 5;
        }

        if (this.config.ConfigVersion < 6)
        {
            this.config.QuickActionWheelKey ??= KeybindList.Parse("X");
            this.config.ConfigVersion = 6;
        }

        if (this.config.ConfigVersion < 7)
        {
            // Adopt the refreshed dock's new home once. The side remains
            // configurable, so players can move it back after migration.
            this.config.CompanionQuickHudSide = CompanionQuickHudSide.Left;
            this.config.ConfigVersion = 7;
        }

        if (this.config.ConfigVersion < 8)
        {
            this.config.ControllerQuickActionWheelKey ??= KeybindList.Parse("LeftStick");
            this.config.CommunicationGroupCooldownSeconds = Math.Max(1, this.config.CommunicationGroupCooldownSeconds);
            this.config.EnablePetExpressions = true;
            this.config.ConfigVersion = 8;
        }
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(NpcConfigAssetKey))
            e.LoadFromModFile<Dictionary<string, NpcCompanionProfile>>("assets/NpcConfig.json", AssetLoadPriority.Exclusive);
    }
}
