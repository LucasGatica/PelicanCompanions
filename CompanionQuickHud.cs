using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

internal sealed class CompanionQuickHud : IClickableMenu
{
    private const int DetailedRowWidth = 248;
    private const int CompactRowWidth = 168;
    private const int DetailedRowHeight = 84;
    private const int CompactRowHeight = 82;
    private const int RowGap = 8;
    private const int DetailedPortraitSize = 56;
    private const int CompactPortraitSize = 58;
    private const int DetailedActionButtonWidth = 38;
    private const int CompactActionButtonWidth = 44;
    private const int ActionButtonHeight = 32;
    private const int ActionButtonGap = 5;
    private const int OverflowButtonHeight = 28;
    private const int EdgeInset = 9;

    private static readonly Color RowColor = new(255, 245, 218);
    private static readonly Color RowHoverColor = new(255, 250, 232);
    private static readonly Color RowBorder = Color.White;
    private static readonly Color PortraitFill = new(248, 235, 205);
    private static readonly Color PortraitBorder = new(116, 82, 54);
    private static readonly Color WorkActive = new(102, 177, 104);
    private static readonly Color WorkIdle = new(232, 214, 174);
    private static readonly Color FollowColor = new(91, 151, 211);
    private static readonly Color ButtonBorder = new(95, 71, 47);
    private static readonly Color ButtonHoverBorder = new(255, 248, 205);
    private static readonly Color IconDark = new(70, 47, 31);
    private static readonly Color IconLight = new(255, 245, 210);
    private static readonly Color MetalLight = new(225, 230, 221);
    private static readonly Color MetalDark = new(107, 103, 92);
    private static readonly Color WaitingIndicator = new(221, 163, 72);
    private static readonly Color WorkingIndicator = new(73, 150, 88);
    private static readonly Color FollowingIndicator = new(64, 132, 196);
    private static readonly Color WarningIndicator = new(193, 79, 64);
    private static readonly Color TextColor = new(64, 42, 26);
    private static readonly Color MutedTextColor = new(94, 72, 52);
    private static readonly Color ButtonTextColor = new(70, 47, 31);
    private static readonly Color BadgeFill = new(238, 224, 196);
    private static readonly Color BadgeActiveFill = new(211, 235, 204);

    private readonly Func<IReadOnlyList<SquadMemberState>> getMembers;
    private readonly Func<string, NPC?> getNpc;
    private readonly Func<string, object?, string> translate;
    private readonly Func<SquadMemberState, string> getStatusText;
    private readonly Func<SquadMemberState, bool> isWorkActive;
    private readonly Func<CompanionQuickHudMode> getMode;
    private readonly Func<int> getMaxVisibleRows;
    private readonly Func<int> getInventorySlotCount;
    private readonly Action<SquadMemberState> toggleWork;
    private readonly Action<SquadMemberState> follow;
    private readonly Action<SquadMemberState> openPanel;

    public CompanionQuickHud(
        Func<IReadOnlyList<SquadMemberState>> getMembers,
        Func<string, NPC?> getNpc,
        Func<string, object?, string> translate,
        Func<SquadMemberState, string> getStatusText,
        Func<SquadMemberState, bool> isWorkActive,
        Func<CompanionQuickHudMode> getMode,
        Func<int> getMaxVisibleRows,
        Func<int> getInventorySlotCount,
        Action<SquadMemberState> toggleWork,
        Action<SquadMemberState> follow,
        Action<SquadMemberState> openPanel)
    {
        this.getMembers = getMembers;
        this.getNpc = getNpc;
        this.translate = translate;
        this.getStatusText = getStatusText;
        this.isWorkActive = isWorkActive;
        this.getMode = getMode;
        this.getMaxVisibleRows = getMaxVisibleRows;
        this.getInventorySlotCount = getInventorySlotCount;
        this.toggleWork = toggleWork;
        this.follow = follow;
        this.openPanel = openPanel;
    }

    public void Draw(SpriteBatch b)
    {
        List<SquadMemberState> allMembers = this.getMembers().ToList();
        if (allMembers.Count == 0)
            return;

        QuickHudLayout layout = this.GetLayout();
        IReadOnlyList<SquadMemberState> members = this.GetVisibleMembers(allMembers, layout);
        int hiddenCount = Math.Max(0, allMembers.Count - members.Count);
        bool hasOverflow = hiddenCount > 0;
        string hoverText = "";
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());

