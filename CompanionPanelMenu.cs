using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

internal sealed class CompanionPanelMenu : IClickableMenu
{
    private const int WindowPadding = 24;
    private const int HeaderHeight = 116;
    private const int LeftPanelWidth = 318;
    private const int MemberRowHeight = 88;
    private const int MemberRowGap = 10;
    private const int MemberScrollPadding = 10;
    private const int MemberScrollStep = 36;
    private const int SkillNodeGap = 8;

    private static readonly Color WindowColor = new(241, 219, 184);
    private static readonly Color WindowBorder = new(80, 50, 31);
    private static readonly Color SectionColor = new(252, 239, 213);
    private static readonly Color SectionBorder = new(116, 82, 54);
    private static readonly Color RowColor = new(248, 238, 216);
    private static readonly Color SelectedRowColor = new(218, 237, 207);
    private static readonly Color AccentColor = new(48, 112, 81);
    private static readonly Color AccentBlue = new(69, 124, 178);
    private static readonly Color SoftBlue = new(211, 231, 242);
    private static readonly Color ButtonActive = new(181, 224, 182);
    private static readonly Color ButtonIdle = new(236, 222, 193);
    private static readonly Color ButtonTextColor = new(74, 49, 32);
    private static readonly Color MutedTextColor = new(88, 66, 48);

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
    private readonly Action<SquadMemberState> toggleWaiting;
    private readonly Action<SquadMemberState> recallMember;
    private readonly Action<SquadMemberState> dismissMember;
    private readonly int inventorySlots;

