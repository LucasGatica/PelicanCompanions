using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

/// <summary>A small, grouped companion dock drawn in HUD coordinates.</summary>
internal sealed class CompanionQuickHud : IClickableMenu
{
    private const int DetailedDockWidth = 260;
    private const int CompactDockWidth = 158;
    private const int DetailedRowHeight = 70;
    private const int CompactRowHeight = 54;
    private const int DetailedPortraitSize = 48;
    private const int CompactPortraitSize = 40;
    private const int DetailedActionButtonWidth = 58;
    private const int CompactActionButtonWidth = 28;
    private const int DetailedActionButtonHeight = 25;
    private const int CompactActionButtonHeight = 22;
    private const int DetailedHeaderHeight = 32;
    private const int CompactHeaderHeight = 27;
    private const int DockPadding = 7;
    private const int HeaderGap = 5;
    private const int RowGap = 4;
    private const int ActionButtonGap = 4;
    private const int OverflowButtonHeight = 26;
    private const int EdgeMargin = 12;
    private const int PortraitRetryTicks = 600;

    private static readonly Color DockFill = new(229, 204, 169, 245);
    private static readonly Color DockBorder = new(76, 49, 31);
    private static readonly Color DockShadow = Color.Black * 0.34f;
    private static readonly Color HeaderFill = new(69, 96, 67);
    private static readonly Color HeaderBorder = new(47, 65, 45);
    private static readonly Color HeaderTextColor = new(255, 244, 213);
    private static readonly Color HeaderAccent = new(224, 178, 78);
    private static readonly Color RowFill = new(252, 240, 216);
    private static readonly Color RowHoverFill = new(255, 249, 232);
    private static readonly Color RowBorder = new(117, 82, 53);
    private static readonly Color RowHoverBorder = new(218, 165, 69);
    private static readonly Color RowShadow = Color.Black * 0.18f;
    private static readonly Color PortraitFill = new(255, 247, 226);
    private static readonly Color PortraitInnerBorder = new(181, 137, 83);
    private static readonly Color WorkActive = new(74, 139, 83);
    private static readonly Color WorkAutonomous = new(198, 137, 50);
    private static readonly Color WorkIdle = new(225, 209, 177);
    private static readonly Color RecallColor = new(68, 122, 177);
    private static readonly Color ButtonBorder = new(83, 59, 40);
    private static readonly Color ButtonHoverBorder = new(255, 239, 174);
    private static readonly Color ButtonTextColor = new(69, 45, 29);
    private static readonly Color ButtonLightTextColor = new(255, 247, 218);
    private static readonly Color TextColor = new(69, 45, 29);
    private static readonly Color MutedTextColor = new(101, 76, 55);
    private static readonly Color LevelBadgeFill = new(238, 220, 181);
    private static readonly Color LevelBadgeBorder = new(151, 108, 62);
    private static readonly Color WaitingIndicator = new(221, 163, 72);
    private static readonly Color WorkingIndicator = new(73, 150, 88);
    private static readonly Color FollowingIndicator = new(64, 132, 196);
    private static readonly Color WarningIndicator = new(193, 79, 64);

    private readonly Func<IReadOnlyList<SquadMemberState>> getMembers;
    private readonly Func<string, NPC?> getNpc;
    private readonly Func<string, object?, string> translate;
    private readonly Func<SquadMemberState, string> getStatusText;
    private readonly Func<SquadMemberState, bool> isWorkActive;
    private readonly Func<CompanionQuickHudMode> getMode;
    private readonly Func<CompanionQuickHudSide> getSide;
    private readonly Func<int> getMaxVisibleRows;
    private readonly Func<int> getInventorySlotCount;
    private readonly Action<SquadMemberState> toggleWork;
    private readonly Action<SquadMemberState> follow;
    private readonly Action<SquadMemberState> openPanel;
    private readonly Dictionary<string, PortraitCacheEntry> portraitCache = new(StringComparer.OrdinalIgnoreCase);