        foreach (QuickHudRow row in this.BuildRows(members, hasOverflow, layout))
        {
            SquadMemberState member = row.Member;
            bool workActive = this.isWorkActive(member);
            bool configuredAutonomyWorking = !workActive && member.CurrentActivityKey == "companion.status.working";
            bool hoveringRow = row.Bounds.Contains(mouse);

            this.DrawPanel(b, row.Bounds, hoveringRow ? RowHoverColor : RowColor, RowBorder);
            b.Draw(Game1.staminaRect, row.Indicator, this.GetIndicatorColor(member, workActive));

            this.DrawPanel(b, row.Portrait, PortraitFill, PortraitBorder);
            this.DrawPortrait(b, this.getNpc(member.NpcName), row.Portrait);

            this.DrawIconButton(b, row.WorkButton, QuickHudAction.Work, workActive, row.WorkButton.Contains(mouse));
            this.DrawIconButton(b, row.FollowButton, QuickHudAction.Follow, active: false, row.FollowButton.Contains(mouse));
            if (layout.ShowText)
                this.DrawMemberText(b, row, member, workActive);

            if (row.WorkButton.Contains(mouse))
            {
                hoverText = configuredAutonomyWorking
                    ? this.translate("companion.quick.configured_autonomy_hover", new
                    {
                        npc = member.DisplayName,
                        specialty = this.translate($"companion.specialty.{member.PreferredWorkSpecialty}", null)
                    })
                    : this.translate(
                        workActive ? "companion.quick.work_stop_hover" : "companion.quick.work_hover",
                        new
                        {
                            npc = member.DisplayName,
                            specialty = this.translate($"companion.specialty.{member.PreferredWorkSpecialty}", null)
                        });
            }
            else if (row.FollowButton.Contains(mouse))
                hoverText = this.translate("companion.quick.follow_hover", new { npc = member.DisplayName });
            else if (row.Bounds.Contains(mouse))
                hoverText = this.translate("companion.quick.panel_hover", new { npc = member.DisplayName, status = this.getStatusText(member) });
        }

        if (hasOverflow)
        {
            Rectangle overflowButton = this.GetOverflowButtonBounds(members.Count, layout);
            this.DrawOverflowButton(b, overflowButton, hiddenCount, overflowButton.Contains(mouse));
            if (overflowButton.Contains(mouse))
                hoverText = this.translate("companion.quick.more_hover", new { count = hiddenCount });
        }

