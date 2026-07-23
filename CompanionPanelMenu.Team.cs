using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace PelicanCompanions;

/// <summary>The interactive target under a point in the team dashboard.</summary>
internal enum CompanionTeamDashboardHitKind
{
    None,
    Member,
    StopAllWork,
    DepositAllCargo,
    FollowAllRoutines
}

/// <summary>A stable hit-test result which can be routed by the main menu input handlers.</summary>
internal readonly record struct CompanionTeamDashboardHit(
    CompanionTeamDashboardHitKind Kind,
    string NpcName = "");

internal sealed partial class CompanionPanelMenu
{
    private const int TeamDashboardCardGap = 6;
    private const int TeamDashboardWideCardHeight = 76;
    private const int TeamDashboardCompactCardHeight = 106;
    private const int TeamDashboardActionGap = 6;

    private readonly List<(Rectangle Bounds, string NpcName)> teamDashboardMemberCards = new();
    private Func<SquadMemberState, CompanionTeamMemberView>? getTeamMemberView;
    private Func<bool>? stopAllCompanionWork;
    private Func<bool>? depositAllCompanionCargo;
    private Func<bool>? followAllCompanionRoutines;
    private Rectangle teamDashboardListArea;
    private Rectangle teamDashboardStopAllButton;
    private Rectangle teamDashboardDepositAllButton;
    private Rectangle teamDashboardFollowAllButton;
    private int teamDashboardFirstVisibleIndex;
    private int teamDashboardMaximumFirstVisibleIndex;

    /// <summary>
    /// Attach the gameplay bridge without expanding the already-large menu constructor.
    /// The caller can configure the dashboard immediately after constructing the menu.
    /// </summary>
    internal void ConfigureTeamDashboard(
        Func<SquadMemberState, CompanionTeamMemberView> getMemberView,
        Func<bool> stopAllWork,
        Func<bool> depositAllCargo,
        Func<bool> followAllRoutines)
    {
        this.getTeamMemberView = getMemberView ?? throw new ArgumentNullException(nameof(getMemberView));
        this.stopAllCompanionWork = stopAllWork ?? throw new ArgumentNullException(nameof(stopAllWork));
        this.depositAllCompanionCargo = depositAllCargo ?? throw new ArgumentNullException(nameof(depositAllCargo));
        this.followAllCompanionRoutines = followAllRoutines ?? throw new ArgumentNullException(nameof(followAllRoutines));
    }

    internal bool IsTeamDashboardConfigured => this.getTeamMemberView is not null
        && this.stopAllCompanionWork is not null
        && this.depositAllCompanionCargo is not null
        && this.followAllCompanionRoutines is not null;

    /// <summary>Clear frame-local hitboxes before redrawing the dashboard.</summary>
    internal void ResetTeamDashboardGeometry()
    {
        this.teamDashboardMemberCards.Clear();
        this.teamDashboardListArea = Rectangle.Empty;
        this.teamDashboardStopAllButton = Rectangle.Empty;
        this.teamDashboardDepositAllButton = Rectangle.Empty;
        this.teamDashboardFollowAllButton = Rectangle.Empty;
        this.teamDashboardMaximumFirstVisibleIndex = 0;
    }

