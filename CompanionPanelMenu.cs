using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace PelicanCompanions;

/// <summary>A responsive, controller-friendly home for companion management.</summary>
internal sealed partial class CompanionPanelMenu : IClickableMenu
{
    private const int WindowMargin = 16;
    private const int WindowMaxWidth = 1180;
    private const int WindowMaxHeight = 780;
    private const int TitleBarHeight = 56;
    private const int BrandedTitleBarHeight = 92;
    private const int CompactViewportHeight = 540;
    private const int BrandedHeaderMinHeight = 620;
    private const int ExpandedMemberLayoutMinHeight = 600;
    private const int ContentGap = 10;
    private const int WideRosterWidth = 268;
    private const int MemberRowHeight = 70;
    private const int MemberRowGap = 6;
    private const int MemberScrollPadding = 5;
    private const int PortraitRetryTicks = 600;
    private const float PanelTitleTextScale = 0.74f;
    private const float PanelHeadingTextScale = 0.68f;
    private const float PanelTextScale = 0.62f;
    private const float PanelMetaTextScale = 0.55f;
    private const float PanelCompactTextScale = 0.50f;
    private const float PanelNumericTextScale = 0.70f;
    private const float PanelCompactNumericTextScale = 0.60f;

    // Shared visual language with AliveNPCs and Beach Episode: vanilla menu
    // frames, warm paper/sand surfaces, brown ink, and green/gold accents.
    // White is a neutral texture tint; the menu texture supplies the yellow paper.
    private static readonly Color WindowTextureTint = Color.White;
    private static readonly Color WindowBorder = new(91, 57, 36);
    private static readonly Color SurfaceTextureTint = Color.White;
    private static readonly Color SurfaceBorder = new(143, 103, 64);
    private static readonly Color RowColor = new(236, 228, 210);
    private static readonly Color RowHoverColor = new(255, 230, 190);
    private static readonly Color SelectedRowColor = new(232, 246, 218);
    private static readonly Color HeaderCardColor = new(255, 238, 185);
    private static readonly Color TabIdleColor = new(215, 199, 170);
    private static readonly Color TabActiveColor = new(250, 215, 135);
    private static readonly Color AccentGreen = new(130, 172, 116);
    private static readonly Color AccentBlue = new(70, 136, 191);
    private static readonly Color AccentGold = new(245, 190, 70);
    private static readonly Color DangerColor = new(198, 94, 82);
    private static readonly Color ButtonIdle = new(235, 210, 170);
    private static readonly Color ButtonActive = new(48, 118, 70);
    private static readonly Color ButtonDanger = new(200, 100, 80);
    private static readonly Color TextColor = new(91, 57, 36);
    private static readonly Color MutedTextColor = new(96, 88, 78);
    private static readonly CompanionEquipmentSlot[] EquipmentSlotOrder =
    {
        CompanionEquipmentSlot.Axe,
        CompanionEquipmentSlot.Pickaxe,
        CompanionEquipmentSlot.WateringCan,
        CompanionEquipmentSlot.FishingRod
    };

    private readonly Func<IEnumerable<SquadMemberState>> getMembers;
    private readonly Func<string, NPC?> getNpc;
    private readonly Func<string, object?, string> translate;
    private readonly Func<SquadMemberState, string> getStatusText;
    private readonly Func<IReadOnlyList<string>> getSummaryLines;
    private readonly Func<SquadMemberState, IReadOnlyList<string>> getDetailLines;
    private readonly Func<SquadMemberState, CompanionPanelMapInfo> getMapInfo;
    private readonly Func<SquadMemberState, CompanionDirective, string> getDirectivePreviewText;
    private readonly Func<SquadMemberState, List<Item>> getInventoryItems;
    private readonly Func<SquadMemberState, CompanionInventoryWorkspace> getInventoryWorkspace;
    private readonly Func<SquadMemberState, CompanionInventoryTransferRequest, bool> transferInventoryItem;
    private readonly Func<SquadMemberState, CompanionInventoryFilter, bool> getInventoryFilter;
    private readonly Func<SquadMemberState, CompanionInventoryFilter, bool> toggleInventoryFilter;
    private readonly Func<SquadMemberState, Item?> getEquippedHat;
    private readonly Func<SquadMemberState, bool> hasEquippedHat;
    private readonly Func<SquadMemberState, bool> changeHat;
    private readonly Func<SquadMemberState, CompanionEquipmentSlot, Item?> getEquipmentItem;
    private readonly Func<SquadMemberState, CompanionEquipmentSlot, bool> hasEquipmentItem;
    private readonly Func<SquadMemberState, CompanionEquipmentSlot, bool> changeEquipment;
    private readonly Func<SquadMemberState, int, bool> withdrawInventoryItem;
    private readonly Func<SquadMemberState, bool> withdrawAllInventoryItems;
    private readonly Func<SquadMemberState, CompanionRoutineState> getRoutine;
    private readonly Func<SquadMemberState, CompanionRoutineState, string, bool> saveRoutine;
    private readonly Action<SquadMemberState, CompanionDirective> toggleDirective;
    private readonly Func<SquadMemberState, string, bool> unlockSkill;
    private readonly Func<bool> isProgressionEnabled;
    private readonly Action<SquadMemberState> toggleWaiting;
    private readonly Action<SquadMemberState> recallMember;
    private readonly Action<SquadMemberState> dismissMember;
    private readonly int inventorySlots;

    private readonly List<(Rectangle Bounds, string NpcName)> memberRows = new();
    private readonly List<(Rectangle Bounds, PanelTab Tab)> tabButtons = new();
    private readonly List<(Rectangle Bounds, CompanionDirective Directive)> directiveButtons = new();
    private readonly List<(Rectangle Bounds, string SkillId)> skillButtons = new();
    private readonly List<(Rectangle Bounds, CompanionEquipmentSlot Slot)> equipmentSlotsBounds = new();
    private readonly List<(Rectangle Bounds, int Index)> inventorySlotsBounds = new();
    private readonly List<(Rectangle Bounds, int Index)> playerInventorySlotsBounds = new();
    private readonly List<(Rectangle Bounds, int Index)> chestInventorySlotsBounds = new();
    private readonly List<(Rectangle Bounds, CompanionInventoryEndpoint Endpoint)> inventoryPaneBounds = new();
    private readonly List<InventoryPanePageState> inventoryPanePages = new();
    private readonly List<(Rectangle Bounds, CompanionInventoryFilter Filter)> inventoryFilterButtons = new();
    private readonly List<Rectangle> focusTargets = new();
    private readonly Dictionary<string, PortraitCacheEntry> portraitCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InventoryDisplayCacheEntry> inventoryDisplayCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CompanionInventoryEndpoint, int> inventoryPageOffsets = new();