        if (!string.IsNullOrWhiteSpace(hoverText))
            drawHoverText(b, hoverText, Game1.smallFont);
    }

    public bool TryHandleClick(Vector2 screenPixels)
    {
        List<SquadMemberState> allMembers = this.getMembers().ToList();
        if (allMembers.Count == 0)
            return false;

        QuickHudLayout layout = this.GetLayout();
        IReadOnlyList<SquadMemberState> members = this.GetVisibleMembers(allMembers, layout);
        int hiddenCount = Math.Max(0, allMembers.Count - members.Count);
        bool hasOverflow = hiddenCount > 0;
        Point point = new((int)screenPixels.X, (int)screenPixels.Y);
        foreach (QuickHudRow row in this.BuildRows(members, hasOverflow, layout))
        {
            if (row.WorkButton.Contains(point))
            {
                this.toggleWork(row.Member);
                Game1.playSound("drumkit6");
                return true;
            }

            if (row.FollowButton.Contains(point))
            {
                this.follow(row.Member);
                Game1.playSound("smallSelect");
                return true;
            }

            if (row.ActionArea.Contains(point))
                return true;

            if (row.Bounds.Contains(point))
            {
                this.openPanel(row.Member);
                Game1.playSound("smallSelect");
                return true;
            }
        }

        if (hasOverflow && this.GetOverflowButtonBounds(members.Count, layout).Contains(point))
        {
            this.openPanel(allMembers[members.Count]);
            Game1.playSound("smallSelect");
            return true;
        }

        return false;
    }

    private IReadOnlyList<SquadMemberState> GetVisibleMembers(IReadOnlyList<SquadMemberState> members, QuickHudLayout layout)
    {
        int configuredRows = Math.Clamp(this.getMaxVisibleRows(), 1, 12);
        int visibleRows = Math.Min(configuredRows, this.GetVisibleRowCapacity(members.Count, layout));
        return members.Take(visibleRows).ToList();
    }

    private int GetVisibleRowCapacity(int totalMemberCount, QuickHudLayout layout)
    {
        if (totalMemberCount <= 0)
            return 0;

        int availableHeight = Math.Max(layout.RowHeight, Game1.uiViewport.Height - 180);
        int fullRows = Math.Max(1, availableHeight / (layout.RowHeight + RowGap));
        if (totalMemberCount <= fullRows)
            return totalMemberCount;

        int overflowReservedHeight = OverflowButtonHeight + RowGap;
        int rowsWithOverflow = Math.Max(1, (availableHeight - overflowReservedHeight) / (layout.RowHeight + RowGap));
        return Math.Min(totalMemberCount, rowsWithOverflow);
    }

    private IEnumerable<QuickHudRow> BuildRows(IReadOnlyList<SquadMemberState> members, bool hasOverflow, QuickHudLayout layout)
    {
        int totalHeight = this.GetHudTotalHeight(members.Count, hasOverflow, layout);
        int startY = this.GetHudStartY(totalHeight);
        int rowWidth = this.GetRowWidth(layout);

        for (int i = 0; i < members.Count; i++)
        {
            int y = startY + i * (layout.RowHeight + RowGap);
            Rectangle bounds = new(8, y, rowWidth, layout.RowHeight);
            Rectangle portrait = new(bounds.X + EdgeInset, y + (layout.RowHeight - layout.PortraitSize) / 2, layout.PortraitSize, layout.PortraitSize);
            int buttonX = bounds.Right - EdgeInset - layout.ActionButtonWidth;
            int buttonY = y + (layout.RowHeight - ActionButtonHeight * 2 - ActionButtonGap) / 2;
            Rectangle work = new(buttonX, buttonY, layout.ActionButtonWidth, ActionButtonHeight);
            Rectangle follow = new(buttonX, work.Bottom + ActionButtonGap, layout.ActionButtonWidth, ActionButtonHeight);
            Rectangle actionArea = new(
                work.X - 5,
                work.Y - 5,
                layout.ActionButtonWidth + 10,
                ActionButtonHeight * 2 + ActionButtonGap + 10);
            Rectangle indicator = new(bounds.X + 3, y + 11, 4, layout.RowHeight - 22);

            yield return new QuickHudRow(members[i], bounds, portrait, work, follow, actionArea, indicator);
        }
    }

    private int GetHudTotalHeight(int visibleRowCount, bool hasOverflow, QuickHudLayout layout)
    {
        int rowsHeight = visibleRowCount * layout.RowHeight + Math.Max(0, visibleRowCount - 1) * RowGap;
        return hasOverflow
            ? rowsHeight + RowGap + OverflowButtonHeight
            : rowsHeight;
    }

    private int GetHudStartY(int totalHeight)
    {
        int minY = 96;
        int maxY = Math.Max(minY, Game1.uiViewport.Height - totalHeight - 112);
        return Math.Clamp((Game1.uiViewport.Height - totalHeight) / 2, minY, maxY);
    }

    private int GetRowWidth(QuickHudLayout layout)
    {
        return Math.Max(128, Math.Min(layout.RowWidth, Game1.uiViewport.Width - 24));
    }

    private Rectangle GetOverflowButtonBounds(int visibleRowCount, QuickHudLayout layout)
    {
        int totalHeight = this.GetHudTotalHeight(visibleRowCount, hasOverflow: true, layout);
        int startY = this.GetHudStartY(totalHeight);
        int rowsHeight = visibleRowCount * layout.RowHeight + Math.Max(0, visibleRowCount - 1) * RowGap;
        return new Rectangle(8, startY + rowsHeight + RowGap, this.GetRowWidth(layout), OverflowButtonHeight);
    }

    private Color GetIndicatorColor(SquadMemberState member, bool workActive)
    {
        if (member.CurrentActivityKey == "companion.status.stuck")
            return WarningIndicator;

        if (workActive || member.CurrentActivityKey == "companion.status.working")
            return WorkingIndicator;

        return member.Mode == CompanionMode.Waiting || member.Mode == CompanionMode.ParkedForDisconnect
            ? WaitingIndicator
            : FollowingIndicator;
    }

    private QuickHudLayout GetLayout()
    {
        bool detailed = this.getMode() == CompanionQuickHudMode.Detailed && Game1.uiViewport.Width >= 360;
        return detailed
            ? new QuickHudLayout(DetailedRowWidth, DetailedRowHeight, DetailedPortraitSize, DetailedActionButtonWidth, ShowText: true)
            : new QuickHudLayout(CompactRowWidth, CompactRowHeight, CompactPortraitSize, CompactActionButtonWidth, ShowText: false);
    }

    private void DrawMemberText(SpriteBatch b, QuickHudRow row, SquadMemberState member, bool workActive)
    {
        int textX = row.Portrait.Right + 9;
        int textRight = row.WorkButton.X - 9;
        int textWidth = Math.Max(1, textRight - textX);

        Utility.drawTextWithShadow(
            b,
            this.FitText(member.DisplayName, Game1.tinyFont, textWidth),
            Game1.tinyFont,
            new Vector2(textX, row.Bounds.Y + 12),
            TextColor);

        string status = this.FitText(this.getStatusText(member), Game1.tinyFont, textWidth);
        Utility.drawTextWithShadow(
            b,
            status,
            Game1.tinyFont,
            new Vector2(textX, row.Bounds.Y + 31),
            MutedTextColor);

        int badgeY = row.Bounds.Bottom - 26;
        string levelText = this.translate("companion.quick.level_short", new { level = member.Level });
        int levelWidth = Math.Clamp((int)Game1.tinyFont.MeasureString(levelText).X + 14, 34, Math.Max(34, textWidth / 2));
        this.DrawMiniBadge(
            b,
            new Rectangle(textX, badgeY, levelWidth, 20),
            levelText,
            BadgeFill,
            ButtonBorder);

        int dotsX = textX + levelWidth + 8;
        this.DrawDirectiveDots(b, new Rectangle(dotsX, badgeY + 4, Math.Max(0, textRight - dotsX), 12), member, workActive);
    }

    private void DrawMiniBadge(SpriteBatch b, Rectangle bounds, string text, Color fill, Color border)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        this.DrawFlatPanel(b, bounds, fill, border, 1);
        string label = this.FitText(text, Game1.tinyFont, bounds.Width - 8);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(1, (bounds.Height - size.Y) / 2f)),
            ButtonTextColor);
    }

    private void DrawDirectiveDots(SpriteBatch b, Rectangle bounds, SquadMemberState member, bool workActive)
    {
        const int dotSize = 10;
        const int gap = 5;
        int x = bounds.X;
        this.DrawDirectiveDot(b, new Rectangle(x, bounds.Y, dotSize, dotSize), member.SearchWood || member.ClearArea || workActive, new Color(91, 141, 79));
        x += dotSize + gap;
        this.DrawDirectiveDot(b, new Rectangle(x, bounds.Y, dotSize, dotSize), member.SearchMining || member.ClearArea || workActive, new Color(123, 132, 145));
        x += dotSize + gap;
        bool inventoryFull = member.Inventory.Count >= Math.Max(1, this.getInventorySlotCount());
        this.DrawDirectiveDot(b, new Rectangle(x, bounds.Y, dotSize, dotSize), inventoryFull, inventoryFull ? WarningIndicator : new Color(185, 153, 84));
    }

    private void DrawDirectiveDot(SpriteBatch b, Rectangle bounds, bool active, Color color)
    {
        b.Draw(Game1.staminaRect, bounds, active ? color : Color.Black * 0.18f);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 2, bounds.Y + 2, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 4)), active ? BadgeActiveFill * 0.35f : BadgeFill * 0.45f);
    }

    private void DrawIconButton(SpriteBatch b, Rectangle bounds, QuickHudAction action, bool active, bool hovered)
    {
        Color fill = action == QuickHudAction.Work
            ? active ? WorkActive : WorkIdle
            : FollowColor;
        this.DrawFlatPanel(b, bounds, fill, hovered ? ButtonHoverBorder : ButtonBorder, 2);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, 1), Color.White * 0.45f);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 3, bounds.Bottom - 4, bounds.Width - 6, 1), Color.Black * 0.2f);

        if (action == QuickHudAction.Work)
            this.DrawWorkIcon(b, bounds, active);
        else
            this.DrawFollowIcon(b, bounds);
    }

    private void DrawWorkIcon(SpriteBatch b, Rectangle bounds, bool active)
    {
        Color handle = active ? IconLight : IconDark;
        Color head = active ? Color.White : MetalLight;
        Color headShadow = active ? new Color(68, 117, 74) : MetalDark;

        int centerX = bounds.X + bounds.Width / 2;
        int centerY = bounds.Y + bounds.Height / 2;
        b.Draw(Game1.staminaRect, new Rectangle(centerX - 3, centerY - 2, 6, 13), handle);
        b.Draw(Game1.staminaRect, new Rectangle(centerX - 10, centerY - 11, 20, 6), headShadow);
        b.Draw(Game1.staminaRect, new Rectangle(centerX - 8, centerY - 13, 16, 6), head);
        b.Draw(Game1.staminaRect, new Rectangle(centerX + 6, centerY - 8, 5, 8), headShadow);
    }

    private void DrawFollowIcon(SpriteBatch b, Rectangle bounds)
    {
        int centerX = bounds.X + bounds.Width / 2;
        int centerY = bounds.Y + bounds.Height / 2;
        Color arrow = IconLight;
        Color shadow = new(43, 82, 125);

        b.Draw(Game1.staminaRect, new Rectangle(centerX - 10, centerY - 1, 17, 5), shadow);
        b.Draw(Game1.staminaRect, new Rectangle(centerX - 9, centerY - 3, 16, 5), arrow);
        b.Draw(Game1.staminaRect, new Rectangle(centerX + 4, centerY - 8, 4, 15), shadow);
        b.Draw(Game1.staminaRect, new Rectangle(centerX + 8, centerY - 5, 4, 9), shadow);
        b.Draw(Game1.staminaRect, new Rectangle(centerX + 3, centerY - 10, 4, 15), arrow);
        b.Draw(Game1.staminaRect, new Rectangle(centerX + 7, centerY - 7, 4, 9), arrow);
    }

    private void DrawOverflowButton(SpriteBatch b, Rectangle bounds, int hiddenCount, bool hovered)
    {
        this.DrawFlatPanel(b, bounds, hovered ? RowHoverColor : RowColor, hovered ? ButtonHoverBorder : ButtonBorder, 2);
        string label = this.FitText(this.translate("companion.quick.more", new { count = hiddenCount }), Game1.tinyFont, bounds.Width - 18);
        Vector2 labelSize = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - labelSize.X) / 2f, bounds.Y + Math.Max(3, (bounds.Height - labelSize.Y) / 2f)),
            IconDark);
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
            new Rectangle(bounds.X + 5, bounds.Y + 5, Math.Max(1, bounds.Width - 10), Math.Max(1, bounds.Height - 10)),
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

    private void DrawPortrait(SpriteBatch b, NPC? npc, Rectangle bounds)
    {
        if (npc is not null)
        {
            try
            {
                Texture2D portrait = Game1.content.Load<Texture2D>($"Portraits/{npc.Name}");
                b.Draw(portrait, new Rectangle(bounds.X + 5, bounds.Y + 5, bounds.Width - 10, bounds.Height - 10), new Rectangle(0, 0, 64, 64), Color.White);
                return;
            }
            catch
            {
                // Custom actors and pets may not provide a portrait asset.
            }
        }

        if (npc?.Sprite?.Texture is not null)
            b.Draw(npc.Sprite.Texture, new Rectangle(bounds.X + 13, bounds.Y + 6, 32, 48), npc.Sprite.SourceRect, Color.White);
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

    private readonly record struct QuickHudRow(
        SquadMemberState Member,
        Rectangle Bounds,
        Rectangle Portrait,
        Rectangle WorkButton,
        Rectangle FollowButton,
        Rectangle ActionArea,
        Rectangle Indicator);

    private readonly record struct QuickHudLayout(
        int RowWidth,
        int RowHeight,
        int PortraitSize,
        int ActionButtonWidth,
        bool ShowText);

    private enum QuickHudAction
    {
        Work,
        Follow
    }
}