    public CompanionQuickHud(
        Func<IReadOnlyList<SquadMemberState>> getMembers,
        Func<string, NPC?> getNpc,
        Func<string, object?, string> translate,
        Func<SquadMemberState, string> getStatusText,
        Func<SquadMemberState, bool> isWorkActive,
        Func<CompanionQuickHudMode> getMode,
        Func<CompanionQuickHudSide> getSide,
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
        this.getSide = getSide;
        this.getMaxVisibleRows = getMaxVisibleRows;
        this.getInventorySlotCount = getInventorySlotCount;
        this.toggleWork = toggleWork;
        this.follow = follow;
        this.openPanel = openPanel;
    }

    public void Draw(SpriteBatch b)
    {
        IReadOnlyList<SquadMemberState> allMembers = this.getMembers();
        if (allMembers.Count == 0)
            return;

        QuickHudLayout layout = this.GetLayout();
        int visibleCount = this.GetVisibleRowCount(allMembers.Count, layout);
        int hiddenCount = Math.Max(0, allMembers.Count - visibleCount);
        bool hasOverflow = hiddenCount > 0;
        Rectangle dock = this.GetDockBounds(visibleCount, hasOverflow, layout);
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        string hoverText = "";

        Rectangle header = this.GetHeaderBounds(dock, layout);
        this.DrawDock(b, dock);
        this.DrawHeader(b, header, allMembers.Count, header.Contains(mouse));
        if (header.Contains(mouse))
            hoverText = this.translate("companion.quick.header_hover", null);
        foreach (QuickHudRow row in this.BuildRows(allMembers, visibleCount, dock, layout))
        {
            SquadMemberState member = row.Member;
            bool directWork = this.isWorkActive(member);
            bool autonomousWork = !directWork && member.CurrentActivityKey == "companion.status.working";
            bool rowHovered = row.Bounds.Contains(mouse);
            Color indicatorColor = this.GetIndicatorColor(member, directWork);

            this.DrawMemberCard(b, row.Bounds, rowHovered, indicatorColor);
            this.DrawStatusIndicator(b, row.Indicator, indicatorColor);
            this.DrawPortraitFrame(b, row.Portrait, indicatorColor);
            this.DrawPortrait(b, this.getNpc(member.NpcName), row.Portrait);

            this.DrawIconButton(
                b,
                row.WorkButton,
                QuickHudAction.Work,
                directWork ? WorkVisualState.Direct : autonomousWork ? WorkVisualState.Autonomous : WorkVisualState.Idle,
                row.WorkButton.Contains(mouse),
                layout.ShowButtonLabels);
            this.DrawIconButton(
                b,
                row.FollowButton,
                QuickHudAction.Follow,
                WorkVisualState.Idle,
                row.FollowButton.Contains(mouse),
                layout.ShowButtonLabels);

            if (layout.ShowText)
                this.DrawMemberText(b, row, member);

            if (row.WorkButton.Contains(mouse))
            {
                hoverText = autonomousWork
                    ? this.translate("companion.quick.configured_autonomy_hover", new
                    {
                        npc = member.DisplayName,
                        specialty = this.translate($"companion.specialty.{member.PreferredWorkSpecialty}", null)
                    })
                    : this.translate(
                        directWork ? "companion.quick.work_stop_hover" : "companion.quick.work_hover",
                        new
                        {
                            npc = member.DisplayName,
                            specialty = this.translate($"companion.specialty.{member.PreferredWorkSpecialty}", null)
                        });
            }
            else if (row.FollowButton.Contains(mouse))
            {
                hoverText = this.translate("companion.quick.follow_hover", new { npc = member.DisplayName });
            }
            else if (rowHovered)
            {
                hoverText = this.translate("companion.quick.panel_hover", new
                {
                    npc = member.DisplayName,
                    status = this.getStatusText(member)
                });
                if (this.IsInventoryFull(member))
                    hoverText += Environment.NewLine + this.translate("companion.quick.inventory_full", null);
            }
        }

        if (hasOverflow)
        {
            Rectangle overflow = this.GetOverflowButtonBounds(dock, layout, visibleCount);
            this.DrawOverflowButton(b, overflow, hiddenCount, overflow.Contains(mouse));
            if (overflow.Contains(mouse))
                hoverText = this.translate("companion.quick.more_hover", new { count = hiddenCount });
        }

        if (!string.IsNullOrWhiteSpace(hoverText))
            drawHoverText(b, hoverText, Game1.smallFont);
    }

