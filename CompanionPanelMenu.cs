using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

/// <summary>A responsive, controller-friendly home for companion management.</summary>
internal sealed partial class CompanionPanelMenu : IClickableMenu
{
    private const int WindowMargin = 16;
    private const int WindowMaxWidth = 1180;
    private const int WindowMaxHeight = 780;
    private const int TitleBarHeight = 50;
    private const int ContentGap = 10;
    private const int WideRosterWidth = 242;
    private const int MemberRowHeight = 70;
    private const int MemberRowGap = 6;
    private const int MemberScrollPadding = 5;
    private const int PortraitRetryTicks = 600;

    private static readonly Color WindowColor = new(239, 216, 180);
    private static readonly Color WindowBorder = new(76, 49, 31);
    private static readonly Color SurfaceColor = new(252, 240, 216);
    private static readonly Color SurfaceBorder = new(117, 82, 53);
    private static readonly Color RowColor = new(247, 235, 210);
    private static readonly Color RowHoverColor = new(255, 247, 226);
    private static readonly Color SelectedRowColor = new(216, 235, 207);
    private static readonly Color AccentGreen = new(48, 112, 81);
    private static readonly Color AccentBlue = new(64, 121, 177);
    private static readonly Color AccentGold = new(207, 153, 62);
    private static readonly Color DangerColor = new(170, 76, 62);
    private static readonly Color ButtonIdle = new(233, 218, 188);
    private static readonly Color ButtonActive = new(199, 228, 198);
    private static readonly Color TextColor = new(69, 45, 29);
    private static readonly Color MutedTextColor = new(101, 76, 55);

    private readonly Func<IEnumerable<SquadMemberState>> getMembers;
    private readonly Func<string, NPC?> getNpc;
    private readonly Func<string, object?, string> translate;
    private readonly Func<SquadMemberState, string> getStatusText;
    private readonly Func<IReadOnlyList<string>> getSummaryLines;
    private readonly Func<SquadMemberState, IReadOnlyList<string>> getDetailLines;
    private readonly Func<SquadMemberState, CompanionPanelMapInfo> getMapInfo;
    private readonly Func<SquadMemberState, CompanionDirective, string> getDirectivePreviewText;
    private readonly Func<SquadMemberState, List<Item>> getInventoryItems;
    private readonly Func<SquadMemberState, int, bool> withdrawInventoryItem;
    private readonly Func<SquadMemberState, bool> withdrawAllInventoryItems;
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
    private readonly List<(Rectangle Bounds, int Index)> inventorySlotsBounds = new();
    private readonly List<Rectangle> focusTargets = new();
    private readonly Dictionary<string, PortraitCacheEntry> portraitCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InventoryDisplayCacheEntry> inventoryDisplayCache = new(StringComparer.OrdinalIgnoreCase);