    private Rectangle memberListArea;
    private Rectangle previousMemberButton;
    private Rectangle nextMemberButton;
    private Rectangle withdrawAllButton;
    private Rectangle hatSlot;
    private Rectangle waitButton;
    private Rectangle recallButton;
    private Rectangle dismissButton;
    private Rectangle closeButton;
    private Rectangle skillDetailsArea;
    private int memberListScrollOffset;
    private int memberListMaxScroll;
    private int focusedControlIndex = -1;
    private string? selectedNpcName;
    private string? inspectedSkillId;
    private PanelTab currentTab = PanelTab.Team;
    private string hoverText = "";
    private bool wideLayout;
    private bool focusSkillOnNextRebuild;
    private bool skillDetailsEmbedded;
    private InventoryDragState? inventoryDrag;
    private CompanionInventoryWorkspace? inventoryWorkspaceCache;
    private string inventoryWorkspaceCacheNpcName = "";
    private int inventoryWorkspaceCacheUntilTick;
    private bool activatingFocusedControl;
    private bool focusInventoryDestinationOnNextRebuild;
    private CompanionInventoryEndpoint? controllerInventoryPageEndpoint;
    private Point controllerInventoryPageFocus;

    public CompanionPanelMenu(
        Func<IEnumerable<SquadMemberState>> getMembers,
        string? selectedNpcName,
        Func<string, NPC?> getNpc,
        Func<string, object?, string> translate,
        Func<SquadMemberState, string> getStatusText,
        Func<IReadOnlyList<string>> getSummaryLines,
        Func<SquadMemberState, IReadOnlyList<string>> getDetailLines,
        Func<SquadMemberState, CompanionPanelMapInfo> getMapInfo,
        Func<SquadMemberState, CompanionDirective, string> getDirectivePreviewText,
        Func<SquadMemberState, List<Item>> getInventoryItems,
        Func<SquadMemberState, CompanionInventoryWorkspace> getInventoryWorkspace,
        Func<SquadMemberState, CompanionInventoryTransferRequest, bool> transferInventoryItem,
        Func<SquadMemberState, CompanionInventoryFilter, bool> getInventoryFilter,
        Func<SquadMemberState, CompanionInventoryFilter, bool> toggleInventoryFilter,
        Func<SquadMemberState, Item?> getEquippedHat,
        Func<SquadMemberState, bool> hasEquippedHat,
        Func<SquadMemberState, bool> changeHat,
        Func<SquadMemberState, CompanionEquipmentSlot, Item?> getEquipmentItem,
        Func<SquadMemberState, CompanionEquipmentSlot, bool> hasEquipmentItem,
        Func<SquadMemberState, CompanionEquipmentSlot, bool> changeEquipment,
        Func<SquadMemberState, int, bool> withdrawInventoryItem,
        Func<SquadMemberState, bool> withdrawAllInventoryItems,
        Func<SquadMemberState, CompanionRoutineState> getRoutine,
        Func<SquadMemberState, CompanionRoutineState, string, bool> saveRoutine,
        Action<SquadMemberState, CompanionDirective> toggleDirective,
        Func<SquadMemberState, string, bool> unlockSkill,
        Func<bool> isProgressionEnabled,
        Action<SquadMemberState> toggleWaiting,
        Action<SquadMemberState> recallMember,
        Action<SquadMemberState> dismissMember,
        int inventorySlots)
        : base(
            WindowMargin,
            WindowMargin,
            Math.Max(1, Math.Min(WindowMaxWidth, Game1.uiViewport.Width - WindowMargin * 2)),
            Math.Max(1, Math.Min(WindowMaxHeight, Game1.uiViewport.Height - WindowMargin * 2)),
            false)
    {
        this.getMembers = getMembers;
        this.selectedNpcName = selectedNpcName;
        this.getNpc = getNpc;
        this.translate = translate;
        this.getStatusText = getStatusText;
        this.getSummaryLines = getSummaryLines;
        this.getDetailLines = getDetailLines;
        this.getMapInfo = getMapInfo;
        this.getDirectivePreviewText = getDirectivePreviewText;
        this.getInventoryItems = getInventoryItems;
        this.getInventoryWorkspace = getInventoryWorkspace;
        this.transferInventoryItem = transferInventoryItem;
        this.getInventoryFilter = getInventoryFilter;
        this.toggleInventoryFilter = toggleInventoryFilter;
        this.getEquippedHat = getEquippedHat;
        this.hasEquippedHat = hasEquippedHat;
        this.changeHat = changeHat;
        this.getEquipmentItem = getEquipmentItem;
        this.hasEquipmentItem = hasEquipmentItem;
        this.changeEquipment = changeEquipment;
        this.withdrawInventoryItem = withdrawInventoryItem;
        this.withdrawAllInventoryItems = withdrawAllInventoryItems;
        this.getRoutine = getRoutine;
        this.saveRoutine = saveRoutine;
        this.toggleDirective = toggleDirective;
        this.unlockSkill = unlockSkill;
        this.isProgressionEnabled = isProgressionEnabled;
        this.toggleWaiting = toggleWaiting;
        this.recallMember = recallMember;
        this.dismissMember = dismissMember;
        this.inventorySlots = Math.Max(1, inventorySlots);
        this.Reflow();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.closeButton.Contains(x, y))
        {
            this.CloseMenu();
            return;
        }

        if (this.previousMemberButton.Contains(x, y))
        {
            if (this.SelectRelativeMember(-1))
                Game1.playSound("shiny4");
            return;
        }

        if (this.nextMemberButton.Contains(x, y))
        {
            if (this.SelectRelativeMember(1))
                Game1.playSound("shiny4");
            return;
        }

        foreach ((Rectangle bounds, string npcName) in this.memberRows)
        {
            if (!bounds.Contains(x, y))
                continue;
            if (!string.Equals(
                    this.selectedNpcName,
                    npcName,
                    StringComparison.OrdinalIgnoreCase))
            {
                this.inventoryDrag = null;
                this.ResetInventoryViewport();
            }
            this.selectedNpcName = npcName;
            Game1.playSound("smallSelect");
            return;
        }

        foreach ((Rectangle bounds, PanelTab tab) in this.tabButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            this.SetTab(tab);
            return;
        }

        SquadMemberState? selected = this.GetSelectedMember();
        if (selected is null)
            return;

