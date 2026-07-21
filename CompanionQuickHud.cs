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
    private const int DetailedRowHeight = 72;
    private const int CompactRowHeight = 54;
    private const int DetailedPortraitSize = 50;
    private const int CompactPortraitSize = 40;
    private const int DetailedActionButtonWidth = 34;
    private const int CompactActionButtonWidth = 30;
    private const int DetailedActionButtonHeight = 32;
    private const int CompactActionButtonHeight = 23;
    private const int DetailedHeaderHeight = 36;
    private const int CompactHeaderHeight = 32;
    private const int DockPadding = 7;
    private const int HeaderGap = 5;
    private const int RowGap = 4;
    private const int ActionButtonGap = 4;
    private const int OverflowButtonHeight = 30;
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
            this.DrawPortraitBadges(b, row.Portrait, member);

            this.DrawIconButton(
                b,
                row.WorkButton,
                QuickHudAction.Work,
                directWork ? WorkVisualState.Direct : autonomousWork ? WorkVisualState.Autonomous : WorkVisualState.Idle,
                row.WorkButton.Contains(mouse));
            this.DrawIconButton(
                b,
                row.FollowButton,
                QuickHudAction.Follow,
                WorkVisualState.Idle,
                row.FollowButton.Contains(mouse));

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
        int lineHeight = Math.Max(1, Game1.tinyFont.LineSpacing);
        int headerHeight = Math.Max(
            detailed ? DetailedHeaderHeight : CompactHeaderHeight,
            lineHeight + 10);
        int rowHeight = detailed
            ? Math.Max(DetailedRowHeight, lineHeight * 2 + 14)
            : CompactRowHeight;
        int overflowHeight = Math.Max(OverflowButtonHeight, lineHeight + 8);
        return detailed
            ? new QuickHudLayout(
                width,
                rowHeight,
                DetailedPortraitSize,
                DetailedActionButtonWidth,
                DetailedActionButtonHeight,
                headerHeight,
                overflowHeight,
                showText)
            : new QuickHudLayout(
                width,
                rowHeight,
                CompactPortraitSize,
                CompactActionButtonWidth,
                CompactActionButtonHeight,
                headerHeight,
                overflowHeight,
                ShowText: false);
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
            int reserved = layout.OverflowButtonHeight + RowGap;
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
            + (hasOverflow ? RowGap + layout.OverflowButtonHeight : 0);
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
            layout.OverflowButtonHeight);
    }

    private void DrawMemberText(SpriteBatch b, QuickHudRow row, SquadMemberState member)
    {
        int textX = row.Portrait.Right + 9;
        int textRight = row.WorkButton.X - 8;
        int width = Math.Max(1, textRight - textX);
        string name = FitText(member.DisplayName, Game1.tinyFont, width);
        string status = FitText(this.getStatusText(member), Game1.tinyFont, width);
        int contentHeight = Math.Max(1, Game1.tinyFont.LineSpacing * 2);
        int textY = row.Bounds.Y + Math.Max(4, (row.Bounds.Height - contentHeight) / 2);

        DrawCrispText(b, name, new Vector2(textX, textY), TextColor);
        DrawCrispText(b, status, new Vector2(textX, textY + Game1.tinyFont.LineSpacing), MutedTextColor);
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
        bool hovered)
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

        if (action == QuickHudAction.Work)
            this.DrawWorkIcon(b, bounds, workState != WorkVisualState.Idle);
        else
            this.DrawRecallIcon(b, bounds);
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
        DrawCrispText(
            b,
            text,
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

        const int digitScale = 2;
        const int badgeHeight = 18;
        int chevronX = bounds.Right - 12;
        int countWidth = GetNumberBadgeWidth(memberCount, digitScale);
        Rectangle countBadge = new(
            chevronX - countWidth - 8,
            bounds.Center.Y - badgeHeight / 2,
            countWidth,
            badgeHeight);
        this.DrawNumberBadge(
            b,
            countBadge,
            memberCount,
            digitScale,
            new Color(244, 216, 153),
            new Color(105, 74, 43),
            ButtonTextColor);
        this.DrawChevron(b, chevronX, bounds.Center.Y, HeaderTextColor);

        string title = FitText(
            this.translate("companion.quick.title", null),
            Game1.tinyFont,
            Math.Max(1, countBadge.X - bounds.X - 18));
        Vector2 titleSize = Game1.tinyFont.MeasureString(title);
        DrawCrispText(
            b,
            title,
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

    private void DrawPortraitBadges(SpriteBatch b, Rectangle portrait, SquadMemberState member)
    {
        const int digitScale = 2;
        const int badgeHeight = 16;
        int level = Math.Clamp(member.Level, 0, 99);
        int levelWidth = GetNumberBadgeWidth(level, digitScale);
        Rectangle levelBadge = new(
            portrait.Right - levelWidth - 1,
            portrait.Bottom - badgeHeight - 1,
            levelWidth,
            badgeHeight);
        this.DrawNumberBadge(
            b,
            levelBadge,
            level,
            digitScale,
            LevelBadgeFill,
            LevelBadgeBorder,
            ButtonTextColor);

        if (!this.IsInventoryFull(member))
            return;

        Rectangle alert = new(portrait.Right - 15, portrait.Y + 1, 14, 14);
        this.DrawFlatPanel(b, alert, WarningIndicator, new Color(123, 49, 42), 1);
        b.Draw(Game1.staminaRect, new Rectangle(alert.Center.X - 1, alert.Y + 3, 2, 6), Color.White);
        b.Draw(Game1.staminaRect, new Rectangle(alert.Center.X - 1, alert.Bottom - 4, 2, 2), Color.White);
    }

    private void DrawNumberBadge(
        SpriteBatch b,
        Rectangle bounds,
        int value,
        int pixelScale,
        Color fill,
        Color border,
        Color textColor)
    {
        this.DrawFlatPanel(b, bounds, fill, border, 1);
        string digits = Math.Max(0, value).ToString();
        int digitWidth = 3 * pixelScale;
        int gap = pixelScale;
        int contentWidth = digits.Length * digitWidth + Math.Max(0, digits.Length - 1) * gap;
        int contentHeight = 5 * pixelScale;
        int x = bounds.X + (bounds.Width - contentWidth) / 2;
        int y = bounds.Y + (bounds.Height - contentHeight) / 2;

        foreach (char digit in digits)
        {
            DrawPixelDigit(b, digit, x, y, pixelScale, textColor);
            x += digitWidth + gap;
        }
    }

    private void DrawChevron(SpriteBatch b, int centerX, int centerY, Color color)
    {
        const int pixelSize = 2;
        ReadOnlySpan<Point> pixels = stackalloc Point[]
        {
            new(0, 0),
            new(1, 1),
            new(2, 2),
            new(1, 3),
            new(0, 4)
        };
        int x = centerX - pixelSize * 3 / 2;
        int y = centerY - pixelSize * 5 / 2;
        foreach (Point pixel in pixels)
        {
            b.Draw(
                Game1.staminaRect,
                new Rectangle(x + pixel.X * pixelSize, y + pixel.Y * pixelSize, pixelSize, pixelSize),
                color);
        }
    }

    private static void DrawPixelDigit(SpriteBatch b, char digit, int x, int y, int pixelScale, Color color)
    {
        string pixels = digit switch
        {
            '0' => "111101101101111",
            '1' => "010110010010111",
            '2' => "111001111100111",
            '3' => "111001111001111",
            '4' => "101101111001001",
            '5' => "111100111001111",
            '6' => "111100111101111",
            '7' => "111001010010010",
            '8' => "111101111101111",
            '9' => "111101111001111",
            _ => "000000000000000"
        };

        for (int index = 0; index < pixels.Length; index++)
        {
            if (pixels[index] != '1')
                continue;

            int column = index % 3;
            int row = index / 3;
            b.Draw(
                Game1.staminaRect,
                new Rectangle(x + column * pixelScale, y + row * pixelScale, pixelScale, pixelScale),
                color);
        }
    }

    private static int GetNumberBadgeWidth(int value, int pixelScale)
    {
        int digitCount = Math.Max(0, value).ToString().Length;
        int contentWidth = digitCount * 3 * pixelScale + Math.Max(0, digitCount - 1) * pixelScale;
        return contentWidth + 6;
    }

    private static void DrawCrispText(SpriteBatch b, string text, Vector2 position, Color color)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Vector2 snapped = new(MathF.Round(position.X), MathF.Round(position.Y));
        b.DrawString(Game1.tinyFont, text, snapped, color);
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
        int OverflowButtonHeight,
        bool ShowText);

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
