using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

/// <summary>A small, grouped companion dock drawn in HUD coordinates.</summary>
internal sealed class CompanionQuickHud : IClickableMenu
{
    private const int DetailedDockWidth = 232;
    private const int CompactDockWidth = 142;
    private const int DetailedRowHeight = 58;
    private const int CompactRowHeight = 46;
    private const int DetailedPortraitSize = 40;
    private const int CompactPortraitSize = 34;
    private const int DetailedActionButtonSize = 29;
    private const int CompactActionButtonSize = 24;
    private const int DockPadding = 5;
    private const int RowGap = 2;
    private const int ActionButtonGap = 4;
    private const int OverflowButtonHeight = 24;
    private const int PortraitRetryTicks = 600;

    private static readonly Color DockFill = new(45, 35, 28, 225);
    private static readonly Color DockBorder = new(245, 224, 185);
    private static readonly Color RowFill = new(250, 237, 210);
    private static readonly Color RowHoverFill = new(255, 247, 225);
    private static readonly Color RowBorder = new(111, 79, 51);
    private static readonly Color PortraitFill = new(255, 247, 227);
    private static readonly Color WorkActive = new(78, 151, 88);
    private static readonly Color WorkAutonomous = new(205, 142, 55);
    private static readonly Color WorkIdle = new(218, 202, 170);
    private static readonly Color RecallColor = new(76, 132, 188);
    private static readonly Color ButtonBorder = new(83, 59, 40);
    private static readonly Color ButtonHoverBorder = Color.White;
    private static readonly Color TextColor = new(65, 43, 28);
    private static readonly Color MutedTextColor = new(101, 75, 53);
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

        this.DrawPanel(b, dock, DockFill, DockBorder);
        foreach (QuickHudRow row in this.BuildRows(allMembers, visibleCount, dock, layout))
        {
            SquadMemberState member = row.Member;
            bool directWork = this.isWorkActive(member);
            bool autonomousWork = !directWork && member.CurrentActivityKey == "companion.status.working";
            bool rowHovered = row.Bounds.Contains(mouse);

            this.DrawFlatPanel(b, row.Bounds, rowHovered ? RowHoverFill : RowFill, RowBorder, 1);
            b.Draw(Game1.staminaRect, row.Indicator, this.GetIndicatorColor(member, directWork));
            this.DrawFlatPanel(b, row.Portrait, PortraitFill, RowBorder, 1);
            this.DrawPortrait(b, this.getNpc(member.NpcName), row.Portrait);

            this.DrawIconButton(
                b,
                row.WorkButton,
                QuickHudAction.Work,
                directWork ? WorkVisualState.Direct : autonomousWork ? WorkVisualState.Autonomous : WorkVisualState.Idle,
                row.WorkButton.Contains(mouse));
            this.DrawIconButton(b, row.FollowButton, QuickHudAction.Follow, WorkVisualState.Idle, row.FollowButton.Contains(mouse));

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
        bool detailed = this.getMode() == CompanionQuickHudMode.Detailed && Game1.uiViewport.Width >= 300;
        int desiredWidth = detailed ? DetailedDockWidth : CompactDockWidth;
        int width = Math.Min(desiredWidth, Math.Max(1, Game1.uiViewport.Width - 16));
        bool showText = detailed && width >= 190;
        return detailed
            ? new QuickHudLayout(width, DetailedRowHeight, DetailedPortraitSize, DetailedActionButtonSize, showText)
            : new QuickHudLayout(width, CompactRowHeight, CompactPortraitSize, CompactActionButtonSize, false);
    }