    /// <summary>Draw the responsive team cards and the three bulk command buttons.</summary>
    internal void DrawTeamDashboard(SpriteBatch b, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(b);
        this.ResetTeamDashboardGeometry();

        List<SquadMemberState> members = this.getMembers().ToList();
        if (!this.IsTeamDashboardConfigured || area.Width < 1 || area.Height < 1)
        {
            DrawCenteredPanelText(
                b,
                this.TeamDashboardText(
                    "companion.team.unavailable",
                    "Team dashboard unavailable"),
                Game1.tinyFont,
                area,
                MutedTextColor,
                PanelTextScale,
                4,
                4);
            return;
        }

        bool compact = area.Width < 650;
        int actionHeight = area.Height < 230 ? 28 : 38;
        int actionTop = Math.Max(area.Y, area.Bottom - actionHeight);
        int actionWidth = Math.Max(
            1,
            (area.Width - TeamDashboardActionGap * 2) / 3);
        this.teamDashboardStopAllButton = new Rectangle(
            area.X,
            actionTop,
            actionWidth,
            actionHeight);
        this.teamDashboardDepositAllButton = new Rectangle(
            this.teamDashboardStopAllButton.Right + TeamDashboardActionGap,
            actionTop,
            actionWidth,
            actionHeight);
        this.teamDashboardFollowAllButton = new Rectangle(
            this.teamDashboardDepositAllButton.Right + TeamDashboardActionGap,
            actionTop,
            Math.Max(1, area.Right - this.teamDashboardDepositAllButton.Right - TeamDashboardActionGap),
            actionHeight);

        int summaryHeight = area.Height >= 250 ? 34 : 0;
        if (summaryHeight > 0)
        {
            int fullCount = 0;
            int blockedCount = 0;
            foreach (SquadMemberState member in members)
            {
                CompanionTeamMemberView view = this.getTeamMemberView!(member);
                if (view.InventoryFull)
                    fullCount++;
                if (!string.IsNullOrWhiteSpace(view.BlockReason))
                    blockedCount++;
            }

            this.DrawTeamDashboardSummary(
                b,
                new Rectangle(area.X, area.Y, area.Width, summaryHeight),
                members.Count,
                fullCount,
                blockedCount);
        }

        int listTop = area.Y + summaryHeight + (summaryHeight > 0 ? 7 : 0);
        this.teamDashboardListArea = new Rectangle(
            area.X,
            listTop,
            area.Width,
            Math.Max(1, actionTop - listTop - 7));

        if (members.Count == 0)
        {
            DrawCenteredPanelText(
                b,
                this.translate("companion.panel.empty", null),
                Game1.tinyFont,
                this.teamDashboardListArea,
                MutedTextColor,
                PanelTextScale,
                6,
                6);
        }
        else
        {
            int requestedCardHeight = compact
                ? TeamDashboardCompactCardHeight
                : TeamDashboardWideCardHeight;
            int cardHeight = Math.Min(
                requestedCardHeight,
                Math.Max(1, this.teamDashboardListArea.Height));
            int stride = cardHeight + TeamDashboardCardGap;
            int visibleCount = Math.Max(
                1,
                (this.teamDashboardListArea.Height + TeamDashboardCardGap) / Math.Max(1, stride));
            this.teamDashboardMaximumFirstVisibleIndex = Math.Max(0, members.Count - visibleCount);
            this.teamDashboardFirstVisibleIndex = Math.Clamp(
                this.teamDashboardFirstVisibleIndex,
                0,
                this.teamDashboardMaximumFirstVisibleIndex);

            int endIndex = Math.Min(
                members.Count,
                this.teamDashboardFirstVisibleIndex + visibleCount);
            for (int index = this.teamDashboardFirstVisibleIndex; index < endIndex; index++)
            {
                int visibleIndex = index - this.teamDashboardFirstVisibleIndex;
                int y = this.teamDashboardListArea.Y + visibleIndex * stride;
                Rectangle card = new(
                    this.teamDashboardListArea.X,
                    y,
                    this.teamDashboardListArea.Width - (this.teamDashboardMaximumFirstVisibleIndex > 0 ? 8 : 0),
                    cardHeight);
                if (card.Bottom > this.teamDashboardListArea.Bottom)
                    break;

                SquadMemberState member = members[index];
                CompanionTeamMemberView view = this.getTeamMemberView!(member);
                this.DrawTeamDashboardMemberCard(b, card, member, view, compact);
                this.teamDashboardMemberCards.Add((card, member.NpcName));
            }

            this.DrawTeamDashboardScrollbar(
                b,
                this.teamDashboardListArea,
                members.Count,
                visibleCount);
        }

        this.DrawButton(
            b,
            this.teamDashboardStopAllButton,
            this.TeamDashboardText("companion.team.stop_all", "Stop all"),
            active: false,
            danger: true);
        this.DrawButton(
            b,
            this.teamDashboardDepositAllButton,
            this.TeamDashboardText("companion.team.deposit_all", "Deposit all"),
            active: false,
            danger: false);
        this.DrawButton(
            b,
            this.teamDashboardFollowAllButton,
            this.TeamDashboardText("companion.team.follow_all_routines", "Apply routines"),
            active: false,
            danger: false);
    }