    private readonly List<(Rectangle Bounds, string NpcName)> memberRows = new();
    private readonly List<(Rectangle Bounds, CompanionDirective Directive)> directiveButtons = new();
    private readonly List<(Rectangle Bounds, string SkillId)> skillButtons = new();
    private readonly List<(Rectangle Bounds, int Index)> inventorySlotsBounds = new();
    private Rectangle memberListArea;
    private int memberListScrollOffset;
    private int memberListMaxScroll;
    private int totalMemberRowsHeight;
    private Rectangle skillsButton;
    private Rectangle inventoryButton;
    private Rectangle withdrawAllButton;
    private Rectangle waitButton;
    private Rectangle recallButton;
    private Rectangle dismissButton;
    private Rectangle closeButton;
    private string? selectedNpcName;
    private bool showingInventory;
    private string hoverText = "";

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
        Action<SquadMemberState> toggleWaiting,
        Action<SquadMemberState> recallMember,
        Action<SquadMemberState> dismissMember,
        int inventorySlots)
        : base(
            Math.Max(32, (Game1.uiViewport.Width - Math.Min(1320, Game1.uiViewport.Width - 64)) / 2),
            Math.Max(32, (Game1.uiViewport.Height - Math.Min(820, Game1.uiViewport.Height - 64)) / 2),
            Math.Min(1320, Game1.uiViewport.Width - 64),
            Math.Min(820, Game1.uiViewport.Height - 64),
            true)
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
        this.toggleWaiting = toggleWaiting;
        this.recallMember = recallMember;
        this.dismissMember = dismissMember;
        this.inventorySlots = inventorySlots;
        this.closeButton = new Rectangle(this.xPositionOnScreen + this.width - 52, this.yPositionOnScreen + 16, 36, 36);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.closeButton.Contains(x, y))
        {
            Game1.activeClickableMenu = null;
            Game1.playSound("bigDeSelect");
            return;
        }

        foreach ((Rectangle bounds, string npcName) in this.memberRows)
        {
            if (!bounds.Contains(x, y))
                continue;

            this.selectedNpcName = npcName;
            this.showingInventory = false;
            Game1.playSound("smallSelect");
            return;
        }

        SquadMemberState? selected = this.GetSelectedMember();
        if (selected is null)
            return;

        if (this.skillsButton.Contains(x, y))
        {
            this.showingInventory = false;
            Game1.playSound("smallSelect");
            return;
        }

        if (this.inventoryButton.Contains(x, y))
        {
            this.showingInventory = true;
            Game1.playSound("smallSelect");
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

        if (this.showingInventory)
        {
            if (this.withdrawAllButton.Contains(x, y))
            {
                if (this.withdrawAllInventoryItems(selected))
                    Game1.playSound("coin");
                else
                    Game1.playSound("cancel");

                return;
            }

            foreach ((Rectangle bounds, int index) in this.inventorySlotsBounds)
            {
                if (!bounds.Contains(x, y))
                    continue;

                if (this.withdrawInventoryItem(selected, index))
                    Game1.playSound("coin");
                else
                    Game1.playSound("cancel");

                return;
            }
        }
        else
        {
            foreach ((Rectangle bounds, CompanionDirective directive) in this.directiveButtons)
            {
                if (!bounds.Contains(x, y))
                    continue;

                this.toggleDirective(selected, directive);
                Game1.playSound("drumkit6");
                return;
            }

            foreach ((Rectangle bounds, string skillId) in this.skillButtons)
            {
                if (!bounds.Contains(x, y))
                    continue;

                if (this.unlockSkill(selected, skillId))
                    Game1.playSound("newArtifact");
                else
                    Game1.playSound("cancel");

                return;
            }
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key is Keys.Escape or Keys.E)
        {
            Game1.activeClickableMenu = null;
            return;
        }

        switch (key)
        {
            case Keys.Tab:
                this.showingInventory = !this.showingInventory;
                Game1.playSound("smallSelect");
                break;

            case Keys.Left:
            case Keys.A:
                if (this.showingInventory)
                {
                    this.showingInventory = false;
                    Game1.playSound("smallSelect");
                }
                break;

            case Keys.Right:
            case Keys.D:
                if (!this.showingInventory)
                {
                    this.showingInventory = true;
                    Game1.playSound("smallSelect");
                }
                break;

            case Keys.Up:
            case Keys.W:
                if (this.SelectRelativeMember(-1))
                    Game1.playSound("shiny4");
                break;

            case Keys.Down:
            case Keys.S:
                if (this.SelectRelativeMember(1))
                    Game1.playSound("shiny4");
                break;

            case Keys.PageUp:
                if (this.SelectRelativeMember(-this.GetVisibleMemberRowCount()))
                    Game1.playSound("shiny4");
                break;

            case Keys.PageDown:
                if (this.SelectRelativeMember(this.GetVisibleMemberRowCount()))
                    Game1.playSound("shiny4");
                break;

            case Keys.Home:
                if (this.SelectMemberByIndex(0))
                    Game1.playSound("shiny4");
                break;

            case Keys.End:
                if (this.SelectMemberByIndex(this.getMembers().Count() - 1))
                    Game1.playSound("shiny4");
                break;
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (this.memberListMaxScroll <= 0 || !this.memberListArea.Contains(Game1.getMouseX(), Game1.getMouseY()))
            return;

        this.memberListScrollOffset = Math.Clamp(
            this.memberListScrollOffset - Math.Sign(direction) * MemberScrollStep,
            0,
            this.memberListMaxScroll);
    }

    public override void performHoverAction(int x, int y)
    {
        this.hoverText = "";

        SquadMemberState? selected = this.GetSelectedMember();
        if (selected is null)
            return;

        foreach ((Rectangle bounds, int index) in this.inventorySlotsBounds)
        {
            if (!bounds.Contains(x, y))
                continue;

            List<Item> items = this.getInventoryItems(selected);
            if (index >= 0 && index < items.Count)
                this.hoverText = $"{items[index].DisplayName} x{items[index].Stack}";

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

    public override void draw(SpriteBatch b)
    {
        this.closeButton = new Rectangle(this.xPositionOnScreen + this.width - 52, this.yPositionOnScreen + 16, 36, 36);
        this.memberRows.Clear();
        this.directiveButtons.Clear();
        this.skillButtons.Clear();
        this.inventorySlotsBounds.Clear();
        this.skillsButton = new Rectangle(0, 0, 0, 0);
        this.inventoryButton = new Rectangle(0, 0, 0, 0);
        this.withdrawAllButton = new Rectangle(0, 0, 0, 0);
        this.waitButton = new Rectangle(0, 0, 0, 0);
        this.recallButton = new Rectangle(0, 0, 0, 0);
        this.dismissButton = new Rectangle(0, 0, 0, 0);

        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.72f);
        this.DrawPanel(b, new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height), WindowColor, WindowBorder);

        string title = this.translate("companion.panel.title", null);
        Vector2 titleSize = Game1.smallFont.MeasureString(title);
        Utility.drawTextWithShadow(b, title, Game1.smallFont, new Vector2(this.xPositionOnScreen + (this.width - titleSize.X) / 2f, this.yPositionOnScreen + 20), new Color(80, 46, 26));

        this.DrawButton(b, this.closeButton, "X", false);

        List<SquadMemberState> members = this.getMembers().ToList();
        this.DrawGlobalSummary(b);
        if (members.Count == 0)
        {
            this.DrawPanel(
                b,
                new Rectangle(
                    this.xPositionOnScreen + WindowPadding,
                    this.yPositionOnScreen + HeaderHeight + 20,
                    this.width - WindowPadding * 2,
                    this.height - HeaderHeight - 40),
                SectionColor,
                SectionBorder);
            Utility.drawTextWithShadow(b, this.translate("companion.panel.empty", null), Game1.smallFont, new Vector2(this.xPositionOnScreen + WindowPadding + 20, this.yPositionOnScreen + HeaderHeight + 52), Color.SaddleBrown);
            this.drawMouse(b);
            return;
        }

        this.EnsureSelection(members);

        int availableWidth = this.width - WindowPadding * 2 - 16;
        int leftPanelWidth = availableWidth < 760
            ? Math.Clamp(availableWidth / 3, 176, 220)
            : Math.Min(LeftPanelWidth, Math.Max(260, (availableWidth - 16) / 4));
        Rectangle listArea = new Rectangle(this.xPositionOnScreen + WindowPadding, this.yPositionOnScreen + HeaderHeight, leftPanelWidth, this.height - HeaderHeight - 32);
        Rectangle detailArea = new Rectangle(listArea.Right + 16, listArea.Y, availableWidth - leftPanelWidth - 16, listArea.Height);
        this.memberListArea = listArea;

        this.DrawPanel(b, listArea, SectionColor, SectionBorder);
        this.DrawMemberList(b, members, listArea);
        this.DrawPanel(b, detailArea, SectionColor, SectionBorder);

        SquadMemberState? selected = this.GetSelectedMember(members);
        if (selected is not null)
            this.DrawSelectedMember(b, selected, detailArea);

        if (!string.IsNullOrWhiteSpace(this.hoverText))
            IClickableMenu.drawHoverText(b, this.hoverText, Game1.smallFont);

        this.drawMouse(b);
    }

    private void DrawGlobalSummary(SpriteBatch b)
    {
        Rectangle summaryArea = new(
            this.xPositionOnScreen + WindowPadding,
            this.yPositionOnScreen + 64,
            this.width - WindowPadding * 2,
            40);

        IReadOnlyList<string> lines = this.getSummaryLines();
        if (lines.Count == 0)
            return;

        this.DrawFlatPanel(b, summaryArea, new Color(248, 238, 216), new Color(121, 87, 55), 2);

        int count = Math.Min(3, lines.Count);
        int cardWidth = summaryArea.Width / count;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                b.Draw(Game1.staminaRect, new Rectangle(summaryArea.X + i * cardWidth, summaryArea.Y + 7, 2, summaryArea.Height - 14), new Color(121, 87, 55) * 0.55f);

            Rectangle card = new(summaryArea.X + i * cardWidth, summaryArea.Y, i == count - 1 ? summaryArea.Right - (summaryArea.X + i * cardWidth) : cardWidth, summaryArea.Height);
            Utility.drawTextWithShadow(
                b,
                this.FitText(lines[i], Game1.tinyFont, card.Width - 22),
                Game1.tinyFont,
                new Vector2(card.X + 12, card.Y + 12),
                new Color(78, 53, 35));
        }
    }

    private void DrawMemberList(SpriteBatch b, List<SquadMemberState> members, Rectangle listArea)
    {
        Utility.drawTextWithShadow(
            b,
            this.translate("companion.panel.member_list", null),
            Game1.smallFont,
            new Vector2(listArea.X + 16, listArea.Y + 13),
            new Color(68, 43, 27));

        Rectangle rowsArea = new(listArea.X, listArea.Y + 42, listArea.Width, listArea.Height - 52);
        this.memberListArea = rowsArea;
        int rowHeight = MemberRowHeight;
        int rowStride = rowHeight + MemberRowGap;
        int visibleHeight = Math.Max(1, rowsArea.Height - MemberScrollPadding * 2);
        this.totalMemberRowsHeight = members.Count * rowStride;
        this.memberListMaxScroll = Math.Max(0, this.totalMemberRowsHeight - visibleHeight);
        this.memberListScrollOffset = Math.Clamp(this.memberListScrollOffset, 0, this.memberListMaxScroll);

        int y = rowsArea.Y + MemberScrollPadding - this.memberListScrollOffset;
        foreach (SquadMemberState member in members)
        {
            Rectangle row = new(rowsArea.X + 12, y, rowsArea.Width - 24, rowHeight);
            if (row.Bottom < rowsArea.Y + MemberScrollPadding || row.Y > rowsArea.Bottom - MemberScrollPadding)
            {
                y += rowStride;
                continue;
            }

            bool selected = string.Equals(member.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase);
            this.DrawPanel(b, row, selected ? SelectedRowColor : RowColor, selected ? AccentColor : new Color(131, 95, 58));
            this.memberRows.Add((row, member.NpcName));
            b.Draw(Game1.staminaRect, new Rectangle(row.Right - 12, row.Y + 15, 5, row.Height - 30), this.GetMemberStatusColor(member));

            NPC? npc = this.getNpc(member.NpcName);
            int portraitSize = row.Width < 250 ? 54 : 72;
            Rectangle portrait = new(row.X + 11, row.Y + (row.Height - portraitSize) / 2, portraitSize, portraitSize);
            this.DrawPanel(b, portrait, Color.White, selected ? AccentColor : new Color(126, 94, 66));
            this.DrawPortrait(b, npc, portrait);

            int textX = portrait.Right + 10;
            int textW = row.Right - textX - 24;
            Utility.drawTextWithShadow(b, this.FitText(member.DisplayName, Game1.smallFont, textW), Game1.smallFont, new Vector2(textX, row.Y + 14), new Color(66, 42, 21));
            Utility.drawTextWithShadow(
                b,
                this.translate("companion.panel.level", new { level = member.Level }),
                Game1.tinyFont,
                new Vector2(textX, row.Y + 40),
                Color.DarkSlateGray);
            Utility.drawTextWithShadow(
                b,
                this.FitText(this.getStatusText(member), Game1.tinyFont, textW),
                Game1.tinyFont,
                new Vector2(textX, row.Y + 60),
                Color.Sienna);
            y += rowStride;

            if (y > rowsArea.Bottom + rowHeight)
                break;
        }

        if (this.memberListMaxScroll > 0)
            this.DrawMemberListScrollbar(b, rowsArea);
    }

    private void DrawSelectedMember(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        NPC? npc = this.getNpc(member.NpcName);

        bool shortLayout = area.Height < 460;
        bool compactHeader = area.Width < 620 || shortLayout;
        int portraitBoxSize = shortLayout ? 56 : compactHeader ? 88 : 104;
        int headerInset = shortLayout ? 12 : 22;
        Rectangle portraitBox = new(area.X + headerInset, area.Y + (shortLayout ? 10 : 22), portraitBoxSize, portraitBoxSize);
        this.DrawPanel(b, portraitBox, new Color(255, 247, 226), new Color(118, 82, 48));
        this.DrawPortrait(b, npc, portraitBox);

        int tabWidth = compactHeader
            ? Math.Max(78, (area.Width - 56) / 2)
            : 128;
        if (shortLayout)
        {
            int tabY = portraitBox.Bottom + 6;
            const int tabInset = 12;
            const int tabGap = 4;
            const int tabHeight = 30;
            int compactActionWidth = Math.Max(20, (area.Width - tabInset * 2 - tabGap * 4) / 5);
            this.skillsButton = new Rectangle(area.X + tabInset, tabY, compactActionWidth, tabHeight);
            this.inventoryButton = new Rectangle(this.skillsButton.Right + tabGap, tabY, compactActionWidth, tabHeight);
            this.waitButton = new Rectangle(this.inventoryButton.Right + tabGap, tabY, compactActionWidth, tabHeight);
            this.recallButton = new Rectangle(this.waitButton.Right + tabGap, tabY, compactActionWidth, tabHeight);
            this.dismissButton = new Rectangle(this.recallButton.Right + tabGap, tabY, Math.Max(1, area.Right - tabInset - (this.recallButton.Right + tabGap)), tabHeight);
        }
        else if (compactHeader)
        {
            int tabY = portraitBox.Bottom + 12;
            const int tabInset = 22;
            const int tabHeight = 34;
            this.skillsButton = new Rectangle(area.X + tabInset, tabY, tabWidth, tabHeight);
            this.inventoryButton = new Rectangle(this.skillsButton.Right + 8, tabY, area.Right - tabInset - (this.skillsButton.Right + 8), tabHeight);
        }
        else
        {
            this.inventoryButton = new Rectangle(area.Right - 24 - tabWidth, area.Y + 28, tabWidth, 34);
            this.skillsButton = new Rectangle(this.inventoryButton.X - tabWidth - 10, this.inventoryButton.Y, tabWidth, 34);
        }

        int textX = portraitBox.Right + (shortLayout ? 12 : 20);
        int textRight = compactHeader ? area.Right - 24 : this.skillsButton.X - 18;
        int textWidth = Math.Max(1, textRight - textX);
        int xpTextOffset = shortLayout ? 72 : 92;

        Utility.drawTextWithShadow(
            b,
            this.FitText(member.DisplayName, Game1.smallFont, textWidth),
            Game1.smallFont,
            new Vector2(textX, area.Y + (shortLayout ? 12 : 24)),
            Color.DarkSlateGray);
        Utility.drawTextWithShadow(
            b,
            this.FitText(this.translate("companion.panel.level", new { level = member.Level }), Game1.tinyFont, Math.Max(1, textWidth / 2)),
            Game1.tinyFont,
            new Vector2(textX + 2, area.Y + (shortLayout ? 39 : 66)),
            Color.DarkSlateGray);
        Utility.drawTextWithShadow(
            b,
            this.FitText(this.translate("companion.panel.xp_short", new
        {
            xp = member.Xp,
            next = CompanionProgression.GetNextLevelXp(member.Level)
        }), Game1.tinyFont, Math.Max(1, textRight - (textX + xpTextOffset))),
            Game1.tinyFont,
            new Vector2(textX + xpTextOffset, area.Y + (shortLayout ? 39 : 66)),
            Color.DarkSlateGray);
        if (!shortLayout)
        {
            this.DrawBadge(
                b,
                new Rectangle(textX, area.Y + 94, Math.Min(132, textWidth), 26),
                this.translate("companion.panel.points_short", new { points = member.UnspentSkillPoints }),
                member.UnspentSkillPoints > 0 ? new Color(241, 220, 142) : new Color(231, 218, 194),
                new Color(121, 87, 55));
        }
        if (!compactHeader)
            this.DrawXpBar(b, new Rectangle(textX, area.Y + 126, Math.Min(390, Math.Max(1, textWidth)), 16), member);

        this.DrawTabButton(b, this.skillsButton, this.translate("companion.panel.tab_skills", null), !this.showingInventory);
        this.DrawTabButton(b, this.inventoryButton, this.translate("companion.panel.tab_inventory", null), this.showingInventory);

        if (shortLayout)
        {
            this.DrawButton(
                b,
                this.waitButton,
                this.translate(member.Mode == CompanionMode.Following ? "management.wait" : "management.resume", null),
                member.Mode != CompanionMode.Following);
            this.DrawButton(b, this.recallButton, this.translate("management.recall", null), false);
            this.DrawButton(b, this.dismissButton, this.translate("management.dismiss", null), false);

            Rectangle shortBody = new(
                area.X + 12,
                this.skillsButton.Bottom + 8,
                area.Width - 24,
                Math.Max(1, area.Bottom - this.skillsButton.Bottom - 20));
            if (this.showingInventory)
                this.DrawInventory(b, member, shortBody);
            else
                this.DrawSkillsAndDirectives(b, member, shortBody);

            return;
        }

        int commandTop = compactHeader ? this.skillsButton.Bottom + 10 : portraitBox.Bottom + 12;
        int commandGap = 8;
        int commandWidth = Math.Max(1, (area.Width - 44 - commandGap * 2) / 3);
        this.waitButton = new Rectangle(area.X + 22, commandTop, commandWidth, 34);
        this.recallButton = new Rectangle(this.waitButton.Right + commandGap, commandTop, commandWidth, 34);
        this.dismissButton = new Rectangle(this.recallButton.Right + commandGap, commandTop, area.Right - 22 - (this.recallButton.Right + commandGap), 34);
        this.DrawButton(
            b,
            this.waitButton,
            this.translate(member.Mode == CompanionMode.Following ? "management.wait" : "management.resume", null),
            member.Mode != CompanionMode.Following);
        this.DrawButton(b, this.recallButton, this.translate("management.recall", null), false);
        this.DrawButton(b, this.dismissButton, this.translate("management.dismiss", null), false);

        int detailTop = this.waitButton.Bottom + 10;
        Rectangle detailLines = new(area.X + 22, detailTop, area.Width - 44, 58);
        this.DrawDetailLines(b, member, detailLines);

        Rectangle bodyArea = new(area.X + 22, detailLines.Bottom + 12, area.Width - 44, Math.Max(1, area.Bottom - detailLines.Bottom - 34));
        if (this.showingInventory)
            this.DrawInventory(b, member, bodyArea);
        else
            this.DrawSkillsAndDirectives(b, member, bodyArea);
    }

    private void DrawDetailLines(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        this.DrawFlatPanel(b, area, new Color(250, 238, 215), new Color(137, 99, 62), 2);
        bool showMap = area.Width >= 430;
        Rectangle textArea = showMap
            ? new Rectangle(area.X, area.Y, area.Width - 154, area.Height)
            : area;
        if (showMap)
            this.DrawSquadMap(b, this.getMapInfo(member), new Rectangle(area.Right - 146, area.Y + 6, 134, area.Height - 12));

        IReadOnlyList<string> lines = this.getDetailLines(member);
        int y = area.Y + 7;
        foreach (string line in lines.Take(showMap ? 3 : 4))
        {
            Utility.drawTextWithShadow(
                b,
                this.FitText(line, Game1.tinyFont, textArea.Width - 20),
                Game1.tinyFont,
                new Vector2(textArea.X + 12, y),
                new Color(84, 59, 40));
            y += 16;
        }
    }

    private void DrawSquadMap(SpriteBatch b, CompanionPanelMapInfo info, Rectangle area)
    {
        this.DrawFlatPanel(b, area, new Color(238, 227, 206), new Color(137, 99, 62), 1);
        string state = this.FitText(this.translate(info.StatusKey, null), Game1.tinyFont, area.Width - 10);
        Utility.drawTextWithShadow(
            b,
            state,
            Game1.tinyFont,
            new Vector2(area.X + 5, area.Y + 3),
            new Color(73, 52, 37));

        Rectangle map = new(area.Right - 44, area.Y + 15, 34, 34);
        b.Draw(Game1.staminaRect, map, Color.Black * 0.16f);
        Rectangle center = new(map.X + map.Width / 2 - 3, map.Y + map.Height / 2 - 3, 6, 6);
        b.Draw(Game1.staminaRect, center, AccentBlue);

        int npcX = map.X + map.Width / 2 - 3;
        int npcY = map.Y + map.Height / 2 - 3;
        if (info.SameLocation)
        {
            int dx = Math.Clamp(info.NpcX - info.OwnerX, -4, 4);
            int dy = Math.Clamp(info.NpcY - info.OwnerY, -4, 4);
            npcX = Math.Clamp(map.X + map.Width / 2 + dx * 4 - 3, map.X + 2, map.Right - 8);
            npcY = Math.Clamp(map.Y + map.Height / 2 + dy * 4 - 3, map.Y + 2, map.Bottom - 8);
        }
        else
        {
            npcX = map.Right - 9;
            npcY = map.Y + 3;
        }

        Color npcColor = info.StatusKey switch
        {
            "companion.map.working" => new Color(207, 133, 50),
            "companion.map.returning" => new Color(86, 145, 196),
            "companion.map.stuck" => new Color(190, 78, 63),
            "companion.map.other_location" => new Color(124, 102, 149),
            _ => AccentColor
        };
        b.Draw(Game1.staminaRect, new Rectangle(npcX, npcY, 7, 7), npcColor);
    }

    private void DrawSkillsAndDirectives(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        bool compactVertical = area.Height < 300;
        Utility.drawTextWithShadow(
            b,
            this.translate("companion.panel.skills", null),
            Game1.tinyFont,
            new Vector2(area.X, area.Y + 4),
            Color.DarkSlateGray);

        int directiveHeight = compactVertical ? 30 : 36;
        int directiveGap = 8;
        int previewHeight = compactVertical ? 0 : 32;
        bool singleDirectiveRow = area.Width >= 420 || compactVertical;
        int directiveBlockHeight = singleDirectiveRow
            ? directiveHeight
            : directiveHeight * 2 + directiveGap;
        int skillTop = area.Y + (compactVertical ? 26 : 32);
        int previewAndGapHeight = previewHeight > 0 ? previewHeight + 8 : 0;
        int previewTop = area.Bottom - directiveBlockHeight - previewAndGapHeight;
        int directiveTop = previewTop + previewAndGapHeight;
        int availableSkillHeight = Math.Max(1, previewTop - skillTop - (compactVertical ? 6 : 12));
        int maxRowsAtReadableHeight = Math.Max(1, (availableSkillHeight + SkillNodeGap) / (26 + SkillNodeGap));
        int columns = Math.Clamp(
            (int)Math.Ceiling(CompanionProgression.Skills.Length / (double)maxRowsAtReadableHeight),
            1,
            4);
        int rows = (int)Math.Ceiling(CompanionProgression.Skills.Length / (double)columns);
        int nodeHeight = Math.Clamp((availableSkillHeight - Math.Max(0, rows - 1) * SkillNodeGap) / Math.Max(1, rows), 18, 42);
        int nodeWidth = Math.Max(1, (area.Width - SkillNodeGap * (columns - 1)) / columns);

        bool canDrawSkillGrid = availableSkillHeight >= 70;
        for (int i = 0; canDrawSkillGrid && i < CompanionProgression.Skills.Length; i++)
        {
            CompanionSkillDefinition skill = CompanionProgression.Skills[i];
            int col = i % columns;
            int row = i / columns;
            Rectangle node = new(area.X + col * (nodeWidth + SkillNodeGap), skillTop + row * (nodeHeight + SkillNodeGap), nodeWidth, nodeHeight);
            bool unlocked = member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase);
            bool available = !unlocked
                && member.UnspentSkillPoints >= skill.Cost
                && (string.IsNullOrWhiteSpace(skill.PrerequisiteId) || member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase));
            this.DrawSkillNode(b, node, skill, unlocked, available);
            this.skillButtons.Add((node, skill.Id));
        }

        if (previewHeight > 0)
            this.DrawTargetPreview(b, member, new Rectangle(area.X, previewTop, area.Width, previewHeight));

        if (singleDirectiveRow)
        {
            int directiveWidth = (area.Width - directiveGap * 2) / 3;
            this.DrawDirectiveButton(b, new Rectangle(area.X, directiveTop, directiveWidth, directiveHeight), this.translate("companion.directive.wood.short", null), member.SearchWood, CompanionDirective.SearchWood);
            this.DrawDirectiveButton(b, new Rectangle(area.X + directiveWidth + directiveGap, directiveTop, directiveWidth, directiveHeight), this.translate("companion.directive.mining.short", null), member.SearchMining, CompanionDirective.SearchMining);
            this.DrawDirectiveButton(b, new Rectangle(area.X + (directiveWidth + directiveGap) * 2, directiveTop, area.Right - (area.X + (directiveWidth + directiveGap) * 2), directiveHeight), this.translate("companion.directive.clear.short", null), member.ClearArea, CompanionDirective.ClearArea);
        }
        else
        {
            int directiveWidth = (area.Width - directiveGap) / 2;
            this.DrawDirectiveButton(b, new Rectangle(area.X, directiveTop, directiveWidth, directiveHeight), this.translate("companion.directive.wood.short", null), member.SearchWood, CompanionDirective.SearchWood);
            this.DrawDirectiveButton(b, new Rectangle(area.X + directiveWidth + directiveGap, directiveTop, area.Right - (area.X + directiveWidth + directiveGap), directiveHeight), this.translate("companion.directive.mining.short", null), member.SearchMining, CompanionDirective.SearchMining);
            this.DrawDirectiveButton(b, new Rectangle(area.X, directiveTop + directiveHeight + directiveGap, area.Width, directiveHeight), this.translate("companion.directive.clear.short", null), member.ClearArea, CompanionDirective.ClearArea);
        }
    }

    private void DrawTargetPreview(SpriteBatch b, SquadMemberState member, Rectangle bounds)
    {
        this.DrawFlatPanel(b, bounds, new Color(244, 232, 207), new Color(137, 99, 62), 2);
        string text = !string.IsNullOrWhiteSpace(member.PreviewTargetKey) && member.PreviewTargetX >= 0 && member.PreviewTargetY >= 0
            ? this.translate("companion.preview.target", new
            {
                target = this.translate(member.PreviewTargetKey, null),
                x = member.PreviewTargetX,
                y = member.PreviewTargetY
            })
            : this.translate("companion.preview.reason", new
            {
                reason = string.IsNullOrWhiteSpace(member.PreviewReasonKey)
                    ? this.translate("companion.preview.inactive", null)
                    : this.translate(member.PreviewReasonKey, null)
            });

        Utility.drawTextWithShadow(
            b,
            this.FitText(text, Game1.tinyFont, bounds.Width - 18),
            Game1.tinyFont,
            new Vector2(bounds.X + 9, bounds.Y + 8),
            new Color(84, 59, 40));
    }

    private void DrawSkillNode(SpriteBatch b, Rectangle bounds, CompanionSkillDefinition skill, bool unlocked, bool available)
    {
        Color fill = unlocked
            ? new Color(209, 236, 203)
            : available
                ? new Color(244, 225, 152)
                : new Color(218, 207, 190);
        Color border = unlocked ? AccentColor : new Color(120, 92, 63);
        this.DrawFlatPanel(b, bounds, fill, border, 2);

        int badgeSize = Math.Clamp(bounds.Height - 16, 18, 24);
        Rectangle badge = new(bounds.Right - badgeSize - 8, bounds.Y + (bounds.Height - badgeSize) / 2, badgeSize, badgeSize);
        b.Draw(Game1.staminaRect, badge, unlocked ? AccentColor : Color.Black * 0.22f);
        string cost = skill.Cost.ToString();
        Vector2 costSize = Game1.tinyFont.MeasureString(cost);
        Utility.drawTextWithShadow(
            b,
            cost,
            Game1.tinyFont,
            new Vector2(badge.X + (badge.Width - costSize.X) / 2f, badge.Y + (badge.Height - costSize.Y) / 2f),
            unlocked ? Color.White : ButtonTextColor);

        string label = this.FitText(this.translate(skill.NameKey, null), Game1.tinyFont, bounds.Width - badgeSize - 28);
        Vector2 labelSize = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + 10, bounds.Y + (bounds.Height - labelSize.Y) / 2f),
            Game1.textColor);
    }

    private void DrawInventory(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        bool denseInventory = area.Height < 220;
        bool compactInventory = area.Width < 380 && !denseInventory;
        int withdrawWidth = denseInventory ? Math.Min(130, Math.Max(80, area.Width / 3)) : compactInventory ? Math.Min(area.Width, 150) : 160;
        this.withdrawAllButton = denseInventory
            ? new Rectangle(area.Right - withdrawWidth, area.Y, withdrawWidth, 30)
            : compactInventory
            ? new Rectangle(area.X, area.Y + 34, withdrawWidth, 36)
            : new Rectangle(area.Right - 168, area.Y, 160, 38);
        int titleWidth = compactInventory ? area.Width : area.Width - withdrawWidth - 18;

        Utility.drawTextWithShadow(
            b,
            this.FitText(this.translate("companion.inventory.title", new { npc = member.DisplayName }), Game1.tinyFont, titleWidth),
            Game1.tinyFont,
            new Vector2(area.X, area.Y + 10),
            Game1.textColor);
        List<Item> items = this.getInventoryItems(member);
        this.DrawButton(b, this.withdrawAllButton, this.translate("companion.inventory.withdraw_all", null), false);

        int slotSize = denseInventory ? 42 : 66;
        int slotGap = denseInventory ? 6 : 10;
        int columns = Math.Clamp((area.Width + slotGap) / (slotSize + slotGap), 1, denseInventory ? this.inventorySlots : 6);
        int startX = area.X;
        int startY = area.Y + (denseInventory ? 38 : compactInventory ? 82 : 50);
        for (int i = 0; i < this.inventorySlots; i++)
        {
            int col = i % columns;
            int row = i / columns;
            Rectangle slot = new(startX + col * (slotSize + slotGap), startY + row * (slotSize + slotGap), slotSize, slotSize);
            this.DrawPanel(b, slot, new Color(236, 222, 196), new Color(112, 80, 50));
            this.inventorySlotsBounds.Add((slot, i));
            if (i < items.Count)
            {
                Item item = items[i];
                float itemScale = denseInventory ? 0.55f : 0.92f;
                int itemInset = denseInventory ? 3 : 5;
                item.drawInMenu(b, new Vector2(slot.X + itemInset, slot.Y + itemInset), itemScale);
                if (item.Stack > 1)
                    Utility.drawTextWithShadow(
                        b,
                        item.Stack.ToString(),
                        Game1.tinyFont,
                        new Vector2(slot.Right - (denseInventory ? 17 : 22), slot.Bottom - (denseInventory ? 17 : 22)),
                        Color.Black);
            }
        }

        if (items.Count == 0 && !denseInventory)
            Utility.drawTextWithShadow(
                b,
                this.translate("companion.inventory.empty", null),
                Game1.tinyFont,
                new Vector2(area.X, startY + slotSize * 2 + 32),
                Color.SaddleBrown);

        RecentCompanionLoot? recent = denseInventory ? null : member.RecentLoot.FirstOrDefault();
        if (recent is not null)
        {
            string recentText = this.translate("companion.loot.recent", new
            {
                item = recent.DisplayName,
                count = recent.Stack,
                source = string.IsNullOrWhiteSpace(recent.SourceKey) ? "" : this.translate(recent.SourceKey, null)
            });
            Utility.drawTextWithShadow(
                b,
                this.FitText(recentText, Game1.tinyFont, area.Width - 12),
                Game1.tinyFont,
                new Vector2(area.X, area.Bottom - 24),
                new Color(84, 59, 40));
        }
    }

    private void DrawDirectiveButton(SpriteBatch b, Rectangle bounds, string label, bool active, CompanionDirective directive)
    {
        this.DrawFlatPanel(b, bounds, active ? new Color(202, 232, 202) : new Color(236, 222, 193), active ? AccentColor : new Color(120, 92, 63), 2);
        Rectangle indicator = new(bounds.X + 9, bounds.Y + 10, 18, bounds.Height - 20);
        b.Draw(Game1.staminaRect, indicator, active ? AccentColor : Color.Black * 0.22f);
        SpriteFont font = Game1.tinyFont;
        string text = this.FitText(label, font, bounds.Width - 40);
        Vector2 size = font.MeasureString(text);
        Utility.drawTextWithShadow(
            b,
            text,
            font,
            new Vector2(bounds.X + 34, bounds.Y + Math.Max(4, (bounds.Height - size.Y) / 2f)),
            ButtonTextColor);
        this.directiveButtons.Add((bounds, directive));
    }

    private void DrawButton(SpriteBatch b, Rectangle bounds, string text, bool active)
    {
        drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            active ? ButtonActive : ButtonIdle);
        string label = this.FitText(text, Game1.tinyFont, bounds.Width - 14);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + Math.Max(8, (bounds.Width - size.X) / 2), bounds.Y + Math.Max(4, (bounds.Height - size.Y) / 2f)),
            ButtonTextColor);
    }

    private void DrawTabButton(SpriteBatch b, Rectangle bounds, string text, bool active)
    {
        this.DrawPanel(b, bounds, active ? SoftBlue : new Color(236, 222, 193), active ? AccentBlue : new Color(120, 92, 63));
        string label = this.FitText(text, Game1.tinyFont, bounds.Width - 16);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(4, (bounds.Height - size.Y) / 2f)),
            ButtonTextColor);
    }

    private void DrawBadge(SpriteBatch b, Rectangle bounds, string text, Color fill, Color border)
    {
        this.DrawPanel(b, bounds, fill, border);
        string label = this.FitText(text, Game1.tinyFont, bounds.Width - 14);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(3, (bounds.Height - size.Y) / 2f)),
            ButtonTextColor);
    }

    private void DrawPanel(SpriteBatch b, Rectangle bounds, Color fill, Color border)
    {
        drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            border);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 6, bounds.Y + 6, Math.Max(1, bounds.Width - 12), Math.Max(1, bounds.Height - 12)),
            fill);
    }

    private void DrawFlatPanel(SpriteBatch b, Rectangle bounds, Color fill, Color border, int borderSize)
    {
        b.Draw(Game1.staminaRect, bounds, border);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(
                bounds.X + borderSize,
                bounds.Y + borderSize,
                Math.Max(1, bounds.Width - borderSize * 2),
                Math.Max(1, bounds.Height - borderSize * 2)),
            fill);
    }

    private void DrawMemberListScrollbar(SpriteBatch b, Rectangle listArea)
    {
        if (this.memberListMaxScroll <= 0)
            return;

        Rectangle track = new(listArea.Right - 10, listArea.Y + 10, 8, listArea.Height - 20);
        int visibleHeight = Math.Max(1, listArea.Height - MemberScrollPadding * 2);
        int thumbHeight = Math.Clamp(track.Height * visibleHeight / (visibleHeight + this.memberListMaxScroll), 24, track.Height);
        int travel = Math.Max(1, track.Height - thumbHeight);
        int thumbTop = track.Y + travel * this.memberListScrollOffset / Math.Max(1, this.memberListMaxScroll);

        b.Draw(Game1.staminaRect, track, Color.Black * 0.15f);
        b.Draw(Game1.staminaRect, new Rectangle(track.X, thumbTop, track.Width, thumbHeight), new Color(130, 98, 70));
    }

    private void DrawPortrait(SpriteBatch b, NPC? npc, Rectangle bounds)
    {
        if (npc is not null)
        {
            try
            {
                Texture2D portrait = Game1.content.Load<Texture2D>($"Portraits/{npc.Name}");
                b.Draw(portrait, new Rectangle(bounds.X + 8, bounds.Y + 8, bounds.Width - 16, bounds.Height - 16), new Rectangle(0, 0, 64, 64), Color.White);
                return;
            }
            catch
            {
                // Some pets and custom actors do not have portrait assets. Use the sprite fallback.
            }
        }

        if (npc?.Sprite?.Texture is not null)
        {
            int spriteWidth = Math.Max(1, Math.Min(64, bounds.Width - 12));
            int spriteHeight = Math.Max(1, Math.Min(96, bounds.Height - 12));
            Rectangle target = new(bounds.X + (bounds.Width - spriteWidth) / 2, bounds.Y + bounds.Height - spriteHeight - 6, spriteWidth, spriteHeight);
            b.Draw(npc.Sprite.Texture, target, npc.Sprite.SourceRect, Color.White);
        }
    }

    private void DrawXpBar(SpriteBatch b, Rectangle bounds, SquadMemberState member)
    {
        int current = CompanionProgression.GetXpForLevel(member.Level);
        int next = CompanionProgression.GetNextLevelXp(member.Level);
        float progress = next <= current ? 1f : Math.Clamp((member.Xp - current) / (float)(next - current), 0f, 1f);
        this.DrawPanel(b, bounds, new Color(98, 73, 58), new Color(70, 48, 34));
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 3, bounds.Y + 3, (int)((bounds.Width - 6) * progress), bounds.Height - 6), new Color(96, 165, 220));
    }

    private SquadMemberState? GetSelectedMember(List<SquadMemberState>? members = null)
    {
        members ??= this.getMembers().ToList();
        return members.FirstOrDefault(p => string.Equals(p.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureSelection(List<SquadMemberState> members)
    {
        if (members.Any(p => string.Equals(p.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase)))
            return;

        this.selectedNpcName = members[0].NpcName;
        this.showingInventory = false;
    }

    private bool SelectRelativeMember(int delta)
    {
        List<SquadMemberState> members = this.getMembers().ToList();
        if (members.Count == 0)
            return false;

        int currentIndex = members.FindIndex(p => string.Equals(p.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = 0;

        return this.SelectMemberByIndex(currentIndex + delta, members);
    }

    private bool SelectMemberByIndex(int index, List<SquadMemberState>? members = null)
    {
        members ??= this.getMembers().ToList();
        if (members.Count == 0)
            return false;

        int selectedIndex = Math.Clamp(index, 0, members.Count - 1);
        string npcName = members[selectedIndex].NpcName;
        if (string.Equals(this.selectedNpcName, npcName, StringComparison.OrdinalIgnoreCase))
            return false;

        this.selectedNpcName = npcName;
        this.EnsureSelectedMemberVisible(members, selectedIndex);
        return true;
    }

    private int GetVisibleMemberRowCount()
    {
        if (this.memberListArea.Height <= 0)
            return 4;

        int visibleHeight = Math.Max(1, this.memberListArea.Height - MemberScrollPadding * 2);
        return Math.Max(1, visibleHeight / Math.Max(1, MemberRowHeight + MemberRowGap));
    }

    private void EnsureSelectedMemberVisible(List<SquadMemberState> members, int selectedIndex)
    {
        if (this.memberListArea.Height <= 0)
            return;

        int rowStride = MemberRowHeight + MemberRowGap;
        int visibleHeight = Math.Max(1, this.memberListArea.Height - MemberScrollPadding * 2);
        this.memberListMaxScroll = Math.Max(0, members.Count * rowStride - visibleHeight);

        int rowTop = selectedIndex * rowStride;
        int rowBottom = rowTop + MemberRowHeight;
        if (rowTop < this.memberListScrollOffset)
            this.memberListScrollOffset = rowTop;
        else if (rowBottom > this.memberListScrollOffset + visibleHeight)
            this.memberListScrollOffset = rowBottom - visibleHeight;

        this.memberListScrollOffset = Math.Clamp(this.memberListScrollOffset, 0, this.memberListMaxScroll);
    }

    private string BuildSkillHoverText(SquadMemberState member, CompanionSkillDefinition skill)
    {
        List<string> lines = new()
        {
            this.translate(skill.NameKey, null),
            this.translate(skill.DescriptionKey, null),
            this.translate("companion.skill.cost", new { cost = skill.Cost }),
            this.translate("companion.skill.points_available", new { points = member.UnspentSkillPoints })
        };

        if (!string.IsNullOrWhiteSpace(skill.PrerequisiteId))
        {
            CompanionSkillDefinition? prerequisite = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skill.PrerequisiteId);
            if (prerequisite is not null)
                lines.Add(this.translate("companion.skill.requires", new { skill = this.translate(prerequisite.NameKey, null) }));
        }

        if (member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(this.translate("companion.skill.learned", null));
        }
        else if (!string.IsNullOrWhiteSpace(skill.PrerequisiteId)
            && !member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(this.translate("companion.skill.locked", null));
        }
        else if (member.UnspentSkillPoints < skill.Cost)
        {
            lines.Add(this.translate("companion.skill.no_points", null));
        }
        else
        {
            lines.Add(this.translate("companion.skill.learn", null));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private Color GetMemberStatusColor(SquadMemberState member)
    {
        if (member.Mode == CompanionMode.Waiting || member.Mode == CompanionMode.ParkedForDisconnect)
            return new Color(221, 163, 72);

        if (member.CurrentActivityKey == "companion.status.stuck")
            return new Color(190, 78, 63);

        if (member.CurrentActivityKey == "companion.status.working")
            return new Color(207, 133, 50);

        if (member.CurrentActivityKey == "companion.status.returning")
            return AccentBlue;

        if (member.Inventory.Count >= this.inventorySlots)
            return new Color(218, 170, 65);

        return AccentColor;
    }

    private string FitText(string text, SpriteFont font, int width)
    {
        if (width <= 0)
            return "";

        if (font.MeasureString(text).X <= width)
            return text;

        const string suffix = "...";
        if (font.MeasureString(suffix).X > width)
            return "";

        while (text.Length > 0 && font.MeasureString(text + suffix).X > width)
            text = text[..^1];

        return text + suffix;
    }
}