    private int GetVisibleRowCount(int totalCount, QuickHudLayout layout)
    {
        int configured = Math.Clamp(this.getMaxVisibleRows(), 1, 12);
        int topSafe = Game1.uiViewport.Height >= 360 ? 68 : 8;
        int bottomSafe = Game1.uiViewport.Height >= 360 ? 92 : 8;
        int available = Math.Max(layout.RowHeight + DockPadding * 2, Game1.uiViewport.Height - topSafe - bottomSafe);
        int capacityWithoutOverflow = Math.Max(1, (available - DockPadding * 2 + RowGap) / (layout.RowHeight + RowGap));
        int visible = Math.Min(Math.Min(configured, totalCount), capacityWithoutOverflow);
        if (visible < totalCount)
        {
            int reserved = OverflowButtonHeight + RowGap;
            visible = Math.Max(1, (available - DockPadding * 2 - reserved + RowGap) / (layout.RowHeight + RowGap));
            visible = Math.Min(visible, Math.Min(configured, totalCount));
        }

        return visible;
    }

    private Rectangle GetDockBounds(int visibleCount, bool hasOverflow, QuickHudLayout layout)
    {
        int rowsHeight = visibleCount * layout.RowHeight + Math.Max(0, visibleCount - 1) * RowGap;
        int height = DockPadding * 2 + rowsHeight + (hasOverflow ? RowGap + OverflowButtonHeight : 0);
        int topSafe = Game1.uiViewport.Height >= 360 ? 68 : 8;
        int bottomSafe = Game1.uiViewport.Height >= 360 ? 92 : 8;
        int idealY = (Game1.uiViewport.Height - height) / 2;
        int maxY = Math.Max(topSafe, Game1.uiViewport.Height - bottomSafe - height);
        int y = Math.Clamp(idealY, topSafe, maxY);
        int x = this.getSide() == CompanionQuickHudSide.Right
            ? Math.Max(0, Game1.uiViewport.Width - layout.DockWidth - 8)
            : Math.Max(0, Math.Min(8, Game1.uiViewport.Width - layout.DockWidth));
        return new Rectangle(x, y, layout.DockWidth, height);
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
            int y = dock.Y + DockPadding + i * (layout.RowHeight + RowGap);
            Rectangle bounds = new(dock.X + DockPadding, y, rowWidth, layout.RowHeight);
            int portraitSize = Math.Min(layout.PortraitSize, Math.Max(1, bounds.Height - 8));
            Rectangle portrait = new(bounds.X + 7, bounds.Y + (bounds.Height - portraitSize) / 2, portraitSize, portraitSize);

            int actionsWidth = layout.ActionButtonSize * 2 + ActionButtonGap;
            int actionX = bounds.Right - 6 - actionsWidth;
            int actionY = bounds.Y + (bounds.Height - layout.ActionButtonSize) / 2;
            Rectangle work = new(actionX, actionY, layout.ActionButtonSize, layout.ActionButtonSize);
            Rectangle follow = new(work.Right + ActionButtonGap, actionY, layout.ActionButtonSize, layout.ActionButtonSize);
            Rectangle indicator = new(bounds.X + 2, bounds.Y + 6, 3, Math.Max(1, bounds.Height - 12));
            yield return new QuickHudRow(members[i], bounds, portrait, work, follow, indicator);
        }
    }

    private Rectangle GetOverflowButtonBounds(Rectangle dock, QuickHudLayout layout, int visibleCount)
    {
        int rowsHeight = visibleCount * layout.RowHeight + Math.Max(0, visibleCount - 1) * RowGap;
        return new Rectangle(
            dock.X + DockPadding,
            dock.Y + DockPadding + rowsHeight + RowGap,
            Math.Max(1, dock.Width - DockPadding * 2),
            OverflowButtonHeight);
    }

    private void DrawMemberText(SpriteBatch b, QuickHudRow row, SquadMemberState member)
    {
        int textX = row.Portrait.Right + 8;
        int textRight = row.WorkButton.X - 7;
        int width = Math.Max(1, textRight - textX);
        string name = FitText(member.DisplayName, Game1.tinyFont, width);
        string status = FitText(this.getStatusText(member), Game1.tinyFont, width);

        Utility.drawTextWithShadow(b, name, Game1.tinyFont, new Vector2(textX, row.Bounds.Y + 7), TextColor);
        Utility.drawTextWithShadow(b, status, Game1.tinyFont, new Vector2(textX, row.Bounds.Y + 25), MutedTextColor);

        string level = FitText(this.translate("companion.quick.level_short", new { level = member.Level }), Game1.tinyFont, width);
        Utility.drawTextWithShadow(b, level, Game1.tinyFont, new Vector2(textX, row.Bounds.Bottom - 17), TextColor);

        if (member.Inventory.Count >= Math.Max(1, this.getInventorySlotCount()))
        {
            Rectangle full = new(textRight - 8, row.Bounds.Bottom - 14, 8, 8);
            b.Draw(Game1.staminaRect, full, WarningIndicator);
        }
    }

    private Color GetIndicatorColor(SquadMemberState member, bool directWork)
    {
        if (member.CurrentActivityKey == "companion.status.stuck"
            || member.LastFailureReasonKey == "companion.task_failure.npc_missing")
            return WarningIndicator;
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
        this.DrawFlatPanel(b, bounds, fill, hovered ? ButtonHoverBorder : ButtonBorder, hovered ? 2 : 1);
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
        b.Draw(Game1.staminaRect, new Rectangle(cx - 2, cy - 2, 4, Math.Max(7, bounds.Height / 3)), handle);
        b.Draw(Game1.staminaRect, new Rectangle(cx - bounds.Width / 4, cy - bounds.Height / 4, Math.Max(8, bounds.Width / 2), 4), head);
        b.Draw(Game1.staminaRect, new Rectangle(cx + bounds.Width / 5, cy - bounds.Height / 4, 3, Math.Max(5, bounds.Height / 4)), head);
    }

    private void DrawRecallIcon(SpriteBatch b, Rectangle bounds)
    {
        Color light = new(255, 247, 218);
        Color shadow = new(39, 75, 111);
        int cx = bounds.Center.X;
        int cy = bounds.Center.Y;
        b.Draw(Game1.staminaRect, new Rectangle(cx - 7, cy - 2, 12, 4), shadow);
        b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 4, 12, 4), light);
        b.Draw(Game1.staminaRect, new Rectangle(cx + 3, cy - 7, 4, 11), light);
        b.Draw(Game1.staminaRect, new Rectangle(cx + 6, cy - 4, 3, 6), light);
    }

    private void DrawOverflowButton(SpriteBatch b, Rectangle bounds, int hiddenCount, bool hovered)
    {
        this.DrawFlatPanel(b, bounds, hovered ? RowHoverFill : RowFill, hovered ? ButtonHoverBorder : RowBorder, 1);
        string text = FitText(this.translate("companion.quick.more", new { count = hiddenCount }), Game1.tinyFont, bounds.Width - 12);
        Vector2 size = Game1.tinyFont.MeasureString(text);
        Utility.drawTextWithShadow(
            b,
            text,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(2, (bounds.Height - size.Y) / 2f)),
            TextColor);
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
            new Rectangle(bounds.X + 4, bounds.Y + 4, Math.Max(1, bounds.Width - 8), Math.Max(1, bounds.Height - 8)),
            fill);
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
                new Rectangle(bounds.X + 3, bounds.Y + 3, Math.Max(1, bounds.Width - 6), Math.Max(1, bounds.Height - 6)),
                new Rectangle(0, 0, 64, 64),
                Color.White);
            return;
        }

        if (npc?.Sprite?.Texture is null)
            return;

        int width = Math.Max(1, Math.Min(bounds.Width - 6, 24));
        int height = Math.Max(1, Math.Min(bounds.Height - 4, 36));
        Rectangle destination = new(bounds.Center.X - width / 2, bounds.Bottom - height - 2, width, height);
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
        int ActionButtonSize,
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
