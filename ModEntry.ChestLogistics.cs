using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const string DepositChestIdModDataKey = "Lucas.PelicanCompanions/DepositChestId";
    private const int ChestDestinationPanelWidth = 236;
    private const int ChestDestinationPanelMargin = 10;
    private const int ChestDestinationPanelPadding = 10;
    private const int ChestDestinationTitleHeight = 34;
    private const int ChestDestinationButtonHeight = 32;
    private const int ChestDestinationButtonGap = 5;
    private const int CompactChestDestinationPanelWidth = 208;
    private const int CompactChestDestinationPageSize = 2;
    private const float ChestSelectionMaximumDistance = 4f;
    private const int ChestIdentityHandshakeTimeoutTicks = 600;

    private PendingChestDestinationHandshake? pendingChestDestinationHandshake;
    private ItemGrabMenu? compactChestDestinationMenu;
    private int compactChestDestinationPage;

    private enum ChestDestinationSelection
    {
        None,
        All,
        Companion,
        PreviousPage,
        PageIndicator,
        NextPage
    }

    private readonly record struct ChestMenuContext(
        ItemGrabMenu Menu,
        Chest Chest,
        GameLocation Location,
        Vector2 Tile,
        string ChestId);

    private readonly record struct ChestDestinationButton(
        ChestDestinationSelection Selection,
        string NpcName,
        string Label,
        Rectangle Bounds,
        bool Active,
        bool Enabled);

    private readonly record struct ChestDestinationLayout(
        Rectangle Panel,
        Rectangle TitleBounds,
        List<ChestDestinationButton> Buttons,
        bool Compact);

    private sealed record ResolvedDepositChest(
        Chest Chest,
        GameLocation Location,
        Vector2 Tile,
        string ChestId);

    private sealed record PendingChestDestinationHandshake(
        ItemGrabMenu Menu,
        Chest Chest,
        string LocationName,
        Vector2 Tile,
        ChestDestinationSelection Selection,
        string NpcName,
        bool DesiredEnabled,
        string CommandId,
        int StartedTick)
    {
        public string AcknowledgedChestId { get; set; } = "";
    }

    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (!Context.IsWorldReady || this.saveWritesBlocked)
        {
            this.pendingChestDestinationHandshake = null;
            return;
        }

        if (!this.TryGetOpenNormalChest(out ChestMenuContext context))
        {
            this.pendingChestDestinationHandshake = null;
            this.compactChestDestinationMenu = null;
            this.compactChestDestinationPage = 0;
            return;
        }

        this.TryAdvanceChestIdentityHandshake(context);

        List<SquadMemberState> companions = this.GetLocalChestDestinationMembers();
        ChestDestinationLayout layout = this.GetChestDestinationLayout(context, companions);
        this.DrawChestDestinationPanel(e.SpriteBatch, layout);
        context.Menu.drawMouse(e.SpriteBatch);
    }

    /// <summary>
    /// Handle the side-panel click while the vanilla ItemGrabMenu is active.
    /// This must run before the general active-menu early return in OnButtonPressed.
    /// </summary>
    private bool TryHandleChestDestinationMenuClick(Vector2 screenPixels, bool activateSelection)
    {
        if (!this.TryGetOpenNormalChest(out ChestMenuContext context))
            return false;

        List<SquadMemberState> companions = this.GetLocalChestDestinationMembers();
        ChestDestinationLayout layout = this.GetChestDestinationLayout(context, companions);
        Point point = new((int)screenPixels.X, (int)screenPixels.Y);
        if (!layout.Panel.Contains(point))
            return false;

        // The compact fallback intentionally overlays part of ItemGrabMenu.
        // Consume both mouse buttons across its full surface, but only let the
        // primary button activate routing controls.
        if (!activateSelection)
            return true;

        ChestDestinationButton? clicked = layout.Buttons
            .Cast<ChestDestinationButton?>()
            .FirstOrDefault(button => button!.Value.Bounds.Contains(point));
        if (clicked is not ChestDestinationButton button)
            return true;

        if (button.Selection == ChestDestinationSelection.PageIndicator)
            return true;

        if (!button.Enabled)
        {
            Game1.playSound("cancel");
            return true;
        }

        if (button.Selection is ChestDestinationSelection.PreviousPage or ChestDestinationSelection.NextPage)
        {
            int pageCount = Math.Max(1, (companions.Count + CompactChestDestinationPageSize - 1) / CompactChestDestinationPageSize);
            int delta = button.Selection == ChestDestinationSelection.PreviousPage ? -1 : 1;
            int previousPage = this.compactChestDestinationPage;
            this.compactChestDestinationPage = Math.Clamp(previousPage + delta, 0, pageCount - 1);
            Game1.playSound(this.compactChestDestinationPage == previousPage ? "cancel" : "shiny4");
            return true;
        }

        bool desiredEnabled = button.Selection switch
        {
            ChestDestinationSelection.None => false,
            _ => !button.Active
        };

        if (!Context.IsMainPlayer)
        {
            string commandId = this.SendActionRequest(
                "SetChestDestination",
                npcName: button.NpcName,
                argument: button.Selection.ToString(),
                tile: context.Tile,
                expectedStateToken: context.ChestId,
                desiredEnabled: desiredEnabled,
                expectedLocationName: context.Location.NameOrUniqueName);
            if (string.IsNullOrWhiteSpace(context.ChestId))
            {
                // Phase one asks the host to put a durable identity on the
                // exact chest it currently sees. The intent isn't replayed
                // until that GUID is both acknowledged and replicated onto
                // this same open chest object.
                this.pendingChestDestinationHandshake = new PendingChestDestinationHandshake(
                    context.Menu,
                    context.Chest,
                    context.Location.NameOrUniqueName,
                    context.Tile,
                    button.Selection,
                    button.NpcName,
                    desiredEnabled,
                    commandId,
                    Game1.ticks);
            }
            else
            {
                this.pendingChestDestinationHandshake = null;
            }
            Game1.playSound("smallSelect");
            return true;
        }

        bool changed = this.TrySetChestDestination(
            Game1.player.UniqueMultiplayerID,
            button.Selection,
            button.NpcName,
            desiredEnabled,
            context.Location.NameOrUniqueName,
            context.Tile,
            context.ChestId,
            showFeedback: true);
        Game1.playSound(changed ? "smallSelect" : "cancel");
        return true;
    }

    /// <summary>
    /// Accept the host's phase-one identity acknowledgement. The caller has
    /// already authenticated the message as coming from the main player.
    /// </summary>
    private bool TryHandleChestIdentityAcknowledgement(CompanionCommandFeedbackMessage feedback)
    {
        PendingChestDestinationHandshake? pending = this.pendingChestDestinationHandshake;
        if (pending is null
            || !string.Equals(feedback.Action, "SetChestDestination", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(feedback.CommandId)
            || feedback.CommandId.Length > 64
            || !string.Equals(feedback.CommandId, pending.CommandId, StringComparison.Ordinal))
        {
            return false;
        }

        if (feedback.IsError
            || (feedback.StateToken?.Length ?? 0) > 128
            || !CompanionChestRoutingPolicy.TryNormalizeChestId(feedback.StateToken, out string acknowledgedId))
        {
            // Let ordinary error feedback continue to the HUD, but cancel this
            // correlated attempt so it can't produce a second timeout later.
            this.pendingChestDestinationHandshake = null;
            return false;
        }

        pending.AcknowledgedChestId = acknowledgedId;
        return true;
    }

    private void TryAdvanceChestIdentityHandshake(ChestMenuContext context)
    {
        PendingChestDestinationHandshake? pending = this.pendingChestDestinationHandshake;
        if (pending is null)
            return;

        bool sameOpenChest = ReferenceEquals(context.Menu, pending.Menu)
            && ReferenceEquals(context.Chest, pending.Chest)
            && string.Equals(context.Location.NameOrUniqueName, pending.LocationName, StringComparison.Ordinal)
            && NormalizeTile(context.Tile) == NormalizeTile(pending.Tile);
        if (!sameOpenChest)
        {
            this.pendingChestDestinationHandshake = null;
            return;
        }

        if (Game1.ticks - pending.StartedTick > ChestIdentityHandshakeTimeoutTicks)
        {
            this.pendingChestDestinationHandshake = null;
            Game1.addHUDMessage(new HUDMessage(this.Tr("chest_destination.identity_timeout"), HUDMessage.error_type));
            return;
        }

        // An ACK alone isn't enough: if the original chest was replaced before
        // the host saw phase one, its ACK identifies the replacement. Waiting
        // for the same GUID on the same local object closes that substitution
        // race before phase two can mutate routing state.
        if (!CompanionChestRoutingPolicy.MatchesExpectedIdentity(
                pending.AcknowledgedChestId,
                context.ChestId))
        {
            return;
        }

        this.SendActionRequest(
            "SetChestDestination",
            npcName: pending.NpcName,
            argument: pending.Selection.ToString(),
            tile: pending.Tile,
            expectedStateToken: pending.AcknowledgedChestId,
            desiredEnabled: pending.DesiredEnabled,
            expectedLocationName: pending.LocationName);
        this.pendingChestDestinationHandshake = null;
    }

    private List<SquadMemberState> GetLocalChestDestinationMembers()
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        return this.members.Values
            .Where(member => member.OwnerId == ownerId)
            .OrderBy(member => member.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(member => member.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool TryGetOpenNormalChest(out ChestMenuContext context)
    {
        context = default;
        if (Game1.activeClickableMenu is not ItemGrabMenu menu
            || menu.context is not Chest chest
            || !IsNormalPlayerChest(chest)
            || Game1.currentLocation is not GameLocation location
            || !TryFindPlacedChestTile(location, chest, out Vector2 tile))
        {
            return false;
        }

        TryGetChestId(chest, out string chestId);
        context = new ChestMenuContext(menu, chest, location, tile, chestId);
        return true;
    }

    private static bool TryFindPlacedChestTile(GameLocation location, Chest expectedChest, out Vector2 tile)
    {
        tile = Vector2.Zero;
        try
        {
            foreach (Vector2 candidateTile in location.Objects.Keys.ToList())
            {
                if (location.Objects.TryGetValue(candidateTile, out SObject? obj)
                    && ReferenceEquals(obj, expectedChest))
                {
                    tile = NormalizeTile(candidateTile);
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsNormalPlayerChest(Chest? chest)
    {
        if (chest is null)
            return false;

        try
        {
            return chest.playerChest.Value
                && !chest.fridge.Value
                && !chest.giftbox.Value
                && string.IsNullOrWhiteSpace(chest.GlobalInventoryId)
                && chest.SpecialChestType == Chest.SpecialChestTypes.None;
        }
        catch
        {
            return false;
        }
    }

    private ChestDestinationLayout GetChestDestinationLayout(
        ChestMenuContext context,
        IReadOnlyList<SquadMemberState> companions)
    {
        int menuLeft = context.Menu.xPositionOnScreen;
        int menuRight = menuLeft + context.Menu.width;
        bool hasRoomOnRight = menuRight + ChestDestinationPanelMargin + ChestDestinationPanelWidth
            <= Game1.uiViewport.Width - ChestDestinationPanelMargin;
        bool hasRoomOnLeft = menuLeft - ChestDestinationPanelMargin - ChestDestinationPanelWidth
            >= ChestDestinationPanelMargin;
        if (!hasRoomOnRight && !hasRoomOnLeft)
            return this.GetCompactChestDestinationLayout(context, companions);

        int buttonCount = companions.Count + 2;
        int maximumPanelHeight = Math.Max(120, Game1.uiViewport.Height - ChestDestinationPanelMargin * 2);
        int singleColumnHeight = ChestDestinationPanelPadding * 2
            + ChestDestinationTitleHeight
            + buttonCount * ChestDestinationButtonHeight
            + Math.Max(0, buttonCount - 1) * ChestDestinationButtonGap;
        bool useTwoCompanionColumns = singleColumnHeight > maximumPanelHeight && companions.Count > 1;
        int rowCount = 2 + (useTwoCompanionColumns ? (companions.Count + 1) / 2 : companions.Count);
        int rowGap = singleColumnHeight <= maximumPanelHeight ? ChestDestinationButtonGap : 2;
        int availableRowHeight = Math.Max(
            12,
            (maximumPanelHeight
                - ChestDestinationPanelPadding * 2
                - ChestDestinationTitleHeight
                - Math.Max(0, rowCount - 1) * rowGap)
            / Math.Max(1, rowCount));
        int rowHeight = Math.Clamp(availableRowHeight, 12, ChestDestinationButtonHeight);
        int panelHeight = ChestDestinationPanelPadding * 2
            + ChestDestinationTitleHeight
            + rowCount * rowHeight
            + Math.Max(0, rowCount - 1) * rowGap;

        int x = menuRight + ChestDestinationPanelMargin;
        if (!hasRoomOnRight)
            x = context.Menu.xPositionOnScreen - ChestDestinationPanelWidth - ChestDestinationPanelMargin;
        x = Math.Clamp(
            x,
            ChestDestinationPanelMargin,
            Math.Max(ChestDestinationPanelMargin, Game1.uiViewport.Width - ChestDestinationPanelWidth - ChestDestinationPanelMargin));
        int y = Math.Clamp(
            context.Menu.yPositionOnScreen,
            ChestDestinationPanelMargin,
            Math.Max(ChestDestinationPanelMargin, Game1.uiViewport.Height - panelHeight - ChestDestinationPanelMargin));

        Rectangle panel = new(x, y, ChestDestinationPanelWidth, panelHeight);
        int contentX = panel.X + ChestDestinationPanelPadding;
        int contentWidth = panel.Width - ChestDestinationPanelPadding * 2;
        int buttonY = panel.Y + ChestDestinationPanelPadding + ChestDestinationTitleHeight;
        long ownerId = Game1.player.UniqueMultiplayerID;
        this.ownerLogistics.TryGetValue(ownerId, out CompanionOwnerLogisticsState? logistics);

        bool defaultPointsHere = DestinationMatchesChest(
            logistics?.DefaultChestDestination,
            context.ChestId);
        bool anyIndividualPointsHere = this.operationalProfiles
            .Where(pair => pair.Key.OwnerId == ownerId)
            .Any(pair => DestinationMatchesChest(
                pair.Value.ChestDestination,
                context.ChestId));

        List<ChestDestinationButton> buttons = new(buttonCount);
        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.None,
            "",
            this.Tr("chest_destination.none"),
            new Rectangle(contentX, buttonY, contentWidth, rowHeight),
            !defaultPointsHere && !anyIndividualPointsHere,
            Enabled: true));
        buttonY += rowHeight + rowGap;

        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.All,
            "",
            this.Tr("chest_destination.all"),
            new Rectangle(contentX, buttonY, contentWidth, rowHeight),
            defaultPointsHere,
            Enabled: true));
        buttonY += rowHeight + rowGap;

        int companionIndex = 0;
        foreach (SquadMemberState member in companions)
        {
            this.TryGetOperationalProfile(ownerId, member.NpcName, out CompanionOperationalProfileState? profile);
            bool active = DestinationMatchesChest(
                profile?.ChestDestination,
                context.ChestId);
            int column = useTwoCompanionColumns ? companionIndex % 2 : 0;
            int row = useTwoCompanionColumns ? companionIndex / 2 : companionIndex;
            int columnGap = useTwoCompanionColumns ? rowGap : 0;
            int columnWidth = useTwoCompanionColumns ? (contentWidth - columnGap) / 2 : contentWidth;
            Rectangle bounds = new(
                contentX + column * (columnWidth + columnGap),
                buttonY + row * (rowHeight + rowGap),
                columnWidth,
                rowHeight);
            buttons.Add(new ChestDestinationButton(
                ChestDestinationSelection.Companion,
                member.NpcName,
                member.DisplayName,
                bounds,
                active,
                Enabled: true));
            companionIndex++;
        }

        Rectangle titleBounds = new(
            contentX,
            panel.Y + ChestDestinationPanelPadding,
            contentWidth,
            ChestDestinationTitleHeight);
        return new ChestDestinationLayout(panel, titleBounds, buttons, Compact: false);
    }

    private ChestDestinationLayout GetCompactChestDestinationLayout(
        ChestMenuContext context,
        IReadOnlyList<SquadMemberState> companions)
    {
        if (!ReferenceEquals(this.compactChestDestinationMenu, context.Menu))
        {
            this.compactChestDestinationMenu = context.Menu;
            this.compactChestDestinationPage = 0;
        }

        int pageCount = Math.Max(
            1,
            (companions.Count + CompactChestDestinationPageSize - 1) / CompactChestDestinationPageSize);
        this.compactChestDestinationPage = Math.Clamp(this.compactChestDestinationPage, 0, pageCount - 1);
        List<SquadMemberState> pageMembers = companions
            .Skip(this.compactChestDestinationPage * CompactChestDestinationPageSize)
            .Take(CompactChestDestinationPageSize)
            .ToList();

        int viewportWidth = Math.Max(1, Game1.uiViewport.Width);
        int viewportHeight = Math.Max(1, Game1.uiViewport.Height);
        int rowCount = 2 + pageMembers.Count; // shared row, companion rows, navigation row
        int horizontalMargin = Math.Min(
            ChestDestinationPanelMargin,
            Math.Max(0, (viewportWidth - 3) / 2));
        int verticalMargin = Math.Min(
            ChestDestinationPanelMargin,
            Math.Max(0, (viewportHeight - rowCount - 1) / 2));
        int panelWidth = Math.Max(
            1,
            Math.Min(
                CompactChestDestinationPanelWidth,
                viewportWidth - horizontalMargin * 2));
        int maximumPanelHeight = Math.Max(1, viewportHeight - verticalMargin * 2);

        int padding = Math.Min(6, Math.Max(0, (maximumPanelHeight - rowCount - 1) / 8));
        padding = Math.Min(padding, Math.Max(0, (panelWidth - 3) / 2));
        int gap = Math.Min(
            3,
            Math.Max(0, (maximumPanelHeight - padding * 2 - rowCount - 1) / Math.Max(1, rowCount - 1)));
        int titleHeight = Math.Min(
            22,
            Math.Max(
                1,
                (maximumPanelHeight - padding * 2 - gap * Math.Max(0, rowCount - 1)) / 4));
        int availableRowsHeight = Math.Max(
            rowCount,
            maximumPanelHeight
                - padding * 2
                - titleHeight
                - gap * Math.Max(0, rowCount - 1));
        int rowHeight = Math.Min(22, Math.Max(1, availableRowsHeight / Math.Max(1, rowCount)));
        int panelHeight = Math.Min(
            maximumPanelHeight,
            padding * 2
                + titleHeight
                + rowCount * rowHeight
                + gap * Math.Max(0, rowCount - 1));

        int x = horizontalMargin;
        int maximumY = Math.Max(verticalMargin, viewportHeight - panelHeight - verticalMargin);
        int y = Math.Clamp(context.Menu.yPositionOnScreen, verticalMargin, maximumY);
        Rectangle panel = new(x, y, panelWidth, panelHeight);
        int contentX = panel.X + padding;
        int contentWidth = Math.Max(1, panel.Width - padding * 2);
        Rectangle titleBounds = new(contentX, panel.Y + padding, contentWidth, titleHeight);
        int buttonY = titleBounds.Bottom;

        long ownerId = Game1.player.UniqueMultiplayerID;
        this.ownerLogistics.TryGetValue(ownerId, out CompanionOwnerLogisticsState? logistics);
        bool defaultPointsHere = DestinationMatchesChest(
            logistics?.DefaultChestDestination,
            context.ChestId);
        bool anyIndividualPointsHere = this.operationalProfiles
            .Where(pair => pair.Key.OwnerId == ownerId)
            .Any(pair => DestinationMatchesChest(pair.Value.ChestDestination, context.ChestId));

        int sharedGap = Math.Min(gap, Math.Max(0, contentWidth - 2));
        int sharedWidth = Math.Max(1, (contentWidth - sharedGap) / 2);
        List<ChestDestinationButton> buttons = new(pageMembers.Count + 5)
        {
            new ChestDestinationButton(
                ChestDestinationSelection.None,
                "",
                this.Tr("chest_destination.none"),
                new Rectangle(contentX, buttonY, sharedWidth, rowHeight),
                !defaultPointsHere && !anyIndividualPointsHere,
                Enabled: true),
            new ChestDestinationButton(
                ChestDestinationSelection.All,
                "",
                this.Tr("chest_destination.all"),
                new Rectangle(
                    contentX + sharedWidth + sharedGap,
                    buttonY,
                    Math.Max(1, contentWidth - sharedWidth - sharedGap),
                    rowHeight),
                defaultPointsHere,
                Enabled: true)
        };
        buttonY += rowHeight + gap;

        foreach (SquadMemberState member in pageMembers)
        {
            this.TryGetOperationalProfile(ownerId, member.NpcName, out CompanionOperationalProfileState? profile);
            buttons.Add(new ChestDestinationButton(
                ChestDestinationSelection.Companion,
                member.NpcName,
                member.DisplayName,
                new Rectangle(contentX, buttonY, contentWidth, rowHeight),
                DestinationMatchesChest(profile?.ChestDestination, context.ChestId),
                Enabled: true));
            buttonY += rowHeight + gap;
        }

        int navigationGap = Math.Min(gap, Math.Max(0, (contentWidth - 3) / 2));
        int navigationWidth = Math.Max(1, (contentWidth - navigationGap * 2) / 3);
        int pageNumber = this.compactChestDestinationPage + 1;
        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.PreviousPage,
            "",
            this.Tr("chest_destination.previous"),
            new Rectangle(contentX, buttonY, navigationWidth, rowHeight),
            Active: false,
            Enabled: this.compactChestDestinationPage > 0));
        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.PageIndicator,
            "",
            this.Tr("chest_destination.page", new { current = pageNumber, total = pageCount }),
            new Rectangle(contentX + navigationWidth + navigationGap, buttonY, navigationWidth, rowHeight),
            Active: false,
            Enabled: false));
        int nextX = contentX + (navigationWidth + navigationGap) * 2;
        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.NextPage,
            "",
            this.Tr("chest_destination.next"),
            new Rectangle(nextX, buttonY, Math.Max(1, contentX + contentWidth - nextX), rowHeight),
            Active: false,
            Enabled: this.compactChestDestinationPage < pageCount - 1));

        return new ChestDestinationLayout(panel, titleBounds, buttons, Compact: true);
    }

    private void DrawChestDestinationPanel(
        SpriteBatch spriteBatch,
        ChestDestinationLayout layout)
    {
        Rectangle panel = layout.Panel;
        Color panelBorder = new(90, 62, 42);
        Color panelFill = new(246, 226, 173);
        Color buttonBorder = new(141, 103, 65);
        Color buttonFill = new(235, 210, 156);
        Color buttonHover = new(250, 232, 188);
        Color buttonActive = new(181, 220, 173);
        Color buttonDisabled = new(218, 204, 175);
        Color textColor = new(63, 42, 31);
        Color disabledTextColor = new(126, 110, 91);
        Point mouse = new(Game1.getMouseX(true), Game1.getMouseY(true));

        spriteBatch.Draw(Game1.staminaRect, new Rectangle(panel.X + 4, panel.Y + 5, panel.Width, panel.Height), Color.Black * 0.25f);
        spriteBatch.Draw(Game1.staminaRect, panel, panelBorder);
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(panel.X + 3, panel.Y + 3, panel.Width - 6, panel.Height - 6),
            panelFill);

        SpriteFont titleFont = layout.Compact ? Game1.tinyFont : Game1.smallFont;
        string title = FitChestDestinationText(
            this.Tr("chest_destination.title"),
            titleFont,
            layout.TitleBounds.Width);
        Vector2 titleSize = titleFont.MeasureString(title);
        Utility.drawTextWithShadow(
            spriteBatch,
            title,
            titleFont,
            new Vector2(
                layout.TitleBounds.X,
                layout.Compact
                    ? layout.TitleBounds.Y + Math.Max(0f, (layout.TitleBounds.Height - titleSize.Y) / 2f)
                    : layout.TitleBounds.Y - 1),
            textColor);

        foreach (ChestDestinationButton button in layout.Buttons)
        {
            bool hovered = button.Enabled && button.Bounds.Contains(mouse);
            Color fill = !button.Enabled
                ? buttonDisabled
                : button.Active
                    ? buttonActive
                    : hovered
                        ? buttonHover
                        : buttonFill;
            spriteBatch.Draw(Game1.staminaRect, button.Bounds, buttonBorder);
            Rectangle inner = new(
                button.Bounds.X + 2,
                button.Bounds.Y + 2,
                Math.Max(1, button.Bounds.Width - 4),
                Math.Max(1, button.Bounds.Height - 4));
            spriteBatch.Draw(Game1.staminaRect, inner, fill);

            string label = FitChestDestinationText(button.Label, Game1.tinyFont, button.Bounds.Width - 20);
            Vector2 size = Game1.tinyFont.MeasureString(label);
            Utility.drawTextWithShadow(
                spriteBatch,
                label,
                Game1.tinyFont,
                new Vector2(
                    button.Bounds.X + Math.Max(8f, (button.Bounds.Width - size.X) / 2f),
                    button.Bounds.Y + Math.Max(2f, (button.Bounds.Height - size.Y) / 2f)),
                button.Enabled ? textColor : disabledTextColor);
        }
    }

    private static string FitChestDestinationText(string text, SpriteFont font, int maximumWidth)
    {
        if (font.MeasureString(text).X <= maximumWidth)
            return text;

        const string ellipsis = "…";
        string trimmed = text;
        while (trimmed.Length > 0 && font.MeasureString(trimmed + ellipsis).X > maximumWidth)
            trimmed = trimmed[..^1];
        return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
    }

    private bool TrySetChestDestination(
        long ownerId,
        ChestDestinationSelection selection,
        string npcName,
        bool desiredEnabled,
        string locationName,
        Vector2 rawTile,
        string expectedChestId,
        bool showFeedback)
    {
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        Vector2 tile = NormalizeTile(rawTile);
        if (owner?.currentLocation is not GameLocation location
            || string.IsNullOrWhiteSpace(locationName)
            || !string.Equals(location.NameOrUniqueName, locationName, StringComparison.Ordinal)
            || Vector2.Distance(owner.Tile, tile) > ChestSelectionMaximumDistance
            || !TryGetNormalChestAt(location, tile, out Chest chest))
        {
            if (showFeedback)
                this.WarnForPlayer(ownerId, "chest_destination.stale");
            return false;
        }

        TryGetChestId(chest, out string currentChestId);
        if (selection == ChestDestinationSelection.Companion
            && (string.IsNullOrWhiteSpace(npcName)
                || !this.members.TryGetValue(npcName, out SquadMemberState? selectedMember)
                || selectedMember.OwnerId != ownerId))
        {
            if (showFeedback)
                this.WarnForPlayer(ownerId, "chest_destination.stale");
            return false;
        }

        bool isRemoteRequest = Context.IsMainPlayer
            && ownerId != Game1.player.UniqueMultiplayerID;
        if (isRemoteRequest && string.IsNullOrWhiteSpace(expectedChestId))
        {
            // Empty identity is phase one only. It may establish the chest GUID,
            // but it must never authorize the requested assignment mutation.
            if (!CompanionChestRoutingPolicy.TryNormalizeChestId(currentChestId, out string handshakeChestId)
                && !this.TryGetOrCreateChestId(chest, out handshakeChestId))
            {
                if (showFeedback)
                    this.WarnForPlayer(ownerId, "chest_destination.stale");
                return false;
            }

            this.SendChestIdentityAcknowledgement(ownerId, handshakeChestId);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedChestId)
            && !CompanionChestRoutingPolicy.MatchesExpectedIdentity(expectedChestId, currentChestId))
        {
            if (showFeedback)
                this.WarnForPlayer(ownerId, "chest_destination.stale");
            return false;
        }

        string chestId;
        if (!CompanionChestRoutingPolicy.TryNormalizeChestId(currentChestId, out chestId))
        {
            if (!desiredEnabled || selection == ChestDestinationSelection.None)
            {
                if (showFeedback)
                    this.InfoForPlayer(ownerId, "chest_destination.unchanged");
                return false;
            }

            if (!this.TryGetOrCreateChestId(chest, out chestId))
            {
                if (showFeedback)
                    this.WarnForPlayer(ownerId, "chest_destination.stale");
                return false;
            }
        }

        CompanionChestDestinationState destination = new()
        {
            LocationName = location.NameOrUniqueName,
            TileX = (int)tile.X,
            TileY = (int)tile.Y,
            ChestId = chestId
        };

        bool changed = selection switch
        {
            ChestDestinationSelection.None => this.ClearChestDestinationAssignments(ownerId, destination),
            ChestDestinationSelection.All => this.SetOwnerDefaultChestDestination(ownerId, destination, desiredEnabled),
            ChestDestinationSelection.Companion => this.SetIndividualChestDestination(ownerId, npcName, destination, desiredEnabled),
            _ => false
        };

        if (!changed)
        {
            if (showFeedback)
                this.InfoForPlayer(ownerId, "chest_destination.unchanged");
            return false;
        }

        this.MarkStateDirty();
        if (showFeedback)
        {
            switch (selection)
            {
                case ChestDestinationSelection.None:
                    this.InfoForPlayer(ownerId, "chest_destination.cleared");
                    break;
                case ChestDestinationSelection.All:
                    this.InfoForPlayer(
                        ownerId,
                        desiredEnabled ? "chest_destination.all_set" : "chest_destination.all_cleared");
                    break;
                case ChestDestinationSelection.Companion:
                    string displayName = this.members.TryGetValue(npcName, out SquadMemberState? member)
                        ? member.DisplayName
                        : npcName;
                    this.InfoForPlayer(
                        ownerId,
                        desiredEnabled ? "chest_destination.companion_set" : "chest_destination.companion_cleared",
                        new { npc = displayName });
                    break;
            }
        }

        return true;
    }

    private void SendChestIdentityAcknowledgement(long ownerId, string chestId)
    {
        string commandId = this.commandFeedbackCommandId ?? "";
        if (!Context.IsMainPlayer
            || ownerId == Game1.player.UniqueMultiplayerID
            || string.IsNullOrWhiteSpace(commandId)
            || !CompanionChestRoutingPolicy.TryNormalizeChestId(chestId, out string normalizedChestId))
        {
            return;
        }

        try
        {
            this.Helper.Multiplayer.SendMessage(
                new CompanionCommandFeedbackMessage
                {
                    Action = "SetChestDestination",
                    CommandId = commandId,
                    StateToken = normalizedChestId
                },
                MessageCommandFeedback,
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: new[] { ownerId });
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Could not acknowledge chest identity to player {ownerId}: {ex.Message}",
                LogLevel.Warn);
        }
    }

    private bool ClearChestDestinationAssignments(long ownerId, CompanionChestDestinationState destination)
    {
        bool changed = false;
        if (this.ownerLogistics.TryGetValue(ownerId, out CompanionOwnerLogisticsState? logistics)
            && DestinationMatchesChest(logistics.DefaultChestDestination, destination))
        {
            logistics.DefaultChestDestination = null;
            changed = true;
        }

        foreach ((CompanionOperationalProfileKey key, CompanionOperationalProfileState profile) in this.operationalProfiles)
        {
            if (key.OwnerId == ownerId && DestinationMatchesChest(profile.ChestDestination, destination))
            {
                profile.ChestDestination = null;
                changed = true;
            }
        }

        return changed;
    }

    private bool SetOwnerDefaultChestDestination(
        long ownerId,
        CompanionChestDestinationState destination,
        bool enabled)
    {
        if (!this.ownerLogistics.TryGetValue(ownerId, out CompanionOwnerLogisticsState? logistics))
        {
            if (!enabled)
                return false;

            logistics = new CompanionOwnerLogisticsState { OwnerId = ownerId };
            this.ownerLogistics.Add(ownerId, logistics);
        }

        if (!enabled)
        {
            if (!DestinationMatchesChest(logistics.DefaultChestDestination, destination))
                return false;

            logistics.DefaultChestDestination = null;
            return true;
        }

        bool changed = false;
        if (!DestinationEquals(logistics.DefaultChestDestination, destination))
        {
            logistics.DefaultChestDestination = CompanionOperationsStateCopy.CloneChest(destination);
            changed = true;
        }

        // “All” is literal at the moment it is selected: stale per-companion
        // overrides must not silently keep workers on older chests. Players can
        // create new individual overrides afterwards.
        foreach ((CompanionOperationalProfileKey key, CompanionOperationalProfileState profile) in this.operationalProfiles)
        {
            if (key.OwnerId == ownerId && profile.ChestDestination is not null)
            {
                profile.ChestDestination = null;
                changed = true;
            }
        }

        return changed;
    }

    private bool SetIndividualChestDestination(
        long ownerId,
        string npcName,
        CompanionChestDestinationState destination,
        bool enabled)
    {
        if (string.IsNullOrWhiteSpace(npcName)
            || !this.members.TryGetValue(npcName, out SquadMemberState? member)
            || member.OwnerId != ownerId)
        {
            return false;
        }

        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(ownerId, member.NpcName);
        if (!enabled)
        {
            if (!DestinationMatchesChest(profile.ChestDestination, destination))
                return false;

            profile.ChestDestination = null;
            return true;
        }

        if (DestinationEquals(profile.ChestDestination, destination))
            return false;

        profile.ChestDestination = CompanionOperationsStateCopy.CloneChest(destination);
        return true;
    }

    private static bool DestinationMatchesChest(
        CompanionChestDestinationState? candidate,
        CompanionChestDestinationState expected)
    {
        return CompanionChestRoutingPolicy.RefersToChestId(candidate, expected.ChestId);
    }

    private static bool DestinationMatchesChest(
        CompanionChestDestinationState? candidate,
        string chestId)
    {
        return CompanionChestRoutingPolicy.RefersToChestId(candidate, chestId);
    }

    private static bool DestinationEquals(
        CompanionChestDestinationState? first,
        CompanionChestDestinationState second)
    {
        return CompanionChestRoutingPolicy.RefersToChestId(first, second.ChestId)
            && string.Equals(first!.LocationName, second.LocationName, StringComparison.Ordinal)
            && first.TileX == second.TileX
            && first.TileY == second.TileY;
    }

    private bool TryGetOrCreateChestId(Chest chest, out string chestId)
    {
        if (TryGetChestId(chest, out chestId))
            return true;

        try
        {
            chestId = Guid.NewGuid().ToString("N");
            chest.modData[DepositChestIdModDataKey] = chestId;
            return true;
        }
        catch (Exception ex)
        {
            chestId = "";
            this.Monitor.Log($"Couldn't assign a deposit GUID to a normal chest: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private static bool TryGetChestId(Chest chest, out string chestId)
    {
        chestId = "";
        try
        {
            return chest.modData.TryGetValue(DepositChestIdModDataKey, out string? rawId)
                && CompanionChestRoutingPolicy.TryNormalizeChestId(rawId, out chestId);
        }
        catch
        {
            chestId = "";
            return false;
        }
    }

    private static bool TryGetNormalChestAt(GameLocation location, Vector2 rawTile, out Chest chest)
    {
        Vector2 tile = NormalizeTile(rawTile);
        chest = null!;
        try
        {
            if (location.Objects.TryGetValue(tile, out SObject? obj)
                && obj is Chest candidate
                && IsNormalPlayerChest(candidate))
            {
                chest = candidate;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private bool TryGetAssignedChestDestination(
        SquadMemberState member,
        out CompanionChestDestinationState destination)
    {
        destination = null!;
        this.TryGetOperationalProfile(member.OwnerId, member.NpcName, out CompanionOperationalProfileState? profile);
        this.ownerLogistics.TryGetValue(member.OwnerId, out CompanionOwnerLogisticsState? logistics);
        CompanionChestDestinationState? selected = CompanionChestRoutingPolicy.Select(
            profile?.ChestDestination,
            logistics?.DefaultChestDestination);
        if (selected is null)
            return false;

        destination = selected;
        return true;
    }

    private bool TryResolveAssignedChest(
        SquadMemberState member,
        out ResolvedDepositChest resolved)
    {
        resolved = null!;
        return this.TryGetAssignedChestDestination(member, out CompanionChestDestinationState destination)
            && this.TryResolveChestDestination(destination, out resolved);
    }

    private bool TryResolveChestDestination(
        CompanionChestDestinationState destination,
        out ResolvedDepositChest resolved)
    {
        resolved = null!;
        if (!CompanionChestRoutingPolicy.IsValid(destination)
            || !CompanionChestRoutingPolicy.TryNormalizeChestId(destination.ChestId, out string requestedId))
        {
            return false;
        }

        List<ResolvedDepositChest> matches = new();
        HashSet<Chest> seenChests = new(ReferenceEqualityComparer.Instance);
        foreach (GameLocation location in this.EnumerateChestSearchLocations(destination.LocationName))
        {
            List<Vector2> tiles;
            try
            {
                tiles = location.Objects.Keys.ToList();
            }
            catch
            {
                continue;
            }

            foreach (Vector2 rawTile in tiles)
            {
                Vector2 tile = NormalizeTile(rawTile);
                if (!TryGetNormalChestAt(location, tile, out Chest chest)
                    || !seenChests.Add(chest)
                    || !TryGetChestId(chest, out string candidateId)
                    || !string.Equals(candidateId, requestedId, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(new ResolvedDepositChest(chest, location, tile, candidateId));
            }
        }

        ResolvedDepositChest? unique = CompanionChestRoutingPolicy.SelectUnique(matches, candidate => candidate.ChestId);
        if (unique is null)
            return false;

        resolved = unique;
        string resolvedLocationName = unique.Location.NameOrUniqueName;
        int resolvedX = (int)unique.Tile.X;
        int resolvedY = (int)unique.Tile.Y;
        if (!string.Equals(destination.LocationName, resolvedLocationName, StringComparison.Ordinal)
            || destination.TileX != resolvedX
            || destination.TileY != resolvedY
            || !string.Equals(destination.ChestId, unique.ChestId, StringComparison.Ordinal))
        {
            destination.LocationName = resolvedLocationName;
            destination.TileX = resolvedX;
            destination.TileY = resolvedY;
            destination.ChestId = unique.ChestId;
            this.MarkStateDirty();
        }

        return true;
    }

    private IEnumerable<GameLocation> EnumerateChestSearchLocations(string knownLocationName)
    {
        Queue<GameLocation> pending = new();
        HashSet<GameLocation> seen = new(ReferenceEqualityComparer.Instance);

        void Add(GameLocation? location)
        {
            if (location is not null && seen.Add(location))
                pending.Enqueue(location);
        }

        try
        {
            Add(Game1.getLocationFromName(knownLocationName));
        }
        catch
        {
            // Continue with the global location graph.
        }

        Add(Game1.currentLocation);
        try
        {
            foreach (GameLocation location in Game1.locations)
                Add(location);
        }
        catch
        {
            // A malformed custom location shouldn't disable known-location lookup.
        }

        try
        {
            foreach (Farmer farmer in Game1.getAllFarmers())
                Add(farmer.currentLocation);
        }
        catch
        {
            // Online-player locations are an optional expansion of the search.
        }

        foreach (SquadMemberState member in this.members.Values)
        {
            try
            {
                Add(this.GetNpcByName(member.NpcName)?.currentLocation);
            }
            catch
            {
                // A missing custom NPC doesn't invalidate the other locations.
            }
        }

        while (pending.Count > 0)
        {
            GameLocation location = pending.Dequeue();
            yield return location;

            List<GameLocation> interiors;
            try
            {
                interiors = location.GetInstancedBuildingInteriors().ToList();
            }
            catch
            {
                continue;
            }

            foreach (GameLocation interior in interiors)
                Add(interior);
        }
    }

    private Item? AddToAssignedChestSafely(
        SquadMemberState member,
        Item item,
        string context)
    {
        if (!this.TryResolveAssignedChest(member, out ResolvedDepositChest resolved)
            || !IsChestAvailableForDeposit(resolved.Chest))
        {
            return item;
        }

        return this.AddToChestSafely(resolved.Chest, item, context);
    }

    private Item? AddToChestSafely(Chest chest, Item item, string context)
    {
        int originalStack;
        Item candidate;
        try
        {
            originalStack = Math.Max(1, item.Stack);
            candidate = CloneItemStack(item);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't clone chest-deposit item during {context}; the original route will retain it. {ex.Message}", LogLevel.Warn);
            return item;
        }

        int before = CountCompatibleChestStack(chest, candidate);
        try
        {
            Item? reportedRemainder = chest.addItem(candidate);
            int after = CountCompatibleChestStack(chest, candidate);
            int transferred = Math.Clamp(after - before, 0, originalStack);
            int reportedRemaining = Math.Clamp(reportedRemainder?.Stack ?? 0, 0, originalStack);
            int remaining = transferred > 0
                ? originalStack - transferred
                : reportedRemaining;
            return this.CreateRemainderStack(item, remaining);
        }
        catch (Exception ex)
        {
            int after = CountCompatibleChestStack(chest, candidate);
            int transferred = Math.Clamp(after - before, 0, originalStack);
            this.Monitor.Log(
                $"Chest deposit failed during {context}; {transferred} committed item(s) were reconciled and only the remainder will continue. {ex}",
                LogLevel.Error);
            return this.CreateRemainderStack(item, originalStack - transferred);
        }
    }

    private static int CountCompatibleChestStack(Chest chest, Item template)
    {
        long total = 0;
        try
        {
            foreach (Item? item in chest.Items)
            {
                try
                {
                    if (item is not null
                        && (ReferenceEquals(item, template) || item.canStackWith(template)))
                        total += Math.Max(0, item.Stack);
                }
                catch
                {
                    // A malformed custom stack is ignored consistently before
                    // and after the attempted transfer.
                }
            }
        }
        catch
        {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, total);
    }

    private static bool IsChestAvailableForDeposit(Chest chest)
    {
        try
        {
            return !chest.GetMutex().IsLocked();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deposit every non-tool item carried by one companion into its effective
    /// individual/default destination. The routine engine retries when false.
    /// </summary>
    private bool TryDepositCompanionInventoryToAssignedChest(
        SquadMemberState member,
        bool showFeedback)
    {
        if (!this.HasCompanionDepositCargo(member))
            return true;

        if (!Context.IsMainPlayer
            || !this.members.TryGetValue(member.NpcName, out SquadMemberState? activeMember)
            || !ReferenceEquals(activeMember, member)
            || !this.TryResolveAssignedChest(member, out ResolvedDepositChest resolved)
            || !IsChestAvailableForDeposit(resolved.Chest))
        {
            if (showFeedback)
                this.WarnForPlayer(member.OwnerId, "chest_destination.deposit_unavailable", new { npc = member.DisplayName });
            return false;
        }

        bool movedAny = false;
        int index = 0;
        while (index < member.Inventory.Count)
        {
            SavedItemStack saved = member.Inventory[index];
            Item? item = this.TryCreateItem(saved);
            if (item is null || item is Tool)
            {
                index++;
                continue;
            }

            int originalStack = Math.Max(1, item.Stack);
            Item? remainder = this.AddToChestSafely(
                resolved.Chest,
                item,
                $"companion inventory deposit for '{member.NpcName}'");
            int remaining = Math.Clamp(remainder?.Stack ?? 0, 0, originalStack);
            int moved = originalStack - remaining;
            if (moved <= 0)
            {
                index++;
                continue;
            }

            movedAny = true;
            if (remaining == 0)
            {
                member.Inventory.RemoveAt(index);
            }
            else
            {
                // The source was already persistable and chest.addItem changes
                // only quantity. Preserve its exact metadata while reconciling
                // the committed count, avoiding a second serialization failure
                // after the destination mutation has already happened.
                saved.Stack = remaining;
                index++;
            }

            this.MarkStateDirty();
        }

        bool complete = !this.HasCompanionDepositCargo(member);
        if (showFeedback)
        {
            if (complete)
                this.InfoForPlayer(member.OwnerId, "chest_destination.deposit_complete", new { npc = member.DisplayName });
            else if (movedAny)
                this.WarnForPlayer(member.OwnerId, "chest_destination.deposit_partial", new { npc = member.DisplayName });
            else
                this.WarnForPlayer(member.OwnerId, "chest_destination.deposit_full", new { npc = member.DisplayName });
        }

        return complete;
    }

    private bool HasCompanionDepositCargo(SquadMemberState member)
    {
        foreach (SavedItemStack saved in member.Inventory)
        {
            Item? item = this.TryCreateItem(saved);
            if (item is null || item is not Tool)
                return true;
        }

        return false;
    }
}