    private Rectangle memberListArea;
    private Rectangle previousMemberButton;
    private Rectangle nextMemberButton;
    private Rectangle withdrawAllButton;
    private Rectangle waitButton;
    private Rectangle recallButton;
    private Rectangle dismissButton;
    private Rectangle closeButton;
    private int memberListScrollOffset;
    private int memberListMaxScroll;
    private int focusedControlIndex = -1;
    private string? selectedNpcName;
    private PanelTab currentTab = PanelTab.Overview;
    private string hoverText = "";
    private bool wideLayout;

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
        Func<SquadMemberState, int, bool> withdrawInventoryItem,
        Func<SquadMemberState, bool> withdrawAllInventoryItems,
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
        this.withdrawInventoryItem = withdrawInventoryItem;
        this.withdrawAllInventoryItems = withdrawAllInventoryItems;
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
            this.selectedNpcName = npcName;
            Game1.playSound("smallSelect");
            return;
        }

        foreach ((Rectangle bounds, PanelTab tab) in this.tabButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            if (this.currentTab != tab)
            {
                this.currentTab = tab;
                this.focusedControlIndex = -1;
                Game1.playSound("smallSelect");
            }
            return;
        }

        SquadMemberState? selected = this.GetSelectedMember();
        if (selected is null)
            return;

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
            if (this.withdrawAllButton.Contains(x, y))
            {
                Game1.playSound(this.withdrawAllInventoryItems(selected) ? "coin" : "cancel");
                return;
            }

            foreach ((Rectangle bounds, int index) in this.inventorySlotsBounds)
            {
                if (!bounds.Contains(x, y))
                    continue;
                Game1.playSound(this.withdrawInventoryItem(selected, index) ? "coin" : "cancel");
                return;
            }
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

                if (!this.isProgressionEnabled())
                {
                    Game1.playSound("cancel");
                    return;
                }

                Game1.playSound(this.unlockSkill(selected, skillId) ? "newArtifact" : "cancel");
                return;
            }
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key is Keys.Escape or Keys.E)
        {
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
                if (this.SelectRelativeMember(-1))
                    Game1.playSound("shiny4");
                return;
            case Keys.Down:
            case Keys.S:
                if (this.SelectRelativeMember(1))
                    Game1.playSound("shiny4");
                return;
            case Keys.PageUp:
                if (this.SelectRelativeMember(-this.GetVisibleMemberRowCount()))
                    Game1.playSound("shiny4");
                return;
            case Keys.PageDown:
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
                this.CloseMenu();
                return;
            case Buttons.LeftShoulder:
                this.CycleTab(-1);
                return;
            case Buttons.RightShoulder:
                this.CycleTab(1);
                return;
            case Buttons.DPadUp:
                if (this.SelectRelativeMember(-1))
                    Game1.playSound("shiny4");
                return;
            case Buttons.DPadDown:
                if (this.SelectRelativeMember(1))
                    Game1.playSound("shiny4");
                return;
            case Buttons.DPadLeft:
                this.MoveFocus(-1);
                return;
            case Buttons.DPadRight:
                this.MoveFocus(1);
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

        foreach ((Rectangle bounds, string skillId) in this.skillButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skillId);
            if (skill is not null)
                this.hoverText = this.BuildSkillHoverText(selected, skill);
            return;
        }

        foreach ((Rectangle bounds, CompanionDirective directive) in this.directiveButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            this.hoverText = this.getDirectivePreviewText(selected, directive);
            return;
        }
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
        this.DrawPanel(b, new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height), WindowColor, WindowBorder);

        string title = this.translate("companion.panel.title", null);
        Utility.drawTextWithShadow(
            b,
            FitText(title, Game1.smallFont, Math.Max(1, this.width - 120)),
            Game1.smallFont,
            new Vector2(this.xPositionOnScreen + 20, this.yPositionOnScreen + 14),
            TextColor);
        this.DrawButton(b, this.closeButton, "X", false, danger: false);

        List<SquadMemberState> members = this.getMembers().ToList();
        if (members.Count == 0)
        {
            Rectangle empty = new(
                this.xPositionOnScreen + 18,
                this.yPositionOnScreen + TitleBarHeight + 6,
                Math.Max(1, this.width - 36),
                Math.Max(1, this.height - TitleBarHeight - 24));
            this.DrawPanel(b, empty, SurfaceColor, SurfaceBorder);
            Utility.drawTextWithShadow(
                b,
                FitText(this.translate("companion.panel.empty", null), Game1.smallFont, empty.Width - 30),
                Game1.smallFont,
                new Vector2(empty.X + 16, empty.Y + 20),
                MutedTextColor);
            this.RebuildFocusTargets();
            this.DrawFocus(b);
            this.drawMouse(b);
            return;
        }

        this.EnsureSelection(members);
        this.wideLayout = this.width >= 780 && this.height >= 440;
        bool shortViewport = this.height < 300;
        int effectiveTitleBarHeight = shortViewport ? 40 : TitleBarHeight;
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
            this.DrawPanel(b, roster, SurfaceColor, SurfaceBorder);
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
            this.DrawPanel(b, selector, SurfaceColor, SurfaceBorder);
            this.DrawNarrowMemberSelector(b, members, selector);
            int selectorGap = shortViewport ? 4 : 8;
            detailArea = new(
                selector.X,
                selector.Bottom + selectorGap,
                selector.Width,
                Math.Max(1, contentBottom - selector.Bottom - selectorGap));
        }

        this.DrawPanel(b, detailArea, SurfaceColor, SurfaceBorder);
        SquadMemberState? selected = this.GetSelectedMember(members);
        if (selected is not null)
            this.DrawSelectedMember(b, selected, detailArea);

        this.RebuildFocusTargets();
        this.DrawFocus(b);
        if (!string.IsNullOrWhiteSpace(this.hoverText))
            drawHoverText(b, this.hoverText, Game1.smallFont);
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
        this.inventorySlotsBounds.Clear();
        this.focusTargets.Clear();
        this.memberListArea = new Rectangle();
        this.previousMemberButton = new Rectangle();
        this.nextMemberButton = new Rectangle();
        this.withdrawAllButton = new Rectangle();
        this.waitButton = new Rectangle();
        this.recallButton = new Rectangle();
        this.dismissButton = new Rectangle();
        this.closeButton = new Rectangle(
            this.xPositionOnScreen + Math.Max(0, this.width - 46),
            this.yPositionOnScreen + 9,
            32,
            32);
    }

    private void DrawWideMemberList(SpriteBatch b, List<SquadMemberState> members, Rectangle area)
    {
        Utility.drawTextWithShadow(
            b,
            FitText(this.translate("companion.panel.member_list", null), Game1.tinyFont, area.Width - 28),
            Game1.tinyFont,
            new Vector2(area.X + 14, area.Y + 12),
            TextColor);

        Rectangle rowsArea = new(area.X + 8, area.Y + 34, Math.Max(1, area.Width - 16), Math.Max(1, area.Height - 43));
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
            this.DrawFlatPanel(b, row, selected ? SelectedRowColor : row.Contains(mouse) ? RowHoverColor : RowColor, selected ? AccentGreen : SurfaceBorder, 2);
            b.Draw(Game1.staminaRect, new Rectangle(row.X + 2, row.Y + 8, 4, row.Height - 16), this.GetMemberStatusColor(member));
            Rectangle portrait = new(row.X + 10, row.Y + 9, 52, 52);
            this.DrawFlatPanel(b, portrait, Color.White, selected ? AccentGreen : SurfaceBorder, 1);
            this.DrawPortrait(b, this.getNpc(member.NpcName), portrait);

            int textX = portrait.Right + 9;
            int textWidth = Math.Max(1, row.Right - textX - 8);
            Utility.drawTextWithShadow(b, FitText(member.DisplayName, Game1.tinyFont, textWidth), Game1.tinyFont, new Vector2(textX, row.Y + 10), TextColor);
            Utility.drawTextWithShadow(
                b,
                FitText(this.getStatusText(member), Game1.tinyFont, textWidth),
                Game1.tinyFont,
                new Vector2(textX, row.Y + 30),
                MutedTextColor);
            Utility.drawTextWithShadow(
                b,
                FitText(this.translate("companion.panel.level", new { level = member.Level }), Game1.tinyFont, textWidth),
                Game1.tinyFont,
                new Vector2(textX, row.Y + 49),
                MutedTextColor);
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
        this.DrawFlatPanel(b, portrait, Color.White, AccentGreen, 1);
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
            Utility.drawTextWithShadow(
                b,
                FitText(compactLabel, Game1.tinyFont, textWidth),
                Game1.tinyFont,
                new Vector2(textX, area.Center.Y - Game1.tinyFont.LineSpacing / 2f),
                TextColor);
            return;
        }

        Utility.drawTextWithShadow(b, FitText(selected.DisplayName, Game1.tinyFont, textWidth), Game1.tinyFont, new Vector2(textX, area.Y + 9), TextColor);
        Utility.drawTextWithShadow(b, FitText(status, Game1.tinyFont, textWidth), Game1.tinyFont, new Vector2(textX, area.Bottom - 25), MutedTextColor);
    }

    private void DrawSelectedMember(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        bool shortViewport = this.height < 300;
        int inset = shortViewport ? 6 : area.Width >= 420 ? 12 : 8;
        int usableWidth = Math.Max(1, area.Width - inset * 2);
        int tabHeight;
        int tabY;
        if (shortViewport)
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
            tabHeight = area.Height >= 220 ? 34 : 27;
            tabY = header.Bottom + 6;
        }

        int tabGap = area.Width >= 420 ? 5 : 2;
        int tabWidth = Math.Max(1, (usableWidth - tabGap * 3) / 4);
        PanelTab[] tabs = { PanelTab.Overview, PanelTab.Work, PanelTab.Skills, PanelTab.Inventory };
        for (int i = 0; i < tabs.Length; i++)
        {
            int x = area.X + inset + i * (tabWidth + tabGap);
            int width = i == tabs.Length - 1 ? area.Right - inset - x : tabWidth;
            Rectangle button = new(x, tabY, Math.Max(1, width), tabHeight);
            this.tabButtons.Add((button, tabs[i]));
            this.DrawTabButton(b, button, this.GetTabLabel(tabs[i]), this.currentTab == tabs[i]);
        }

        Rectangle body = new(
            area.X + inset,
            tabY + tabHeight + (shortViewport ? 4 : 6),
            usableWidth,
            Math.Max(1, area.Bottom - inset - (tabY + tabHeight + (shortViewport ? 4 : 6))));
        if (body.Height < 12)
            return;

        switch (this.currentTab)
        {
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
        }
    }

    private void DrawMemberHeader(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        int portraitSize = Math.Clamp(area.Height - 8, 26, 66);
        Rectangle portrait = new(area.X, area.Center.Y - portraitSize / 2, portraitSize, portraitSize);
        this.DrawFlatPanel(b, portrait, Color.White, SurfaceBorder, 1);
        this.DrawPortrait(b, this.getNpc(member.NpcName), portrait);

        int textX = portrait.Right + 10;
        int badgeWidth = area.Width >= 440 ? 104 : 0;
        int textRight = area.Right - badgeWidth - (badgeWidth > 0 ? 8 : 0);
        int textWidth = Math.Max(1, textRight - textX);
        Utility.drawTextWithShadow(b, FitText(member.DisplayName, Game1.smallFont, textWidth), Game1.smallFont, new Vector2(textX, area.Y + 2), TextColor);
        string status = this.translate("companion.panel.header_status", new
        {
            level = member.Level,
            status = this.getStatusText(member)
        });
        Utility.drawTextWithShadow(b, FitText(status, Game1.tinyFont, textWidth), Game1.tinyFont, new Vector2(textX, area.Y + Math.Min(30, Math.Max(20, area.Height - 28))), MutedTextColor);

        if (area.Height >= 54)
        {
            Rectangle xp = new(textX, area.Bottom - 12, Math.Max(1, textWidth), 10);
            this.DrawXpBar(b, xp, member);
        }

        if (badgeWidth > 0)
        {
            Rectangle badge = new(area.Right - badgeWidth, area.Center.Y - 14, badgeWidth, 28);
            this.DrawBadge(
                b,
                badge,
                this.translate("companion.panel.points_short", new { points = member.UnspentSkillPoints }),
                member.UnspentSkillPoints > 0 ? new Color(241, 220, 142) : ButtonIdle,
                SurfaceBorder);
        }
    }
}