        if (this.currentTab == PanelTab.Team
            && this.TryHandleTeamDashboardClick(x, y))
        {
            return;
        }

        if (this.waitButton.Contains(x, y))
        {
            this.toggleWaiting(selected);
            Game1.playSound("smallSelect");
            return;
        }

        if (this.recallButton.Contains(x, y))
        {
            this.recallMember(selected);
            Game1.playSound("dwop");
            return;
        }

        if (this.dismissButton.Contains(x, y))
        {
            this.dismissMember(selected);
            Game1.playSound("bigDeSelect");
            return;
        }

        if (this.currentTab == PanelTab.Inventory)
        {
            if (this.hatSlot.Contains(x, y))
            {
                this.InvalidateInventoryWorkspaceCache();
                Game1.playSound(this.changeHat(selected) ? "coin" : "cancel");
                return;
            }

            foreach ((Rectangle bounds, CompanionEquipmentSlot slot) in this.equipmentSlotsBounds)
            {
                if (!bounds.Contains(x, y))
                    continue;
                this.InvalidateInventoryWorkspaceCache();
                Game1.playSound(this.changeEquipment(selected, slot) ? "coin" : "cancel");
                return;
            }

            if (this.withdrawAllButton.Contains(x, y))
            {
                this.InvalidateInventoryWorkspaceCache();
                Game1.playSound(this.withdrawAllInventoryItems(selected) ? "coin" : "cancel");
                return;
            }

            foreach ((Rectangle bounds, CompanionInventoryFilter filter) in this.inventoryFilterButtons)
            {
                if (!bounds.Contains(x, y))
                    continue;
                Game1.playSound(this.toggleInventoryFilter(selected, filter) ? "smallSelect" : "cancel");
                return;
            }

            if (this.TryBeginInventoryDrag(selected, x, y))
                return;
        }
        else if (this.currentTab == PanelTab.Work)
        {
            foreach ((Rectangle bounds, CompanionDirective directive) in this.directiveButtons)
            {
                if (!bounds.Contains(x, y))
                    continue;
                this.toggleDirective(selected, directive);
                Game1.playSound("drumkit6");
                return;
            }
        }
        else if (this.currentTab == PanelTab.Skills)
        {
            foreach ((Rectangle bounds, string skillId) in this.skillButtons)
            {
                if (!bounds.Contains(x, y))
                    continue;

                this.inspectedSkillId = skillId;
                CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skillId);
                if (skill is null)
                    return;

                CompanionSkillTreeState state = CompanionSkillTreePolicy.GetState(
                    skill,
                    selected.UnlockedSkillIds,
                    selected.UnspentSkillPoints,
                    this.isProgressionEnabled());
                if (state != CompanionSkillTreeState.Available)
                {
                    Game1.playSound(state == CompanionSkillTreeState.Learned ? "smallSelect" : "cancel");
                    return;
                }

                Game1.playSound(this.unlockSkill(selected, skillId) ? "newArtifact" : "cancel");
                return;
            }
        }
        else if (this.currentTab == PanelTab.Routine && this.HandleRoutineLeftClick(selected, x, y))
        {
            return;
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key is Keys.Escape or Keys.E)
        {
            if (this.inventoryDrag is not null)
            {
                this.inventoryDrag = null;
                Game1.playSound("bigDeSelect");
                return;
            }
            this.CloseMenu();
            return;
        }

        switch (key)
        {
            case Keys.Tab:
                this.MoveFocus(1);
                return;
            case Keys.Enter:
            case Keys.Space:
                this.ActivateFocusedControl();
                return;
            case Keys.Left:
            case Keys.A:
                this.CycleTab(-1);
                return;
            case Keys.Right:
            case Keys.D:
                this.CycleTab(1);
                return;
            case Keys.Up:
            case Keys.W:
                if ((this.currentTab == PanelTab.Team
                        ? this.SelectRelativeTeamDashboardMember(-1)
                        : this.SelectRelativeMember(-1)))
                    Game1.playSound("shiny4");
                return;
            case Keys.Down:
            case Keys.S:
                if ((this.currentTab == PanelTab.Team
                        ? this.SelectRelativeTeamDashboardMember(1)
                        : this.SelectRelativeMember(1)))
                    Game1.playSound("shiny4");
                return;
            case Keys.PageUp:
                if (this.currentTab == PanelTab.Inventory)
                {
                    this.TryScrollFocusedInventoryPane(1);
                    return;
                }
                if (this.SelectRelativeMember(-this.GetVisibleMemberRowCount()))
                    Game1.playSound("shiny4");
                return;
            case Keys.PageDown:
                if (this.currentTab == PanelTab.Inventory)
                {
                    this.TryScrollFocusedInventoryPane(-1);
                    return;
                }
                if (this.SelectRelativeMember(this.GetVisibleMemberRowCount()))
                    Game1.playSound("shiny4");
                return;
            case Keys.Home:
                this.SelectMemberByIndex(0);
                return;
            case Keys.End:
                this.SelectMemberByIndex(this.getMembers().Count() - 1);
                return;
            case Keys.D1:
                this.SetTab(PanelTab.Overview);
                return;
            case Keys.D2:
                this.SetTab(PanelTab.Work);
                return;
            case Keys.D3:
                this.SetTab(PanelTab.Skills);
                return;
            case Keys.D4:
                this.SetTab(PanelTab.Inventory);
                return;
            case Keys.D5:
                this.SetTab(PanelTab.Routine);
                return;
            case Keys.D6:
                this.SetTab(PanelTab.Team);
                return;
            default:
                base.receiveKeyPress(key);
                return;
        }
    }

    public override void receiveGamePadButton(Buttons button)
    {
        switch (button)
        {
            case Buttons.B:
            case Buttons.Back:
                if (this.inventoryDrag is not null)
                {
                    this.inventoryDrag = null;
                    Game1.playSound("bigDeSelect");
                    return;
                }
                this.CloseMenu();
                return;
            case Buttons.LeftShoulder:
                this.CycleTab(-1);
                return;
            case Buttons.RightShoulder:
                this.CycleTab(1);
                return;
            case Buttons.DPadUp:
                if (this.currentTab is PanelTab.Skills or PanelTab.Inventory)
                {
                    this.MoveFocusSpatial(0, -1);
                    return;
                }
                if ((this.currentTab == PanelTab.Team
                        ? this.SelectRelativeTeamDashboardMember(-1)
                        : this.SelectRelativeMember(-1)))
                    Game1.playSound("shiny4");
                return;
            case Buttons.DPadDown:
                if (this.currentTab is PanelTab.Skills or PanelTab.Inventory)
                {
                    this.MoveFocusSpatial(0, 1);
                    return;
                }
                if ((this.currentTab == PanelTab.Team
                        ? this.SelectRelativeTeamDashboardMember(1)
                        : this.SelectRelativeMember(1)))
                    Game1.playSound("shiny4");
                return;
            case Buttons.DPadLeft:
                if (this.currentTab is PanelTab.Skills or PanelTab.Inventory)
                    this.MoveFocusSpatial(-1, 0);
                else
                    this.MoveFocus(-1);
                return;
            case Buttons.DPadRight:
                if (this.currentTab is PanelTab.Skills or PanelTab.Inventory)
                    this.MoveFocusSpatial(1, 0);
                else
                    this.MoveFocus(1);
                return;
            case Buttons.LeftTrigger:
                if (this.currentTab == PanelTab.Inventory)
                {
                    this.TryScrollFocusedInventoryPane(1);
                    return;
                }
                base.receiveGamePadButton(button);
                return;
            case Buttons.RightTrigger:
                if (this.currentTab == PanelTab.Inventory)
                {
                    this.TryScrollFocusedInventoryPane(-1);
                    return;
                }
                base.receiveGamePadButton(button);
                return;
            case Buttons.A:
                this.ActivateFocusedControl();
                return;
            default:
                base.receiveGamePadButton(button);
                return;
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (this.currentTab == PanelTab.Inventory
            && this.TryScrollInventoryPane(
                direction,
                Game1.getMouseX(),
                Game1.getMouseY()))
        {
            return;
        }

        if (this.currentTab == PanelTab.Team
            && this.TryScrollTeamDashboard(
                direction,
                Game1.getMouseX(),
                Game1.getMouseY()))
        {
            return;
        }

        if (!this.wideLayout || this.memberListMaxScroll <= 0 || !this.memberListArea.Contains(Game1.getMouseX(), Game1.getMouseY()))
            return;

        int stride = MemberRowHeight + MemberRowGap;
        int delta = direction > 0 ? -stride : direction < 0 ? stride : 0;
        this.memberListScrollOffset = Math.Clamp(this.memberListScrollOffset + delta, 0, this.memberListMaxScroll);
    }

    public override void performHoverAction(int x, int y)
    {
        this.hoverText = "";
        SquadMemberState? selected = this.GetSelectedMember();
        if (selected is null)
            return;

        if (this.currentTab == PanelTab.Team
            && this.TryHandleTeamDashboardHover(x, y))
        {
            return;
        }

        if (this.waitButton.Contains(x, y))
        {
            this.hoverText = this.translate(selected.Mode == CompanionMode.Following ? "management.wait" : "management.resume", null);
            return;
        }
        if (this.recallButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.panel.recall_hover", new { npc = selected.DisplayName });
            return;
        }
        if (this.dismissButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.panel.dismiss_hover", new { npc = selected.DisplayName });
            return;
        }
        if (this.withdrawAllButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.inventory.withdraw_all", null);
            return;
        }

        foreach ((Rectangle bounds, CompanionEquipmentSlot slot) in this.equipmentSlotsBounds)
        {
            if (!bounds.Contains(x, y))
                continue;

            Item? equipment = this.getEquipmentItem(selected, slot);
            string label = this.translate(GetEquipmentSlotTranslationKey(slot), null);
            string state;
            if (equipment is WateringCan wateringCan)
            {
                string water = this.translate("companion.equipment.water", new
                {
                    current = wateringCan.WaterLeft,
                    capacity = CompanionEquipmentPolicy.GetWateringCanCapacity(wateringCan.UpgradeLevel)
                });
                state = $"{wateringCan.DisplayName} — {water}";
            }
            else if (equipment is Tool tool)
            {
                string upgrade = this.translate("companion.equipment.upgrade", new { level = tool.UpgradeLevel });
                state = $"{tool.DisplayName} — {upgrade}";
            }
            else if (equipment is not null)
                state = equipment.DisplayName;
            else if (this.hasEquipmentItem(selected, slot))
                state = this.translate("companion.equipment.unavailable", null);
            else
                state = this.translate("companion.equipment.empty", null);
            this.hoverText = $"{label}: {state}{Environment.NewLine}{this.translate("companion.equipment.hint", null)}";
            return;
        }

        if (this.hatSlot.Contains(x, y))
        {
            Item? hat = this.getEquippedHat(selected);
            this.hoverText = hat is not null
                ? this.translate("companion.hat.replace_hint", new { hat = hat.DisplayName })
                : this.hasEquippedHat(selected)
                    ? this.translate("companion.hat.unavailable_hint", null)
                    : this.translate("companion.hat.equip_hint", null);
            return;
        }

        foreach ((Rectangle bounds, int index) in this.inventorySlotsBounds)
        {
            if (!bounds.Contains(x, y))
                continue;
            IReadOnlyList<Item> items = this.GetCachedInventoryItems(selected);
            if (index >= 0 && index < items.Count)
            {
                this.hoverText = this.translate("companion.inventory.item_stack", new
                {
                    item = items[index].DisplayName,
                    count = items[index].Stack
                });
            }
            return;
        }

        foreach ((Rectangle bounds, int index) in this.playerInventorySlotsBounds)
        {
            if (!bounds.Contains(x, y))
                continue;
            CompanionInventoryWorkspace workspace = this.GetInventoryWorkspaceForPanel(selected);
            Item? item = index >= 0 && index < workspace.PlayerItems.Count
                ? workspace.PlayerItems[index]
                : null;
            if (item is not null)
                this.hoverText = $"{item.DisplayName} ×{item.Stack}";
            return;
        }

        foreach ((Rectangle bounds, int index) in this.chestInventorySlotsBounds)
        {
            if (!bounds.Contains(x, y))
                continue;
            CompanionInventoryWorkspace workspace = this.GetInventoryWorkspaceForPanel(selected);
            Item? item = index >= 0 && index < workspace.ChestItems.Count
                ? workspace.ChestItems[index]
                : null;
            if (item is not null)
                this.hoverText = $"{item.DisplayName} ×{item.Stack}";
            return;
        }

        foreach ((Rectangle bounds, CompanionInventoryFilter filter) in this.inventoryFilterButtons)
        {
            if (bounds.Contains(x, y))
            {
                this.hoverText = this.translate($"{GetInventoryFilterTranslationKey(filter)}_hint", null);
                return;
            }
        }

        foreach ((Rectangle bounds, string skillId) in this.skillButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skillId);
            if (skill is not null)
            {
                this.inspectedSkillId = skill.Id;
                if (!this.skillDetailsEmbedded)
                    this.hoverText = this.BuildSkillHoverText(selected, skill);
            }
            return;
        }

        foreach ((Rectangle bounds, CompanionDirective directive) in this.directiveButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            this.hoverText = this.getDirectivePreviewText(selected, directive);
            return;
        }

        if (this.currentTab == PanelTab.Routine)
            this.HandleRoutineHover(selected, x, y);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.Reflow();
    }

    public override void draw(SpriteBatch b)
    {
        this.ResetFrameGeometry();
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.72f);
        this.DrawPanel(b, new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height), WindowTextureTint);

        bool shortViewport = this.height < CompactViewportHeight;
        bool brandedHeader = !shortViewport
            && this.height >= BrandedHeaderMinHeight
            && this.width >= 520;
        int effectiveTitleBarHeight = shortViewport
            ? 40
            : brandedHeader ? BrandedTitleBarHeight : TitleBarHeight;
        SpriteFont titleFont = brandedHeader
            ? Game1.dialogueFont
            : shortViewport ? Game1.tinyFont : Game1.smallFont;
        float preferredTitleScale = brandedHeader
            ? PanelTitleTextScale
            : shortViewport ? PanelHeadingTextScale : PanelTitleTextScale;
        string title = this.translate("companion.panel.title", null);
        int titleWidth = Math.Max(1, this.width - 120);
        float titleScale = GetTextScaleForBox(
            title,
            titleFont,
            preferredTitleScale,
            titleWidth,
            brandedHeader ? 42 : effectiveTitleBarHeight - 16,
            minimumScale: brandedHeader ? 0.54f : PanelCompactTextScale);
        string fittedTitle = FitText(title, titleFont, titleWidth, titleScale);
        Vector2 titleSize = MeasureScaledText(fittedTitle, titleFont, titleScale);
        DrawPanelText(
            b,
            fittedTitle,
            titleFont,
            new Vector2(
                this.xPositionOnScreen + (this.width - titleSize.X) / 2f,
                this.yPositionOnScreen + (brandedHeader ? 12 : shortViewport ? 9 : 13)),
            TextColor,
            titleScale,
            shadow: true);

        if (brandedHeader)
        {
            string subtitle = this.translate("companion.panel.subtitle", null);
            float subtitleScale = GetTextScaleForBox(
                subtitle,
                Game1.smallFont,
                PanelTextScale,
                Math.Max(1, this.width - 120),
                24,
                minimumScale: PanelMetaTextScale);
            string fittedSubtitle = FitText(subtitle, Game1.smallFont, Math.Max(1, this.width - 120), subtitleScale);
            Vector2 subtitleSize = MeasureScaledText(fittedSubtitle, Game1.smallFont, subtitleScale);
            DrawPanelText(
                b,
                fittedSubtitle,
                Game1.smallFont,
                new Vector2(
                    this.xPositionOnScreen + (this.width - subtitleSize.X) / 2f,
                    this.yPositionOnScreen + 52),
                MutedTextColor,
                subtitleScale,
                shadow: true);
            this.DrawHeaderDivider(b, this.yPositionOnScreen + 80);
        }
        else if (!shortViewport)
        {
            this.DrawHeaderDivider(b, this.yPositionOnScreen + effectiveTitleBarHeight - 7);
        }

        this.DrawButton(b, this.closeButton, "X", false, danger: true);

        List<SquadMemberState> members = this.getMembers().ToList();
        if (members.Count == 0)
        {
            Rectangle empty = new(
                this.xPositionOnScreen + 18,
                this.yPositionOnScreen + effectiveTitleBarHeight + 6,
                Math.Max(1, this.width - 36),
                Math.Max(1, this.height - effectiveTitleBarHeight - 24));
            this.DrawPanel(b, empty, SurfaceTextureTint);
            DrawCenteredPanelText(
                b,
                this.translate("companion.panel.empty", null),
                Game1.smallFont,
                empty,
                MutedTextColor,
                PanelTextScale,
                30,
                20,
                minimumScale: PanelMetaTextScale);
            this.RebuildFocusTargets();
            this.DrawFocus(b);
            this.drawMouse(b);
            return;
        }

        this.EnsureSelection(members);
        this.wideLayout = this.width >= 780 && this.height >= 440;
        int contentTop = this.yPositionOnScreen + effectiveTitleBarHeight + (shortViewport ? 3 : 6);
        int contentBottom = this.yPositionOnScreen + this.height - (shortViewport ? 6 : 16);

        Rectangle detailArea;
        if (this.wideLayout)
        {
            int rosterWidth = Math.Min(WideRosterWidth, Math.Max(210, this.width / 4));
            Rectangle roster = new(
                this.xPositionOnScreen + 16,
                contentTop,
                rosterWidth,
                Math.Max(1, contentBottom - contentTop));
            detailArea = new(
                roster.Right + ContentGap,
                contentTop,
                Math.Max(1, this.xPositionOnScreen + this.width - 16 - roster.Right - ContentGap),
                roster.Height);
            this.DrawPanel(b, roster, SurfaceTextureTint);
            this.DrawWideMemberList(b, members, roster);
        }
        else
        {
            int selectorHeight = shortViewport ? 34 : this.height >= 330 ? 64 : 52;
            Rectangle selector = new(
                this.xPositionOnScreen + 14,
                contentTop,
                Math.Max(1, this.width - 28),
                selectorHeight);
            this.DrawPanel(b, selector, SurfaceTextureTint);
            this.DrawNarrowMemberSelector(b, members, selector);
            int selectorGap = shortViewport ? 4 : 8;
            detailArea = new(
                selector.X,
                selector.Bottom + selectorGap,
                selector.Width,
                Math.Max(1, contentBottom - selector.Bottom - selectorGap));
        }

        this.DrawPanel(b, detailArea, SurfaceTextureTint);
        SquadMemberState? selected = this.GetSelectedMember(members);
        if (selected is not null)
            this.DrawSelectedMember(b, selected, detailArea);

        this.RebuildFocusTargets();
        if (this.currentTab == PanelTab.Skills
            && !this.skillDetailsEmbedded
            && string.IsNullOrWhiteSpace(this.hoverText)
            && selected is not null
            && this.focusedControlIndex >= 0
            && this.focusedControlIndex < this.focusTargets.Count)
        {
            Rectangle focusedBounds = this.focusTargets[this.focusedControlIndex];
            string? focusedSkillId = null;
            foreach ((Rectangle bounds, string skillId) in this.skillButtons)
            {
                if (bounds != focusedBounds)
                    continue;
                focusedSkillId = skillId;
                break;
            }
            CompanionSkillDefinition? focusedSkill = focusedSkillId is null
                ? null
                : CompanionProgression.Skills.FirstOrDefault(skill => string.Equals(skill.Id, focusedSkillId, StringComparison.OrdinalIgnoreCase));
            if (focusedSkill is not null)
                this.hoverText = this.BuildSkillHoverText(selected, focusedSkill);
        }
        this.DrawFocus(b);
        if (!string.IsNullOrWhiteSpace(this.hoverText))
        {
            if (this.currentTab == PanelTab.Skills && !this.skillDetailsEmbedded)
                this.DrawCompactSkillHover(b, this.hoverText);
            else
                drawHoverText(b, this.hoverText, Game1.smallFont);
        }
        if (this.inventoryDrag is InventoryDragState drag)
        {
            float dragScale = 0.7f;
            Vector2 dragPosition = new(
                Game1.getMouseX() - 22,
                Game1.getMouseY() - 22);
            if (drag.UsesFocus
                && this.focusedControlIndex >= 0
                && this.focusedControlIndex < this.focusTargets.Count)
            {
                Point center = this.focusTargets[this.focusedControlIndex].Center;
                dragPosition = new Vector2(center.X - 22, center.Y - 22);
            }
            drag.Item.drawInMenu(
                b,
                dragPosition,
                dragScale,
                0.75f,
                0.98f,
                StackDrawType.Draw,
                Color.White,
                drawShadow: true);
        }
        this.drawMouse(b);
    }

    private IReadOnlyList<Item> GetCachedInventoryItems(SquadMemberState member)
    {
        int fingerprint = GetInventoryFingerprint(member);
        if (this.inventoryDisplayCache.TryGetValue(member.NpcName, out InventoryDisplayCacheEntry cached)
            && cached.Fingerprint == fingerprint)
        {
            return cached.Items;
        }

        IReadOnlyList<Item> items = this.getInventoryItems(member);
        this.inventoryDisplayCache[member.NpcName] = new InventoryDisplayCacheEntry(fingerprint, items);
        return items;
    }

    private static int GetInventoryFingerprint(SquadMemberState member)
    {
        HashCode fingerprint = new();
        fingerprint.Add(member.Inventory.Count);
        foreach (SavedItemStack stack in member.Inventory)
        {
            fingerprint.Add(stack.QualifiedItemId, StringComparer.Ordinal);
            fingerprint.Add(stack.Stack);
            fingerprint.Add(stack.Quality);
            fingerprint.Add(stack.PreservedParentItemId, StringComparer.Ordinal);
            fingerprint.Add(stack.HasColor);
            fingerprint.Add(stack.ColorR);
            fingerprint.Add(stack.ColorG);
            fingerprint.Add(stack.ColorB);
            fingerprint.Add(stack.ColorA);

            Dictionary<string, string>? modData = stack.ModData;
            fingerprint.Add(modData?.Count ?? 0);
            if (modData is null)
                continue;

            foreach ((string key, string value) in modData)
            {
                fingerprint.Add(key, StringComparer.Ordinal);
                fingerprint.Add(value, StringComparer.Ordinal);
            }
        }

        return fingerprint.ToHashCode();
    }

    private static string GetEquipmentSlotTranslationKey(CompanionEquipmentSlot slot)
    {
        return slot switch
        {
            CompanionEquipmentSlot.Axe => "companion.equipment.slot.axe",
            CompanionEquipmentSlot.Pickaxe => "companion.equipment.slot.pickaxe",
            CompanionEquipmentSlot.WateringCan => "companion.equipment.slot.watering_can",
            CompanionEquipmentSlot.FishingRod => "companion.equipment.slot.fishing_rod",
            _ => "companion.equipment.slot.unknown"
        };
    }

    private void Reflow()
    {
        int availableWidth = Math.Max(1, Game1.uiViewport.Width - WindowMargin * 2);
        int availableHeight = Math.Max(1, Game1.uiViewport.Height - WindowMargin * 2);
        this.width = Math.Min(WindowMaxWidth, availableWidth);
        this.height = Math.Min(WindowMaxHeight, availableHeight);
        this.xPositionOnScreen = Math.Max(0, (Game1.uiViewport.Width - this.width) / 2);
        this.yPositionOnScreen = Math.Max(0, (Game1.uiViewport.Height - this.height) / 2);
        this.closeButton = new Rectangle(
            this.xPositionOnScreen + Math.Max(0, this.width - 46),
            this.yPositionOnScreen + 9,
            32,
            32);
    }

    private void ResetFrameGeometry()
    {
        this.memberRows.Clear();
        this.tabButtons.Clear();
        this.directiveButtons.Clear();
        this.skillButtons.Clear();
        this.equipmentSlotsBounds.Clear();
        this.inventorySlotsBounds.Clear();
        this.playerInventorySlotsBounds.Clear();
        this.chestInventorySlotsBounds.Clear();
        this.inventoryPaneBounds.Clear();
        this.inventoryPanePages.Clear();
        this.inventoryFilterButtons.Clear();
        this.focusTargets.Clear();
        this.memberListArea = new Rectangle();
        this.previousMemberButton = new Rectangle();
        this.nextMemberButton = new Rectangle();
        this.withdrawAllButton = new Rectangle();
        this.hatSlot = new Rectangle();
        this.waitButton = new Rectangle();
        this.recallButton = new Rectangle();
        this.dismissButton = new Rectangle();
        this.skillDetailsArea = new Rectangle();
        this.skillDetailsEmbedded = false;
        this.ResetRoutineGeometry();
        this.ResetTeamDashboardGeometry();
        this.closeButton = new Rectangle(
            this.xPositionOnScreen + Math.Max(0, this.width - 46),
            this.yPositionOnScreen + 9,
            32,
            32);
    }

    private sealed record InventoryDragState(
        string NpcName,
        CompanionInventoryEndpoint Source,
        int SourceIndex,
        Item Item,
        string ExpectedItemToken,
        string ChestId,
        string ChestLocationName,
        int ChestTileX,
        int ChestTileY,
        Rectangle SourceBounds,
        bool UsesFocus);

    private sealed record InventoryPanePageState(
        Rectangle Bounds,
        CompanionInventoryEndpoint Endpoint,
        int Columns,
        int VisibleCapacity,
        int TotalSlots,
        int Offset);

    private void DrawWideMemberList(SpriteBatch b, List<SquadMemberState> members, Rectangle area)
    {
        Rectangle heading = new(area.X + 14, area.Y + 7, Math.Max(1, area.Width - 28), 23);
        DrawCenteredPanelText(
            b,
            this.translate("companion.panel.member_list", null),
            Game1.tinyFont,
            heading,
            TextColor,
            PanelHeadingTextScale,
            6,
            3,
            minimumScale: PanelMetaTextScale);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(area.X + 18, area.Y + 32, Math.Max(1, area.Width - 36), 2),
            AccentGold * 0.72f);

        Rectangle rowsArea = new(area.X + 8, area.Y + 38, Math.Max(1, area.Width - 16), Math.Max(1, area.Height - 47));
        this.memberListArea = rowsArea;
        int stride = MemberRowHeight + MemberRowGap;
        int visibleCount = this.GetVisibleMemberRowCount();
        this.memberListMaxScroll = Math.Max(0, (members.Count - visibleCount) * stride);
        this.memberListScrollOffset = Math.Clamp(this.memberListScrollOffset, 0, this.memberListMaxScroll);
        this.memberListScrollOffset -= this.memberListScrollOffset % stride;
        int startIndex = this.memberListScrollOffset / stride;
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());

        for (int visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            int memberIndex = startIndex + visibleIndex;
            if (memberIndex >= members.Count)
                break;
            SquadMemberState member = members[memberIndex];
            Rectangle row = new(
                rowsArea.X + MemberScrollPadding,
                rowsArea.Y + MemberScrollPadding + visibleIndex * stride,
                Math.Max(1, rowsArea.Width - MemberScrollPadding * 2 - (this.memberListMaxScroll > 0 ? 8 : 0)),
                MemberRowHeight);
            if (row.Bottom > rowsArea.Bottom - MemberScrollPadding + 1)
                break;

            bool selected = string.Equals(member.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase);
            Color statusAccent = this.GetMemberStatusColor(member);
            this.DrawMenuCard(
                b,
                row,
                selected ? SelectedRowColor : row.Contains(mouse) ? RowHoverColor : RowColor,
                statusAccent);
            Rectangle portrait = new(row.X + 16, row.Y + 9, 52, 52);
            this.DrawFlatPanel(b, portrait, Color.White, selected ? AccentGreen : SurfaceBorder, 1);
            this.DrawPortrait(b, this.getNpc(member.NpcName), portrait);

            int textX = portrait.Right + 9;
            int textWidth = Math.Max(1, row.Right - textX - 8);
            int nameLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale);
            string levelLabel = this.translate("companion.panel.level", new { level = member.Level });
            float levelScale = GetTextScaleForBox(
                levelLabel,
                Game1.tinyFont,
                PanelCompactNumericTextScale,
                Math.Max(1, textWidth / 2),
                nameLineHeight,
                minimumScale: 0.54f);
            string fittedLevelLabel = FitText(levelLabel, Game1.tinyFont, Math.Max(1, textWidth / 2), levelScale);
            Vector2 levelSize = MeasureScaledText(fittedLevelLabel, Game1.tinyFont, levelScale);
            int nameWidth = Math.Max(1, textWidth - (int)Math.Ceiling(levelSize.X) - 6);
            DrawPanelText(
                b,
                FitText(member.DisplayName, Game1.tinyFont, nameWidth, PanelTextScale),
                Game1.tinyFont,
                new Vector2(textX, row.Y + 12),
                TextColor,
                PanelTextScale);
            DrawPanelText(
                b,
                fittedLevelLabel,
                Game1.tinyFont,
                new Vector2(row.Right - 8 - levelSize.X, row.Y + 12 + Math.Max(0f, (nameLineHeight - levelSize.Y) / 2f)),
                TextColor,
                levelScale);
            string rosterStatus = this.getStatusText(member);
            float rosterStatusScale = GetTextScaleForBox(
                rosterStatus,
                Game1.tinyFont,
                PanelMetaTextScale,
                textWidth,
                GetScaledLineHeight(Game1.tinyFont, PanelMetaTextScale),
                minimumScale: 0.52f);
            DrawPanelText(
                b,
                FitText(rosterStatus, Game1.tinyFont, textWidth, rosterStatusScale),
                Game1.tinyFont,
                new Vector2(textX, row.Y + 12 + nameLineHeight + 6),
                MutedTextColor,
                rosterStatusScale);
            this.memberRows.Add((row, member.NpcName));
        }

        this.DrawMemberListScrollbar(b, rowsArea);
    }

    private void DrawNarrowMemberSelector(SpriteBatch b, List<SquadMemberState> members, Rectangle area)
    {
        int buttonSize = Math.Clamp(area.Height - 16, 26, 38);
        this.previousMemberButton = new Rectangle(area.X + 8, area.Center.Y - buttonSize / 2, buttonSize, buttonSize);
        this.nextMemberButton = new Rectangle(area.Right - buttonSize - 8, area.Center.Y - buttonSize / 2, buttonSize, buttonSize);
        this.DrawButton(b, this.previousMemberButton, "<", false, danger: false);
        this.DrawButton(b, this.nextMemberButton, ">", false, danger: false);

        SquadMemberState? selected = this.GetSelectedMember(members);
        if (selected is null)
            return;

        int selectedIndex = members.FindIndex(p => string.Equals(p.NpcName, selected.NpcName, StringComparison.OrdinalIgnoreCase));
        int portraitSize = Math.Clamp(area.Height - 12, 28, 48);
        Rectangle portrait = new(this.previousMemberButton.Right + 8, area.Center.Y - portraitSize / 2, portraitSize, portraitSize);
        this.DrawFlatPanel(b, portrait, Color.White, this.GetMemberStatusColor(selected), 1);
        this.DrawPortrait(b, this.getNpc(selected.NpcName), portrait);

        int textX = portrait.Right + 8;
        int textRight = this.nextMemberButton.X - 7;
        int textWidth = Math.Max(1, textRight - textX);
        string status = this.translate("companion.panel.member_position", new
        {
            current = Math.Max(1, selectedIndex + 1),
            total = members.Count,
            status = this.getStatusText(selected)
        });
        if (area.Height < 44)
        {
            string compactLabel = $"{selected.DisplayName} · {status}";
            float scale = GetTextScaleForHeight(Game1.tinyFont, PanelTextScale, area.Height - 10);
            Vector2 size = MeasureScaledText(FitText(compactLabel, Game1.tinyFont, textWidth, scale), Game1.tinyFont, scale);
            DrawPanelText(
                b,
                FitText(compactLabel, Game1.tinyFont, textWidth, scale),
                Game1.tinyFont,
                new Vector2(textX, area.Center.Y - size.Y / 2f),
                TextColor,
                scale);
            return;
        }

        int selectorNameHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale);
        DrawPanelText(
            b,
            FitText(selected.DisplayName, Game1.tinyFont, textWidth, PanelTextScale),
            Game1.tinyFont,
            new Vector2(textX, area.Y + 9),
            TextColor,
            PanelTextScale);
        DrawPanelText(
            b,
            FitText(status, Game1.tinyFont, textWidth, PanelCompactNumericTextScale),
            Game1.tinyFont,
            new Vector2(textX, area.Y + 9 + selectorNameHeight + 4),
            MutedTextColor,
            PanelCompactNumericTextScale);
    }

    private void DrawSelectedMember(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        bool compactMemberLayout = this.height < ExpandedMemberLayoutMinHeight;
        int inset = compactMemberLayout ? 6 : area.Width >= 420 ? 12 : 8;
        int usableWidth = Math.Max(1, area.Width - inset * 2);
        int tabHeight;
        int tabY;
        if (compactMemberLayout)
        {
            // The compact selector already identifies the member. Omitting the
            // duplicate portrait header preserves usable tab content at high UI
            // scale and in four-player split-screen viewports.
            tabHeight = 24;
            tabY = area.Y + 5;
        }
        else
        {
            int headerHeight = area.Height >= 390 ? 78 : area.Height >= 250 ? 58 : 40;
            Rectangle header = new(area.X + inset, area.Y + 8, usableWidth, Math.Max(1, Math.Min(headerHeight, area.Height - 12)));
            this.DrawMemberHeader(b, member, header);
            tabHeight = area.Height >= 460 ? 40 : area.Height >= 220 ? 34 : 27;
            tabY = header.Bottom + 6;
        }

        int tabGap = area.Width >= 700 ? 8 : area.Width >= 420 ? 5 : 2;
        PanelTab[] tabs =
        {
            PanelTab.Overview,
            PanelTab.Work,
            PanelTab.Skills,
            PanelTab.Inventory,
            PanelTab.Routine,
            PanelTab.Team
        };
        int tabWidth = Math.Max(1, (usableWidth - tabGap * (tabs.Length - 1)) / tabs.Length);
        for (int i = 0; i < tabs.Length; i++)
        {
            int x = area.X + inset + i * (tabWidth + tabGap);
            int width = i == tabs.Length - 1 ? area.Right - inset - x : tabWidth;
            Rectangle button = new(x, tabY, Math.Max(1, width), tabHeight);
            this.tabButtons.Add((button, tabs[i]));
            string? badgeText = tabs[i] switch
            {
                PanelTab.Skills when this.isProgressionEnabled() && member.UnspentSkillPoints > 0 => member.UnspentSkillPoints.ToString(),
                PanelTab.Inventory when member.Inventory.Count > 0 => member.Inventory.Count.ToString(),
                _ => null
            };
            Color? badgeColor = tabs[i] == PanelTab.Skills ? AccentGold : AccentGreen;
            this.DrawTabButton(b, button, this.GetTabLabel(tabs[i], compact: button.Width < 125), this.currentTab == tabs[i], badgeText, badgeColor);
        }

        Rectangle body = new(
            area.X + inset,
            tabY + tabHeight + (compactMemberLayout ? 4 : 6),
            usableWidth,
            Math.Max(1, area.Bottom - inset - (tabY + tabHeight + (compactMemberLayout ? 4 : 6))));
        if (body.Height < 12)
            return;

        switch (this.currentTab)
        {
            case PanelTab.Team:
                this.DrawTeamDashboard(b, body);
                break;
            case PanelTab.Overview:
                this.DrawOverview(b, member, body);
                break;
            case PanelTab.Work:
                this.DrawWork(b, member, body);
                break;
            case PanelTab.Skills:
                this.DrawSkills(b, member, body);
                break;
            case PanelTab.Inventory:
                this.DrawInventory(b, member, body);
                break;
            case PanelTab.Routine:
                this.DrawRoutine(b, member, body);
                break;
        }
    }

    private void DrawMemberHeader(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        Color statusAccent = this.GetMemberStatusColor(member);
        this.DrawMenuCard(b, area, HeaderCardColor, statusAccent);

        int portraitSize = Math.Clamp(area.Height - 14, 24, 62);
        Rectangle portrait = new(area.X + 16, area.Center.Y - portraitSize / 2, portraitSize, portraitSize);
        this.DrawFlatPanel(b, portrait, Color.White, SurfaceBorder, 1);
        this.DrawPortrait(b, this.getNpc(member.NpcName), portrait);

        int textX = portrait.Right + 10;
        int textWidth = Math.Max(1, area.Right - textX - 12);
        float nameScale = area.Height >= 70 ? PanelHeadingTextScale : PanelTextScale;
        float statusScale = area.Height >= 70 ? PanelTextScale : PanelMetaTextScale;
        int textTop = area.Y + 8;
        int textBottom = area.Height >= 54 ? area.Bottom - 15 : area.Bottom - 2;
        int availableTextHeight = Math.Max(1, textBottom - textTop);
        float desiredTextHeight = Game1.smallFont.LineSpacing * nameScale
            + 3f
            + Game1.tinyFont.LineSpacing * statusScale;
        if (desiredTextHeight > availableTextHeight)
        {
            float reduction = availableTextHeight / desiredTextHeight;
            nameScale *= reduction;
            statusScale *= reduction;
        }
        int nameLineHeight = GetScaledLineHeight(Game1.smallFont, nameScale);
        DrawPanelText(
            b,
            FitText(member.DisplayName, Game1.smallFont, textWidth, nameScale),
            Game1.smallFont,
            new Vector2(textX, textTop),
            TextColor,
            nameScale);
        string status = this.translate("companion.panel.header_status", new
        {
            level = member.Level,
            status = this.getStatusText(member)
        });
        DrawPanelText(
            b,
            FitText(status, Game1.tinyFont, textWidth, statusScale),
            Game1.tinyFont,
            new Vector2(textX, textTop + nameLineHeight + 3),
            MutedTextColor,
            statusScale);

        if (area.Height >= 54)
        {
            Rectangle xp = new(textX, area.Bottom - 12, Math.Max(1, textWidth), 10);
            this.DrawXpBar(b, xp, member);
        }

    }
}