    /// <summary>Return the member or bulk command under the supplied UI coordinates.</summary>
    internal CompanionTeamDashboardHit HitTestTeamDashboard(int x, int y)
    {
        Point point = new(x, y);
        if (this.teamDashboardStopAllButton.Contains(point))
            return new CompanionTeamDashboardHit(CompanionTeamDashboardHitKind.StopAllWork);
        if (this.teamDashboardDepositAllButton.Contains(point))
            return new CompanionTeamDashboardHit(CompanionTeamDashboardHitKind.DepositAllCargo);
        if (this.teamDashboardFollowAllButton.Contains(point))
            return new CompanionTeamDashboardHit(CompanionTeamDashboardHitKind.FollowAllRoutines);

        foreach ((Rectangle bounds, string npcName) in this.teamDashboardMemberCards)
        {
            if (bounds.Contains(point))
                return new CompanionTeamDashboardHit(CompanionTeamDashboardHitKind.Member, npcName);
        }

        return default;
    }

    /// <summary>
    /// Execute a dashboard hit. The main input handler only needs to call this
    /// when the team view is active.
    /// </summary>
    internal bool ActivateTeamDashboardHit(CompanionTeamDashboardHit hit)
    {
        switch (hit.Kind)
        {
            case CompanionTeamDashboardHitKind.Member:
                if (string.IsNullOrWhiteSpace(hit.NpcName))
                    return false;
                this.inventoryDrag = null;
                this.selectedNpcName = hit.NpcName;
                this.SetTab(PanelTab.Overview);
                this.focusedControlIndex = -1;
                Game1.playSound("smallSelect");
                return true;

            case CompanionTeamDashboardHitKind.StopAllWork:
                return this.ActivateTeamDashboardCommand(
                    this.stopAllCompanionWork,
                    successSound: "bigDeSelect");

            case CompanionTeamDashboardHitKind.DepositAllCargo:
                return this.ActivateTeamDashboardCommand(
                    this.depositAllCompanionCargo,
                    successSound: "Ship");

            case CompanionTeamDashboardHitKind.FollowAllRoutines:
                return this.ActivateTeamDashboardCommand(
                    this.followAllCompanionRoutines,
                    successSound: "questcomplete");

            default:
                return false;
        }
    }

    /// <summary>Convenience click handler for the main menu's receiveLeftClick override.</summary>
    internal bool TryHandleTeamDashboardClick(int x, int y)
    {
        CompanionTeamDashboardHit hit = this.HitTestTeamDashboard(x, y);
        return hit.Kind != CompanionTeamDashboardHitKind.None
            && this.ActivateTeamDashboardHit(hit);
    }

    /// <summary>Populate the existing tooltip channel for a hovered team card or command.</summary>
    internal bool TryHandleTeamDashboardHover(int x, int y)
    {
        CompanionTeamDashboardHit hit = this.HitTestTeamDashboard(x, y);
        switch (hit.Kind)
        {
            case CompanionTeamDashboardHitKind.Member:
                SquadMemberState? member = this.getMembers().FirstOrDefault(candidate =>
                    string.Equals(candidate.NpcName, hit.NpcName, StringComparison.OrdinalIgnoreCase));
                if (member is null || this.getTeamMemberView is null)
                    return false;

                CompanionTeamMemberView view = this.getTeamMemberView(member);
                this.hoverText = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        member.DisplayName,
                        view.Location,
                        view.Activity,
                        view.Tool,
                        view.Water,
                        $"{view.InventoryCount}/{view.InventoryCapacity}",
                        view.BlockReason
                    }.Where(text => !string.IsNullOrWhiteSpace(text)));
                return true;

            case CompanionTeamDashboardHitKind.StopAllWork:
                this.hoverText = this.TeamDashboardText(
                    "companion.team.stop_all_hint",
                    "Stop every companion's current work.");
                return true;

            case CompanionTeamDashboardHitKind.DepositAllCargo:
                this.hoverText = this.TeamDashboardText(
                    "companion.team.deposit_all_hint",
                    "Deposit eligible cargo using each companion's assigned chest and filters.");
                return true;

            case CompanionTeamDashboardHitKind.FollowAllRoutines:
                this.hoverText = this.TeamDashboardText(
                    "companion.team.follow_all_routines_hint",
                    "Resume each companion's own active routine.");
                return true;