    public bool TryHandleClick(Vector2 screenPixels)
    {
        IReadOnlyList<SquadMemberState> allMembers = this.getMembers();
        if (allMembers.Count == 0)
            return false;

        QuickHudLayout layout = this.GetLayout();
        int visibleCount = this.GetVisibleRowCount(allMembers.Count, layout);
        bool hasOverflow = visibleCount < allMembers.Count;
        Rectangle dock = this.GetDockBounds(visibleCount, hasOverflow, layout);
        Point point = screenPixels.ToPoint();

        foreach (QuickHudRow row in this.BuildRows(allMembers, visibleCount, dock, layout))
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

            if (row.Bounds.Contains(point))
            {
                this.openPanel(row.Member);
                Game1.playSound("smallSelect");
                return true;
            }
        }

        if (this.GetHeaderBounds(dock, layout).Contains(point))
        {
            this.openPanel(allMembers[0]);
            Game1.playSound("smallSelect");
            return true;
        }

        if (hasOverflow && this.GetOverflowButtonBounds(dock, layout, visibleCount).Contains(point))
        {
            this.openPanel(allMembers[visibleCount]);
            Game1.playSound("smallSelect");
            return true;
        }

        // Padding around the dock isn't interactive, so clicks never disappear into a dead zone.
        return false;
    }

    private QuickHudLayout GetLayout()
    {
        bool detailed = this.getMode() == CompanionQuickHudMode.Detailed && Game1.uiViewport.Width >= 420;
        int desiredWidth = detailed ? DetailedDockWidth : CompactDockWidth;
        int width = Math.Min(desiredWidth, Math.Max(1, Game1.uiViewport.Width - EdgeMargin * 2));
        bool showText = detailed && width >= 210;
        return detailed
            ? new QuickHudLayout(
                width,
                DetailedRowHeight,
                DetailedPortraitSize,
                DetailedActionButtonWidth,
                DetailedActionButtonHeight,
                DetailedHeaderHeight,
                showText,
                ShowButtonLabels: true)
            : new QuickHudLayout(
                width,
                CompactRowHeight,
                CompactPortraitSize,
                CompactActionButtonWidth,
                CompactActionButtonHeight,
                CompactHeaderHeight,
                ShowText: false,
                ShowButtonLabels: false);
    }

    private int GetVisibleRowCount(int totalCount, QuickHudLayout layout)
    {
        int configured = Math.Clamp(this.getMaxVisibleRows(), 1, 12);
        int topSafe = Game1.uiViewport.Height >= 360 ? 68 : 8;
        int bottomSafe = Game1.uiViewport.Height >= 360 ? 92 : 8;
        int available = Math.Max(
            layout.RowHeight + DockPadding * 2 + layout.HeaderHeight + HeaderGap,
            Game1.uiViewport.Height - topSafe - bottomSafe);
        int rowsAvailable = Math.Max(
            layout.RowHeight,
            available - DockPadding * 2 - layout.HeaderHeight - HeaderGap);
        int capacityWithoutOverflow = Math.Max(1, (rowsAvailable + RowGap) / (layout.RowHeight + RowGap));
        int visible = Math.Min(Math.Min(configured, totalCount), capacityWithoutOverflow);
        if (visible < totalCount)
        {
            int reserved = OverflowButtonHeight + RowGap;
            visible = Math.Max(1, (rowsAvailable - reserved + RowGap) / (layout.RowHeight + RowGap));
            visible = Math.Min(visible, Math.Min(configured, totalCount));
        }

        return visible;
    }

    private Rectangle GetDockBounds(int visibleCount, bool hasOverflow, QuickHudLayout layout)
    {
        int rowsHeight = visibleCount * layout.RowHeight + Math.Max(0, visibleCount - 1) * RowGap;
        int height = DockPadding * 2
            + layout.HeaderHeight
            + HeaderGap
            + rowsHeight
            + (hasOverflow ? RowGap + OverflowButtonHeight : 0);
        int topSafe = Game1.uiViewport.Height >= 360 ? 68 : 8;
        int bottomSafe = Game1.uiViewport.Height >= 360 ? 92 : 8;
        int idealY = (Game1.uiViewport.Height - height) / 2;
        int maxY = Math.Max(topSafe, Game1.uiViewport.Height - bottomSafe - height);
        int y = Math.Clamp(idealY, topSafe, maxY);
        int maxX = Math.Max(0, Game1.uiViewport.Width - layout.DockWidth);
        int x = this.getSide() == CompanionQuickHudSide.Right
            ? Math.Max(0, maxX - EdgeMargin)
            : Math.Min(EdgeMargin, maxX);
        return new Rectangle(x, y, layout.DockWidth, height);
    }

    private Rectangle GetHeaderBounds(Rectangle dock, QuickHudLayout layout)
    {
        return new Rectangle(
            dock.X + DockPadding,
            dock.Y + DockPadding,
            Math.Max(1, dock.Width - DockPadding * 2),
            layout.HeaderHeight);
    }

    private IEnumerable<QuickHudRow> BuildRows(
        IReadOnlyList<SquadMemberState> members,
        int visibleCount,
        Rectangle dock,
        QuickHudLayout layout)
    {
        int rowWidth = Math.Max(1, dock.Width - DockPadding * 2);
        for (int i = 0; i < visibleCount; i++)
        {
            int y = dock.Y + DockPadding + layout.HeaderHeight + HeaderGap + i * (layout.RowHeight + RowGap);
            Rectangle bounds = new(dock.X + DockPadding, y, rowWidth, layout.RowHeight);
            int portraitSize = Math.Min(layout.PortraitSize, Math.Max(1, bounds.Height - 8));
            Rectangle portrait = new(bounds.X + 11, bounds.Y + (bounds.Height - portraitSize) / 2, portraitSize, portraitSize);

            int actionsHeight = layout.ActionButtonHeight * 2 + ActionButtonGap;
            int actionX = bounds.Right - 8 - layout.ActionButtonWidth;
            int actionY = bounds.Y + (bounds.Height - actionsHeight) / 2;
            Rectangle work = new(actionX, actionY, layout.ActionButtonWidth, layout.ActionButtonHeight);
            Rectangle follow = new(actionX, work.Bottom + ActionButtonGap, layout.ActionButtonWidth, layout.ActionButtonHeight);
            Rectangle indicator = new(bounds.X + 3, bounds.Y + 9, 4, Math.Max(1, bounds.Height - 18));
            yield return new QuickHudRow(members[i], bounds, portrait, work, follow, indicator);
        }
    }

    private Rectangle GetOverflowButtonBounds(Rectangle dock, QuickHudLayout layout, int visibleCount)
    {
        int rowsHeight = visibleCount * layout.RowHeight + Math.Max(0, visibleCount - 1) * RowGap;
        return new Rectangle(
            dock.X + DockPadding,
            dock.Y + DockPadding + layout.HeaderHeight + HeaderGap + rowsHeight + RowGap,
            Math.Max(1, dock.Width - DockPadding * 2),
            OverflowButtonHeight);
    }

    private void DrawMemberText(SpriteBatch b, QuickHudRow row, SquadMemberState member)
    {
        int textX = row.Portrait.Right + 9;
        int textRight = row.WorkButton.X - 8;
        int width = Math.Max(1, textRight - textX);
        string name = FitText(member.DisplayName, Game1.tinyFont, width);
        string status = FitText(this.getStatusText(member), Game1.tinyFont, width);

        Utility.drawTextWithShadow(b, name, Game1.tinyFont, new Vector2(textX, row.Bounds.Y + 8), TextColor);
        Utility.drawTextWithShadow(b, status, Game1.tinyFont, new Vector2(textX, row.Bounds.Y + 27), MutedTextColor);

        string level = FitText(this.translate("companion.quick.level_short", new { level = member.Level }), Game1.tinyFont, width);
        int levelWidth = Math.Clamp((int)Game1.tinyFont.MeasureString(level).X + 12, 34, width);
        Rectangle levelBadge = new(textX, row.Bounds.Bottom - 22, levelWidth, 18);
        this.DrawMiniBadge(b, levelBadge, level, LevelBadgeFill, LevelBadgeBorder);

        if (this.IsInventoryFull(member))
        {
            Rectangle full = new(Math.Min(textRight - 18, levelBadge.Right + 5), levelBadge.Y, 18, 18);
            this.DrawMiniBadge(b, full, "!", WarningIndicator, new Color(123, 49, 42), ButtonLightTextColor);
        }
    }

    private bool IsInventoryFull(SquadMemberState member)
    {
        return member.Inventory.Count >= Math.Max(1, this.getInventorySlotCount());
    }

    private Color GetIndicatorColor(SquadMemberState member, bool directWork)
    {
        if (member.CurrentActivityKey == "companion.status.stuck"
            || member.LastFailureReasonKey == "companion.task_failure.npc_missing")
            return WarningIndicator;
        if (member.CurrentActivityKey == "companion.status.moving_to_wait")
            return FollowingIndicator;
        if (directWork || member.CurrentActivityKey == "companion.status.working")
            return WorkingIndicator;
        return member.Mode == CompanionMode.Waiting || member.Mode == CompanionMode.ParkedForDisconnect
            ? WaitingIndicator
            : FollowingIndicator;
    }

    private void DrawIconButton(
        SpriteBatch b,
        Rectangle bounds,
        QuickHudAction action,
        WorkVisualState workState,
        bool hovered,
        bool showLabel)
    {
        Color fill = action == QuickHudAction.Follow
            ? RecallColor
            : workState switch
            {
                WorkVisualState.Direct => WorkActive,
                WorkVisualState.Autonomous => WorkAutonomous,
                _ => WorkIdle
            };
        if (hovered)
            fill = Color.Lerp(fill, Color.White, 0.16f);

        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 1, bounds.Y + 2, bounds.Width, bounds.Height), RowShadow);
        this.DrawFlatPanel(b, bounds, fill, hovered ? ButtonHoverBorder : ButtonBorder, hovered ? 2 : 1);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 3, bounds.Y + 3, Math.Max(1, bounds.Width - 6), 1),
            Color.White * 0.42f);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 3, bounds.Bottom - 4, Math.Max(1, bounds.Width - 6), 1),
            Color.Black * 0.18f);

        Rectangle iconBounds = showLabel
            ? new Rectangle(bounds.X + 4, bounds.Y + 3, 16, Math.Max(1, bounds.Height - 6))
            : bounds;
        if (action == QuickHudAction.Work)
            this.DrawWorkIcon(b, iconBounds, workState != WorkVisualState.Idle);
        else
            this.DrawRecallIcon(b, iconBounds);

        if (!showLabel)
            return;

        string labelKey = action == QuickHudAction.Follow
            ? "companion.quick.follow_short"
            : workState == WorkVisualState.Direct
                ? "companion.quick.stop_short"
                : "companion.quick.work_short";
        int labelX = iconBounds.Right + 1;
        string label = FitText(this.translate(labelKey, null), Game1.tinyFont, Math.Max(1, bounds.Right - labelX - 4));
        Vector2 labelSize = Game1.tinyFont.MeasureString(label);
        Color labelColor = action == QuickHudAction.Work && workState == WorkVisualState.Idle
            ? ButtonTextColor
            : ButtonLightTextColor;
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(labelX, bounds.Y + Math.Max(1, (bounds.Height - labelSize.Y) / 2f)),
            labelColor);
    }

    private void DrawWorkIcon(SpriteBatch b, Rectangle bounds, bool active)
    {
        Color handle = active ? Color.White : new Color(78, 54, 36);
        Color head = active ? new Color(238, 244, 231) : new Color(129, 122, 105);
        int cx = bounds.Center.X;
        int cy = bounds.Center.Y;
        b.Draw(Game1.staminaRect, new Rectangle(cx - 1, cy - 1, 3, Math.Max(6, bounds.Height / 3)), handle);
        b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 6, 11, 3), head);
        b.Draw(Game1.staminaRect, new Rectangle(cx + 3, cy - 5, 3, 5), head);
    }

    private void DrawRecallIcon(SpriteBatch b, Rectangle bounds)
    {
        Color light = new(255, 247, 218);
        Color shadow = new(39, 75, 111);
        int cx = bounds.Center.X;
        int cy = bounds.Center.Y;
        b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 1, 10, 3), shadow);
        b.Draw(Game1.staminaRect, new Rectangle(cx - 5, cy - 3, 10, 3), light);
        b.Draw(Game1.staminaRect, new Rectangle(cx + 2, cy - 6, 3, 9), light);
        b.Draw(Game1.staminaRect, new Rectangle(cx + 5, cy - 3, 3, 5), light);
    }

    private void DrawOverflowButton(SpriteBatch b, Rectangle bounds, int hiddenCount, bool hovered)
    {
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height), RowShadow);
        this.DrawFlatPanel(b, bounds, hovered ? RowHoverFill : RowFill, hovered ? RowHoverBorder : RowBorder, hovered ? 2 : 1);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 4, bounds.Y + 3, Math.Max(1, bounds.Width - 8), 1),
            Color.White * 0.48f);
        string text = FitText(this.translate("companion.quick.more", new { count = hiddenCount }), Game1.tinyFont, bounds.Width - 12);
        Vector2 size = Game1.tinyFont.MeasureString(text);
        Utility.drawTextWithShadow(
            b,
            text,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(2, (bounds.Height - size.Y) / 2f)),
            TextColor);
    }

    private void DrawDock(SpriteBatch b, Rectangle bounds)
    {
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 4, bounds.Y + 5, bounds.Width, bounds.Height),
            DockShadow);
        drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            DockBorder);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 5, bounds.Y + 5, Math.Max(1, bounds.Width - 10), Math.Max(1, bounds.Height - 10)),
            DockFill);
    }

    private void DrawHeader(SpriteBatch b, Rectangle bounds, int memberCount, bool hovered)
    {
        Color fill = hovered ? Color.Lerp(HeaderFill, Color.White, 0.10f) : HeaderFill;
        this.DrawFlatPanel(b, bounds, fill, hovered ? ButtonHoverBorder : HeaderBorder, 2);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 3, bounds.Y + 3, Math.Max(1, bounds.Width - 6), 1),
            Color.White * 0.18f);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 7, bounds.Bottom - 4, Math.Max(1, bounds.Width - 14), 2),
            HeaderAccent * 0.85f);

        int countWidth = Math.Clamp((int)Game1.tinyFont.MeasureString(memberCount.ToString()).X + 14, 25, 36);
        Rectangle countBadge = new(bounds.Right - countWidth - 6, bounds.Y + 5, countWidth, Math.Max(16, bounds.Height - 10));
        this.DrawMiniBadge(
            b,
            countBadge,
            memberCount.ToString(),
            new Color(244, 216, 153),
            new Color(105, 74, 43),
            ButtonTextColor);

        string title = FitText(
            this.translate("companion.panel.title", null),
            Game1.tinyFont,
            Math.Max(1, countBadge.X - bounds.X - 18));
        Vector2 titleSize = Game1.tinyFont.MeasureString(title);
        Utility.drawTextWithShadow(
            b,
            title,
            Game1.tinyFont,
            new Vector2(bounds.X + 9, bounds.Y + Math.Max(1, (bounds.Height - titleSize.Y) / 2f)),
            HeaderTextColor);
    }

    private void DrawMemberCard(SpriteBatch b, Rectangle bounds, bool hovered, Color indicatorColor)
    {
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height),
            RowShadow);
        this.DrawFlatPanel(
            b,
            bounds,
            hovered ? RowHoverFill : RowFill,
            hovered ? RowHoverBorder : RowBorder,
            hovered ? 2 : 1);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 3, bounds.Y + 3, Math.Max(1, bounds.Width - 6), 1),
            Color.White * 0.52f);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 3, bounds.Bottom - 4, Math.Max(1, bounds.Width - 6), 1),
            indicatorColor * 0.28f);
    }

    private void DrawStatusIndicator(SpriteBatch b, Rectangle bounds, Color color)
    {
        this.DrawFlatPanel(b, bounds, color, Color.Black * 0.28f, 1);
        if (bounds.Height > 6)
        {
            b.Draw(
                Game1.staminaRect,
                new Rectangle(bounds.X + 1, bounds.Y + 2, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height / 3)),
                Color.White * 0.28f);
        }
    }

    private void DrawPortraitFrame(SpriteBatch b, Rectangle bounds, Color accent)
    {
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height),
            RowShadow);
        this.DrawFlatPanel(b, bounds, accent, ButtonBorder, 1);
        this.DrawFlatPanel(
            b,
            new Rectangle(bounds.X + 2, bounds.Y + 2, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 4)),
            PortraitFill,
            PortraitInnerBorder,
            1);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 4, bounds.Y + 4, Math.Max(1, bounds.Width - 8), 1),
            Color.White * 0.58f);
    }

    private void DrawMiniBadge(
        SpriteBatch b,
        Rectangle bounds,
        string text,
        Color fill,
        Color border,
        Color? textColor = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        this.DrawFlatPanel(b, bounds, fill, border, 1);
        string label = FitText(text, Game1.tinyFont, Math.Max(1, bounds.Width - 6));
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(0, (bounds.Height - size.Y) / 2f)),
            textColor ?? ButtonTextColor);
    }

    private void DrawFlatPanel(SpriteBatch b, Rectangle bounds, Color fill, Color border, int borderSize)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
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
        Texture2D? portrait = this.GetPortrait(npc);
        if (portrait is not null)
        {
            b.Draw(
                portrait,
                new Rectangle(bounds.X + 4, bounds.Y + 4, Math.Max(1, bounds.Width - 8), Math.Max(1, bounds.Height - 8)),
                new Rectangle(0, 0, 64, 64),
                Color.White);
            return;
        }

        if (npc?.Sprite?.Texture is null)
            return;

        int width = Math.Max(1, Math.Min(bounds.Width - 10, 28));
        int height = Math.Max(1, Math.Min(bounds.Height - 7, 42));
        Rectangle destination = new(bounds.Center.X - width / 2, bounds.Bottom - height - 4, width, height);
        b.Draw(npc.Sprite.Texture, destination, npc.Sprite.SourceRect, Color.White);
    }

    private Texture2D? GetPortrait(NPC? npc)
    {
        if (npc is null)
            return null;

        if (this.portraitCache.TryGetValue(npc.Name, out PortraitCacheEntry cached))
        {
            if (cached.Texture is not null && !cached.Texture.IsDisposed)
                return cached.Texture;
            if (cached.Texture is null && Game1.ticks - cached.CheckedAtTick < PortraitRetryTicks)
                return null;
        }

        try
        {
            Texture2D portrait = Game1.content.Load<Texture2D>($"Portraits/{npc.Name}");
            this.portraitCache[npc.Name] = new PortraitCacheEntry(portrait, Game1.ticks);
            return portrait;
        }
        catch
        {
            this.portraitCache[npc.Name] = new PortraitCacheEntry(null, Game1.ticks);
            return null;
        }
    }

    private static string FitText(string text, SpriteFont font, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
            return "";
        if (font.MeasureString(text).X <= width)
            return text;

        const string suffix = "…";
        if (font.MeasureString(suffix).X > width)
            return "";

        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (font.MeasureString(text[..mid] + suffix).X <= width)
                low = mid;
            else
                high = mid - 1;
        }

        return text[..low] + suffix;
    }

    private readonly record struct PortraitCacheEntry(Texture2D? Texture, int CheckedAtTick);

    private readonly record struct QuickHudRow(
        SquadMemberState Member,
        Rectangle Bounds,
        Rectangle Portrait,
        Rectangle WorkButton,
        Rectangle FollowButton,
        Rectangle Indicator);

    private readonly record struct QuickHudLayout(
        int DockWidth,
        int RowHeight,
        int PortraitSize,
        int ActionButtonWidth,
        int ActionButtonHeight,
        int HeaderHeight,
        bool ShowText,
        bool ShowButtonLabels);

    private enum QuickHudAction
    {
        Work,
        Follow
    }

    private enum WorkVisualState
    {
        Idle,
        Direct,
        Autonomous
    }
}