            default:
                return false;
        }
    }

    /// <summary>Scroll the team cards when the pointer is over their list.</summary>
    internal bool TryScrollTeamDashboard(int direction, int mouseX, int mouseY)
    {
        if (direction == 0
            || this.teamDashboardMaximumFirstVisibleIndex <= 0
            || !this.teamDashboardListArea.Contains(mouseX, mouseY))
        {
            return false;
        }

        int delta = direction > 0 ? -1 : 1;
        int previous = this.teamDashboardFirstVisibleIndex;
        this.teamDashboardFirstVisibleIndex = Math.Clamp(
            previous + delta,
            0,
            this.teamDashboardMaximumFirstVisibleIndex);
        if (previous == this.teamDashboardFirstVisibleIndex)
            return false;

        Game1.playSound("shiny4");
        return true;
    }

    /// <summary>Add visible cards and commands to the menu's existing focus ring.</summary>
    internal void AddTeamDashboardFocusTargets(List<Rectangle> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        targets.AddRange(this.teamDashboardMemberCards.Select(card => card.Bounds));
        if (this.teamDashboardStopAllButton.Width > 0)
            targets.Add(this.teamDashboardStopAllButton);
        if (this.teamDashboardDepositAllButton.Width > 0)
            targets.Add(this.teamDashboardDepositAllButton);
        if (this.teamDashboardFollowAllButton.Width > 0)
            targets.Add(this.teamDashboardFollowAllButton);
    }

    /// <summary>Move the selected member and keep its team card inside the visible window.</summary>
    internal bool SelectRelativeTeamDashboardMember(int delta)
    {
        List<SquadMemberState> members = this.getMembers().ToList();
        if (members.Count == 0 || delta == 0)
            return false;

        int current = members.FindIndex(member =>
            string.Equals(member.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase));
        int next = current < 0
            ? delta > 0 ? 0 : members.Count - 1
            : Math.Clamp(current + Math.Sign(delta), 0, members.Count - 1);
        if (next == current)
            return false;

        this.inventoryDrag = null;
        this.selectedNpcName = members[next].NpcName;
        int visibleCount = Math.Max(1, this.teamDashboardMemberCards.Count);
        if (next < this.teamDashboardFirstVisibleIndex)
            this.teamDashboardFirstVisibleIndex = next;
        else if (next >= this.teamDashboardFirstVisibleIndex + visibleCount)
            this.teamDashboardFirstVisibleIndex = next - visibleCount + 1;
        this.teamDashboardFirstVisibleIndex = Math.Clamp(
            this.teamDashboardFirstVisibleIndex,
            0,
            this.teamDashboardMaximumFirstVisibleIndex);
        return true;
    }

    private void DrawTeamDashboardSummary(
        SpriteBatch b,
        Rectangle bounds,
        int memberCount,
        int fullCount,
        int blockedCount)
    {
        string[] labels =
        {
            this.TeamDashboardText(
                memberCount == 1
                    ? "companion.team.summary.members_one"
                    : "companion.team.summary.members",
                $"{memberCount} companions",
                new { count = memberCount }),
            this.TeamDashboardText(
                fullCount == 1
                    ? "companion.team.summary.full_one"
                    : "companion.team.summary.full",
                $"{fullCount} full",
                new { count = fullCount }),
            this.TeamDashboardText(
                blockedCount == 1
                    ? "companion.team.summary.blocked_one"
                    : "companion.team.summary.blocked",
                $"{blockedCount} blocked",
                new { count = blockedCount })
        };
        Color[] accents = { AccentGreen, AccentGold, DangerColor };
        int gap = 5;
        int width = Math.Max(1, (bounds.Width - gap * 2) / 3);
        for (int index = 0; index < labels.Length; index++)
        {
            int x = bounds.X + index * (width + gap);
            Rectangle chip = new(
                x,
                bounds.Y,
                index == labels.Length - 1 ? Math.Max(1, bounds.Right - x) : width,
                bounds.Height);
            this.DrawMenuCard(b, chip, HeaderCardColor, accents[index]);
            DrawCenteredPanelText(
                b,
                FitText(labels[index], Game1.tinyFont, Math.Max(1, chip.Width - 18), PanelMetaTextScale),
                Game1.tinyFont,
                chip,
                MutedTextColor,
                PanelMetaTextScale,
                9,
                3);
        }
    }

    private void DrawTeamDashboardMemberCard(
        SpriteBatch b,
        Rectangle bounds,
        SquadMemberState member,
        CompanionTeamMemberView view,
        bool compact)
    {
        bool hovered = bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color accent = !string.IsNullOrWhiteSpace(view.BlockReason)
            ? DangerColor
            : view.InventoryFull ? AccentGold : this.GetMemberStatusColor(member);
        this.DrawMenuCard(
            b,
            bounds,
            hovered ? RowHoverColor : RowColor,
            accent);

        bool tiny = bounds.Height < 80;
        int portraitSize = tiny
            ? Math.Clamp(bounds.Height - 14, 20, 34)
            : Math.Clamp(
                bounds.Height - (compact ? 54 : 20),
                34,
                compact ? 46 : 52);
        Rectangle portrait = new(
            bounds.X + (tiny ? 9 : 16),
            bounds.Y + (tiny ? Math.Max(4, (bounds.Height - portraitSize) / 2) : 10),
            portraitSize,
            portraitSize);
        this.DrawFlatPanel(b, portrait, Color.White, accent, 1);
        this.DrawPortrait(b, this.getNpc(member.NpcName), portrait);

        if (tiny)
            this.DrawTinyTeamDashboardCard(b, bounds, portrait, member, view);
        else if (compact)
            this.DrawCompactTeamDashboardCard(b, bounds, portrait, member, view);
        else
            this.DrawWideTeamDashboardCard(b, bounds, portrait, member, view);
    }

    private void DrawTinyTeamDashboardCard(
        SpriteBatch b,
        Rectangle bounds,
        Rectangle portrait,
        SquadMemberState member,
        CompanionTeamMemberView view)
    {
        int textX = portrait.Right + 6;
        int textWidth = Math.Max(1, bounds.Right - textX - 7);
        float scale = PanelCompactTextScale;
        int lineHeight = GetScaledLineHeight(Game1.tinyFont, scale);
        string firstLine = $"{member.DisplayName} · {view.Location}";
        string cargo = $"{view.InventoryCount}/{view.InventoryCapacity}";
        string secondLine = string.Join(
            " · ",
            new[]
            {
                view.Activity,
                view.Tool,
                view.Water,
                cargo,
                view.BlockReason
            }.Where(text => !string.IsNullOrWhiteSpace(text)));
        int firstY = bounds.Center.Y - lineHeight - 1;
        DrawPanelText(
            b,
            FitText(firstLine, Game1.tinyFont, textWidth, scale),
            Game1.tinyFont,
            new Vector2(textX, firstY),
            TextColor,
            scale,
            shadow: true);
        DrawPanelText(
            b,
            FitText(secondLine, Game1.tinyFont, textWidth, scale),
            Game1.tinyFont,
            new Vector2(textX, firstY + lineHeight + 2),
            string.IsNullOrWhiteSpace(view.BlockReason)
                ? view.InventoryFull ? DangerColor : MutedTextColor
                : DangerColor,
            scale,
            shadow: true);
    }

    private void DrawWideTeamDashboardCard(
        SpriteBatch b,
        Rectangle bounds,
        Rectangle portrait,
        SquadMemberState member,
        CompanionTeamMemberView view)
    {
        int textX = portrait.Right + 9;
        int textWidth = Math.Max(1, bounds.Right - textX - 12);
        int firstColumn = Math.Clamp(textWidth / 4, 110, 180);
        int secondColumn = Math.Clamp(textWidth / 4, 105, 170);
        int thirdX = textX + firstColumn + secondColumn + 12;
        int thirdWidth = Math.Max(1, bounds.Right - thirdX - 12);

        DrawPanelText(
            b,
            FitText(member.DisplayName, Game1.tinyFont, firstColumn, PanelTextScale),
            Game1.tinyFont,
            new Vector2(textX, bounds.Y + 10),
            TextColor,
            PanelTextScale,
            shadow: true);
        DrawPanelText(
            b,
            FitText(view.Location, Game1.tinyFont, secondColumn, PanelMetaTextScale),
            Game1.tinyFont,
            new Vector2(textX + firstColumn + 6, bounds.Y + 10),
            MutedTextColor,
            PanelMetaTextScale,
            shadow: true);
        DrawPanelText(
            b,
            FitText(view.Activity, Game1.tinyFont, thirdWidth, PanelTextScale),
            Game1.tinyFont,
            new Vector2(thirdX, bounds.Y + 10),
            TextColor,
            PanelTextScale,
            shadow: true);

        string inventory = this.TeamDashboardText(
            "companion.team.inventory",
            $"Cargo {view.InventoryCount}/{view.InventoryCapacity}",
            new { current = view.InventoryCount, capacity = view.InventoryCapacity });
        string equipmentLine = string.Join(
            " · ",
            new[] { view.Tool, view.Water, inventory }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        DrawPanelText(
            b,
            FitText(equipmentLine, Game1.tinyFont, textWidth, PanelMetaTextScale),
            Game1.tinyFont,
            new Vector2(textX, bounds.Y + 36),
            view.InventoryFull ? DangerColor : MutedTextColor,
            PanelMetaTextScale,
            shadow: true);

        if (!string.IsNullOrWhiteSpace(view.BlockReason))
        {
            string block = this.TeamDashboardText(
                "companion.team.blocked",
                $"Blocked: {view.BlockReason}",
                new { reason = view.BlockReason });
            DrawPanelText(
                b,
                FitText(block, Game1.tinyFont, textWidth, PanelCompactTextScale),
                Game1.tinyFont,
                new Vector2(textX, bounds.Bottom - GetScaledLineHeight(Game1.tinyFont, PanelCompactTextScale) - 6),
                DangerColor,
                PanelCompactTextScale,
                shadow: true);
        }
    }

    private void DrawCompactTeamDashboardCard(
        SpriteBatch b,
        Rectangle bounds,
        Rectangle portrait,
        SquadMemberState member,
        CompanionTeamMemberView view)
    {
        int textX = portrait.Right + 8;
        int textWidth = Math.Max(1, bounds.Right - textX - 10);
        DrawPanelText(
            b,
            FitText(member.DisplayName, Game1.tinyFont, textWidth, PanelTextScale),
            Game1.tinyFont,
            new Vector2(textX, bounds.Y + 8),
            TextColor,
            PanelTextScale,
            shadow: true);
        DrawPanelText(
            b,
            FitText(view.Activity, Game1.tinyFont, textWidth, PanelMetaTextScale),
            Game1.tinyFont,
            new Vector2(textX, bounds.Y + 29),
            MutedTextColor,
            PanelMetaTextScale,
            shadow: true);
        DrawPanelText(
            b,
            FitText(view.Location, Game1.tinyFont, textWidth, PanelCompactTextScale),
            Game1.tinyFont,
            new Vector2(textX, bounds.Y + 49),
            MutedTextColor,
            PanelCompactTextScale,
            shadow: true);

        string inventory = $"{view.InventoryCount}/{view.InventoryCapacity}";
        string equipment = string.Join(
            " · ",
            new[] { view.Tool, view.Water, inventory }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        DrawPanelText(
            b,
            FitText(equipment, Game1.tinyFont, Math.Max(1, bounds.Width - 32), PanelCompactTextScale),
            Game1.tinyFont,
            new Vector2(bounds.X + 17, bounds.Y + 72),
            view.InventoryFull ? DangerColor : MutedTextColor,
            PanelCompactTextScale,
            shadow: true);

        if (!string.IsNullOrWhiteSpace(view.BlockReason))
        {
            DrawPanelText(
                b,
                FitText(view.BlockReason, Game1.tinyFont, Math.Max(1, bounds.Width - 32), PanelCompactTextScale),
                Game1.tinyFont,
                new Vector2(bounds.X + 17, bounds.Bottom - GetScaledLineHeight(Game1.tinyFont, PanelCompactTextScale) - 4),
                DangerColor,
                PanelCompactTextScale,
                shadow: true);
        }
    }

    private void DrawTeamDashboardScrollbar(
        SpriteBatch b,
        Rectangle area,
        int memberCount,
        int visibleCount)
    {
        if (memberCount <= visibleCount || area.Height < 8)
            return;

        Rectangle track = new(area.Right - 5, area.Y + 2, 3, Math.Max(1, area.Height - 4));
        b.Draw(Game1.staminaRect, track, Color.Black * 0.16f);
        int thumbHeight = Math.Max(8, track.Height * visibleCount / Math.Max(1, memberCount));
        int travel = Math.Max(0, track.Height - thumbHeight);
        int thumbY = track.Y + (this.teamDashboardMaximumFirstVisibleIndex <= 0
            ? 0
            : travel * this.teamDashboardFirstVisibleIndex / this.teamDashboardMaximumFirstVisibleIndex);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(track.X, thumbY, track.Width, thumbHeight),
            AccentGold);
    }

    private bool ActivateTeamDashboardCommand(Func<bool>? command, string successSound)
    {
        if (command is null)
            return false;

        bool accepted = command();
        Game1.playSound(accepted ? successSound : "cancel");
        return true;
    }

    private string TeamDashboardText(string key, string fallback, object? tokens = null)
    {
        string translated = this.translate(key, tokens);
        return string.IsNullOrWhiteSpace(translated)
            || string.Equals(translated, key, StringComparison.Ordinal)
            || translated.Contains("no translation", StringComparison.OrdinalIgnoreCase)
                ? fallback
                : translated;
    }
}
