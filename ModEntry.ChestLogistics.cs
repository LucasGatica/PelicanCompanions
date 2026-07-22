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
    private const int ChestDestinationMinimumButtonHeight = 24;
    private const int ChestDestinationButtonGap = 5;
    private const int CompactChestDestinationPanelWidth = 208;
    private const int CompactChestDestinationPageSize = 2;
    private const float ChestDestinationTitleTextScale = 0.70f;
    private const float ChestDestinationBodyTextScale = 0.62f;
    private const float ChestDestinationMetaTextScale = 0.50f;
    private const float ChestDestinationNumberTextScale = 0.72f;
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

    private enum ChestDestinationDisplayState
    {
        Neutral,
        ExplicitHere,
        InheritedHere,
        OverrideElsewhere
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
        bool Enabled,
        ChestDestinationDisplayState DisplayState);

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
        this.DrawChestDestinationPanel(e.SpriteBatch, layout, context.Menu);
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

        if (this.pendingChestDestinationHandshake is not null
            && ReferenceEquals(this.pendingChestDestinationHandshake.Menu, context.Menu))
        {
            Game1.playSound("cancel");
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
        int availableRowHeight = (maximumPanelHeight
            - ChestDestinationPanelPadding * 2
            - ChestDestinationTitleHeight
            - Math.Max(0, rowCount - 1) * rowGap)
            / Math.Max(1, rowCount);
        if (availableRowHeight < ChestDestinationMinimumButtonHeight)
            return this.GetCompactChestDestinationLayout(context, companions);

        int rowHeight = Math.Min(availableRowHeight, ChestDestinationButtonHeight);
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
        bool nothingPointsHere = !defaultPointsHere && !anyIndividualPointsHere;

        List<ChestDestinationButton> buttons = new(buttonCount);
        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.None,
            "",
            this.Tr("chest_destination.none"),
            new Rectangle(contentX, buttonY, contentWidth, rowHeight),
            nothingPointsHere,
            Enabled: true,
            DisplayState: nothingPointsHere ? ChestDestinationDisplayState.ExplicitHere : ChestDestinationDisplayState.Neutral));
        buttonY += rowHeight + rowGap;

        buttons.Add(new ChestDestinationButton(
            ChestDestinationSelection.All,
            "",
            this.Tr("chest_destination.all"),
            new Rectangle(contentX, buttonY, contentWidth, rowHeight),
            defaultPointsHere,
            Enabled: true,
            DisplayState: defaultPointsHere ? ChestDestinationDisplayState.ExplicitHere : ChestDestinationDisplayState.Neutral));
        buttonY += rowHeight + rowGap;

        int companionIndex = 0;
        foreach (SquadMemberState member in companions)
        {
            this.TryGetOperationalProfile(ownerId, member.NpcName, out CompanionOperationalProfileState? profile);
            bool active = DestinationMatchesChest(
                profile?.ChestDestination,
                context.ChestId);
            ChestDestinationDisplayState displayState = active
                ? ChestDestinationDisplayState.ExplicitHere
                : profile?.ChestDestination is not null
                    ? ChestDestinationDisplayState.OverrideElsewhere
                    : defaultPointsHere
                        ? ChestDestinationDisplayState.InheritedHere
                        : ChestDestinationDisplayState.Neutral;
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
                Enabled: true,
                DisplayState: displayState));
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
        bool showNavigation = pageCount > 1;
        List<SquadMemberState> pageMembers = companions
            .Skip(this.compactChestDestinationPage * CompactChestDestinationPageSize)
            .Take(CompactChestDestinationPageSize)
            .ToList();

        int viewportWidth = Math.Max(1, Game1.uiViewport.Width);
        int viewportHeight = Math.Max(1, Game1.uiViewport.Height);
        int companionRowSlots = showNavigation
            ? CompactChestDestinationPageSize
            : pageMembers.Count;
        int rowCount = 1 + companionRowSlots + (showNavigation ? 1 : 0);
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

        int padding = Math.Min(10, Math.Max(0, (maximumPanelHeight - rowCount - 1) / 8));
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
        int rowHeight = Math.Min(26, Math.Max(1, availableRowsHeight / Math.Max(1, rowCount)));
        int panelHeight = Math.Min(
            maximumPanelHeight,
            padding * 2
                + titleHeight
                + rowCount * rowHeight
                + gap * Math.Max(0, rowCount - 1));

        int x = horizontalMargin;
        int menuLeft = context.Menu.xPositionOnScreen;
        int menuRight = menuLeft + context.Menu.width;
        if (menuRight + ChestDestinationPanelMargin + panelWidth <= viewportWidth - horizontalMargin)
            x = menuRight + ChestDestinationPanelMargin;
        else if (menuLeft - ChestDestinationPanelMargin - panelWidth >= horizontalMargin)
            x = menuLeft - ChestDestinationPanelMargin - panelWidth;
        x = Math.Clamp(x, horizontalMargin, Math.Max(horizontalMargin, viewportWidth - panelWidth - horizontalMargin));
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
        bool nothingPointsHere = !defaultPointsHere && !anyIndividualPointsHere;

        int sharedGap = Math.Min(gap, Math.Max(0, contentWidth - 2));
        int sharedWidth = Math.Max(1, (contentWidth - sharedGap) / 2);
        List<ChestDestinationButton> buttons = new(pageMembers.Count + 5)
        {
            new ChestDestinationButton(
                ChestDestinationSelection.None,
                "",
                this.Tr("chest_destination.none_short"),
                new Rectangle(contentX, buttonY, sharedWidth, rowHeight),
                nothingPointsHere,
                Enabled: true,
                DisplayState: nothingPointsHere ? ChestDestinationDisplayState.ExplicitHere : ChestDestinationDisplayState.Neutral),
            new ChestDestinationButton(
                ChestDestinationSelection.All,
                "",
                this.Tr("chest_destination.all_short"),
                new Rectangle(
                    contentX + sharedWidth + sharedGap,
                    buttonY,
                    Math.Max(1, contentWidth - sharedWidth - sharedGap),
                rowHeight),
                defaultPointsHere,
                Enabled: true,
                DisplayState: defaultPointsHere ? ChestDestinationDisplayState.ExplicitHere : ChestDestinationDisplayState.Neutral)
        };
        buttonY += rowHeight + gap;

        foreach (SquadMemberState member in pageMembers)
        {
            this.TryGetOperationalProfile(ownerId, member.NpcName, out CompanionOperationalProfileState? profile);
            bool active = DestinationMatchesChest(profile?.ChestDestination, context.ChestId);
            ChestDestinationDisplayState displayState = active
                ? ChestDestinationDisplayState.ExplicitHere
                : profile?.ChestDestination is not null
                    ? ChestDestinationDisplayState.OverrideElsewhere
                    : defaultPointsHere
                        ? ChestDestinationDisplayState.InheritedHere
                        : ChestDestinationDisplayState.Neutral;
            buttons.Add(new ChestDestinationButton(
                ChestDestinationSelection.Companion,
                member.NpcName,
                member.DisplayName,
                new Rectangle(contentX, buttonY, contentWidth, rowHeight),
                active,
                Enabled: true,
                DisplayState: displayState));
            buttonY += rowHeight + gap;
        }

        for (int emptyRow = pageMembers.Count; emptyRow < companionRowSlots; emptyRow++)
            buttonY += rowHeight + gap;

        if (showNavigation)
        {
            int navigationGap = Math.Min(gap, Math.Max(0, (contentWidth - 3) / 2));
            int navigationWidth = Math.Max(1, (contentWidth - navigationGap * 2) / 3);
            int pageNumber = this.compactChestDestinationPage + 1;
            buttons.Add(new ChestDestinationButton(
                ChestDestinationSelection.PreviousPage,
                "",
                this.Tr("chest_destination.previous"),
                new Rectangle(contentX, buttonY, navigationWidth, rowHeight),
                Active: false,
                Enabled: this.compactChestDestinationPage > 0,
                DisplayState: ChestDestinationDisplayState.Neutral));
            buttons.Add(new ChestDestinationButton(
                ChestDestinationSelection.PageIndicator,
                "",
                this.Tr("chest_destination.page", new { current = pageNumber, total = pageCount }),
                new Rectangle(contentX + navigationWidth + navigationGap, buttonY, navigationWidth, rowHeight),
                Active: false,
                Enabled: false,
                DisplayState: ChestDestinationDisplayState.Neutral));
            int nextX = contentX + (navigationWidth + navigationGap) * 2;
            buttons.Add(new ChestDestinationButton(
                ChestDestinationSelection.NextPage,
                "",
                this.Tr("chest_destination.next"),
                new Rectangle(nextX, buttonY, Math.Max(1, contentX + contentWidth - nextX), rowHeight),
                Active: false,
                Enabled: this.compactChestDestinationPage < pageCount - 1,
                DisplayState: ChestDestinationDisplayState.Neutral));
        }

        return new ChestDestinationLayout(panel, titleBounds, buttons, Compact: true);
    }

    private void DrawChestDestinationPanel(
        SpriteBatch spriteBatch,
        ChestDestinationLayout layout,
        ItemGrabMenu menu)
    {
        Rectangle panel = layout.Panel;
        Color textColor = new(91, 57, 36);
        Color gold = new(245, 190, 70);
        Color green = new(130, 172, 116);
        Point mouse = new(Game1.getMouseX(true), Game1.getMouseY(true));
        PendingChestDestinationHandshake? pending = this.pendingChestDestinationHandshake;
        bool interactionLocked = pending is not null && ReferenceEquals(pending.Menu, menu);

        if (panel.Width >= 40 && panel.Height >= 32)
        {
            IClickableMenu.drawTextureBox(
                spriteBatch,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                panel.X,
                panel.Y,
                panel.Width,
                panel.Height,
                Color.White);
        }
        else
        {
            DrawChestDestinationFlatPanel(
                spriteBatch,
                panel,
                new Color(250, 221, 164),
                new Color(91, 57, 36),
                1);
        }

        Rectangle titleBounds = layout.TitleBounds;
        int iconSize = Math.Clamp(titleBounds.Height - (layout.Compact ? 8 : 10), 10, 20);
        Rectangle iconBounds = new(
            titleBounds.X + 1,
            titleBounds.Center.Y - iconSize / 2 - (layout.Compact ? 0 : 1),
            iconSize,
            iconSize);
        DrawChestDestinationIcon(spriteBatch, iconBounds);

        Rectangle titleTextBounds = new(
            iconBounds.Right + 6,
            titleBounds.Y,
            Math.Max(1, titleBounds.Right - iconBounds.Right - 7),
            titleBounds.Height);
        string titleText = this.Tr(layout.Compact
            ? "chest_destination.title_short"
            : "chest_destination.title");
        if (layout.Compact)
        {
            DrawCenteredChestDestinationText(
                spriteBatch,
                titleText,
                Game1.tinyFont,
                titleTextBounds,
                textColor,
                ChestDestinationTitleTextScale,
                2,
                2,
                minimumScale: 0.56f);
        }
        else
        {
            Rectangle titleLine = new(titleTextBounds.X, titleTextBounds.Y, titleTextBounds.Width, 16);
            DrawChestDestinationTextInBounds(
                spriteBatch,
                titleText,
                Game1.tinyFont,
                titleLine,
                textColor,
                ChestDestinationTitleTextScale,
                0.56f,
                centered: false);
            string subtitle = this.Tr(interactionLocked
                ? "chest_destination.pending"
                : "chest_destination.subtitle");
            Rectangle subtitleLine = new(
                titleTextBounds.X,
                titleTextBounds.Y + 16,
                titleTextBounds.Width,
                Math.Max(1, titleTextBounds.Height - 18));
            DrawChestDestinationTextInBounds(
                spriteBatch,
                subtitle,
                Game1.tinyFont,
                subtitleLine,
                interactionLocked ? new Color(145, 94, 32) : new Color(96, 88, 78),
                ChestDestinationMetaTextScale,
                0.42f,
                centered: false);
        }

        int dividerY = titleBounds.Bottom - 3;
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(titleBounds.X + 2, dividerY, Math.Max(1, titleBounds.Width - 4), 2),
            gold * 0.72f);
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(titleBounds.Center.X - Math.Min(24, titleBounds.Width / 6), dividerY, Math.Min(48, titleBounds.Width / 3), 2),
            green);

        bool companionSectionStarted = false;
        foreach (ChestDestinationButton button in layout.Buttons)
        {
            if (!companionSectionStarted && button.Selection == ChestDestinationSelection.Companion)
            {
                companionSectionStarted = true;
                int sectionY = Math.Max(titleBounds.Bottom, button.Bounds.Y - 1);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(panel.X + 14, sectionY, Math.Max(1, panel.Width - 28), 1),
                    new Color(143, 103, 64) * 0.42f);
            }

            bool assignmentButton = button.Selection is ChestDestinationSelection.None
                or ChestDestinationSelection.All
                or ChestDestinationSelection.Companion;
            bool pendingButton = interactionLocked
                && assignmentButton
                && pending!.Selection == button.Selection
                && string.Equals(pending.NpcName, button.NpcName, StringComparison.OrdinalIgnoreCase);
            bool visuallyEnabled = button.Enabled && (!interactionLocked || !assignmentButton || pendingButton);
            bool hovered = visuallyEnabled && button.Bounds.Contains(mouse);
            this.DrawChestDestinationButton(
                spriteBatch,
                button,
                hovered,
                visuallyEnabled,
                pendingButton,
                layout.Compact);
        }
    }

    private void DrawChestDestinationButton(
        SpriteBatch spriteBatch,
        ChestDestinationButton button,
        bool hovered,
        bool visuallyEnabled,
        bool pending,
        bool compact)
    {
        Color textColor = new(91, 57, 36);
        Color mutedTextColor = new(112, 98, 82);
        Color border = new(143, 103, 64);
        Color fill = new(235, 210, 170);
        bool navigation = button.Selection is ChestDestinationSelection.PreviousPage
            or ChestDestinationSelection.NextPage;
        bool pageIndicator = button.Selection == ChestDestinationSelection.PageIndicator;

        if (pageIndicator)
        {
            DrawChestDestinationFlatPanel(
                spriteBatch,
                button.Bounds,
                new Color(255, 238, 185),
                new Color(181, 135, 61),
                1);
            DrawCenteredChestDestinationText(
                spriteBatch,
                button.Label,
                Game1.tinyFont,
                button.Bounds,
                textColor,
                ChestDestinationNumberTextScale,
                3,
                2,
                minimumScale: 0.58f);
            return;
        }

        if (pending)
        {
            fill = new Color(250, 215, 135);
            border = new Color(181, 135, 61);
        }
        else if (!visuallyEnabled)
        {
            fill = new Color(215, 205, 185);
            border = new Color(155, 139, 116);
        }
        else if (button.DisplayState == ChestDestinationDisplayState.ExplicitHere)
        {
            fill = new Color(48, 118, 70);
            border = new Color(36, 88, 54);
        }
        else if (button.DisplayState == ChestDestinationDisplayState.InheritedHere)
        {
            fill = new Color(250, 222, 154);
            border = new Color(199, 146, 52);
        }
        else if (button.DisplayState == ChestDestinationDisplayState.OverrideElsewhere)
        {
            fill = new Color(218, 216, 203);
            border = new Color(105, 131, 145);
        }
        else if (hovered)
        {
            fill = new Color(255, 230, 190);
            border = button.Selection == ChestDestinationSelection.None
                ? new Color(198, 94, 82)
                : new Color(181, 135, 61);
        }

        if (button.Bounds.Width >= 8 && button.Bounds.Height >= 8)
        {
            spriteBatch.Draw(
                Game1.staminaRect,
                new Rectangle(button.Bounds.X + 2, button.Bounds.Y + 2, button.Bounds.Width, button.Bounds.Height),
                Color.Black * 0.16f);
        }
        DrawChestDestinationFlatPanel(spriteBatch, button.Bounds, fill, border, hovered ? 2 : 1);
        if (button.Bounds.Width >= 8 && button.Bounds.Height >= 7)
        {
            spriteBatch.Draw(
                Game1.staminaRect,
                new Rectangle(button.Bounds.X + 3, button.Bounds.Y + 3, Math.Max(1, button.Bounds.Width - 6), 1),
                Color.White * (button.DisplayState == ChestDestinationDisplayState.ExplicitHere ? 0.20f : 0.48f));
        }

        Color labelColor = pending
            ? textColor
            : button.DisplayState == ChestDestinationDisplayState.ExplicitHere && visuallyEnabled
                ? Color.White
                : visuallyEnabled ? textColor : mutedTextColor;
        if (navigation)
        {
            DrawCenteredChestDestinationText(
                spriteBatch,
                button.Label,
                Game1.tinyFont,
                button.Bounds,
                labelColor,
                compact ? ChestDestinationBodyTextScale : ChestDestinationTitleTextScale,
                6,
                3,
                minimumScale: 0.48f);
            return;
        }

        int glyphSize = Math.Clamp(button.Bounds.Height - 12, 8, 14);
        Rectangle glyph = new(
            button.Bounds.X + 6,
            button.Bounds.Center.Y - glyphSize / 2,
            glyphSize,
            glyphSize);
        DrawChestDestinationStateGlyph(spriteBatch, glyph, button.DisplayState, pending, visuallyEnabled);

        string stateLabel = button.DisplayState switch
        {
            ChestDestinationDisplayState.InheritedHere => this.Tr("chest_destination.inherited"),
            ChestDestinationDisplayState.OverrideElsewhere => this.Tr("chest_destination.other"),
            _ => ""
        };
        int rightPadding = 7;
        int badgeWidth = 0;
        Rectangle badge = new();
        if (!string.IsNullOrEmpty(stateLabel) && button.Bounds.Width >= 118)
        {
            badgeWidth = Math.Min(
                button.Bounds.Width / 2,
                (int)Math.Ceiling(Game1.tinyFont.MeasureString(stateLabel).X * ChestDestinationMetaTextScale) + 10);
            badge = new Rectangle(
                button.Bounds.Right - badgeWidth - 5,
                button.Bounds.Center.Y - Math.Min(16, button.Bounds.Height - 6) / 2,
                badgeWidth,
                Math.Min(16, button.Bounds.Height - 6));
            Color badgeFill = button.DisplayState == ChestDestinationDisplayState.InheritedHere
                ? new Color(245, 190, 70)
                : new Color(177, 203, 214);
            DrawChestDestinationFlatPanel(spriteBatch, badge, badgeFill, Color.Lerp(badgeFill, textColor, 0.35f), 1);
            DrawCenteredChestDestinationText(
                spriteBatch,
                stateLabel,
                Game1.tinyFont,
                badge,
                textColor,
                ChestDestinationMetaTextScale,
                5,
                2,
                minimumScale: 0.42f);
            rightPadding += badgeWidth + 4;
        }

        Rectangle labelBounds = new(
            glyph.Right + 6,
            button.Bounds.Y,
            Math.Max(1, button.Bounds.Right - rightPadding - glyph.Right - 6),
            button.Bounds.Height);
        DrawChestDestinationTextInBounds(
            spriteBatch,
            button.Label,
            Game1.tinyFont,
            labelBounds,
            labelColor,
            compact ? 0.58f : ChestDestinationBodyTextScale,
            0.46f,
            centered: false);
    }

    private static void DrawChestDestinationStateGlyph(
        SpriteBatch spriteBatch,
        Rectangle bounds,
        ChestDestinationDisplayState state,
        bool pending,
        bool enabled)
    {
        Color ink = new(91, 57, 36);
        if (pending)
        {
            DrawChestDestinationFlatPanel(spriteBatch, bounds, new Color(245, 190, 70), new Color(145, 94, 32), 1);
            int dot = Math.Max(2, bounds.Width / 4);
            spriteBatch.Draw(
                Game1.staminaRect,
                new Rectangle(bounds.Center.X - dot / 2, bounds.Center.Y - dot / 2, dot, dot),
                ink);
            return;
        }

        if (!enabled)
        {
            DrawChestDestinationFlatPanel(spriteBatch, bounds, new Color(205, 197, 180), new Color(145, 132, 112), 1);
            return;
        }

        switch (state)
        {
            case ChestDestinationDisplayState.ExplicitHere:
                DrawChestDestinationFlatPanel(spriteBatch, bounds, Color.White * 0.92f, Color.White, 1);
                int stroke = Math.Max(1, bounds.Width / 7);
                Color checkColor = new(48, 118, 70);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.X + 2, bounds.Center.Y, stroke * 2, stroke),
                    checkColor);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.X + 2 + stroke, bounds.Center.Y + stroke, stroke * 2, stroke),
                    checkColor);
                for (int step = 0; step < 3; step++)
                {
                    spriteBatch.Draw(
                        Game1.staminaRect,
                        new Rectangle(
                            bounds.Center.X - 1 + step * stroke,
                            bounds.Center.Y - step * stroke,
                            stroke * 2,
                            stroke),
                        checkColor);
                }
                break;
            case ChestDestinationDisplayState.InheritedHere:
                DrawChestDestinationFlatPanel(spriteBatch, bounds, new Color(245, 190, 70), new Color(181, 135, 61), 1);
                int inheritedDot = Math.Max(1, bounds.Width / 6);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.Center.X - inheritedDot / 2, bounds.Y + 2, inheritedDot, inheritedDot),
                    ink);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.X + 2, bounds.Center.Y, Math.Max(1, bounds.Width - 4), 1),
                    ink);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.X + 2, bounds.Center.Y + 2, inheritedDot, inheritedDot),
                    ink);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.Right - inheritedDot - 2, bounds.Center.Y + 2, inheritedDot, inheritedDot),
                    ink);
                break;
            case ChestDestinationDisplayState.OverrideElsewhere:
                DrawChestDestinationFlatPanel(spriteBatch, bounds, new Color(177, 203, 214), new Color(91, 126, 145), 1);
                int arrowStroke = Math.Max(1, bounds.Width / 7);
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.X + 2, bounds.Center.Y - arrowStroke / 2, Math.Max(1, bounds.Width - 5), arrowStroke),
                    ink);
                for (int step = 0; step < 3; step++)
                {
                    int arrowX = bounds.Right - 3 - step * arrowStroke;
                    int offsetY = step * arrowStroke;
                    spriteBatch.Draw(
                        Game1.staminaRect,
                        new Rectangle(arrowX, bounds.Center.Y - offsetY, arrowStroke * 2, arrowStroke),
                        ink);
                    spriteBatch.Draw(
                        Game1.staminaRect,
                        new Rectangle(arrowX, bounds.Center.Y + offsetY, arrowStroke * 2, arrowStroke),
                        ink);
                }
                break;
            default:
                DrawChestDestinationFlatPanel(spriteBatch, bounds, new Color(255, 239, 200), new Color(143, 103, 64), 1);
                break;
        }
    }

    private static void DrawChestDestinationIcon(SpriteBatch spriteBatch, Rectangle bounds)
    {
        Color outline = new(91, 57, 36);
        Color wood = new(205, 126, 52);
        Color lightWood = new(239, 168, 74);
        spriteBatch.Draw(Game1.staminaRect, new Rectangle(bounds.X + 1, bounds.Y + 2, bounds.Width, bounds.Height), Color.Black * 0.18f);
        DrawChestDestinationFlatPanel(spriteBatch, bounds, wood, outline, 1);
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 2, bounds.Y + 2, Math.Max(1, bounds.Width - 4), Math.Max(2, bounds.Height / 3)),
            lightWood);
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 1, bounds.Y + Math.Max(3, bounds.Height / 3), Math.Max(1, bounds.Width - 2), 2),
            outline);
        int lockSize = Math.Clamp(bounds.Width / 4, 2, 5);
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.Center.X - lockSize / 2, bounds.Center.Y, lockSize, lockSize),
            new Color(245, 190, 70));
    }

    private static void DrawChestDestinationFlatPanel(
        SpriteBatch spriteBatch,
        Rectangle bounds,
        Color fill,
        Color border,
        int borderSize)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        spriteBatch.Draw(Game1.staminaRect, bounds, border);
        int maximumInset = Math.Min((bounds.Width - 1) / 2, (bounds.Height - 1) / 2);
        if (maximumInset <= 0)
            return;
        int inset = Math.Clamp(borderSize, 1, maximumInset);
        Rectangle inner = new(
            bounds.X + inset,
            bounds.Y + inset,
            Math.Max(1, bounds.Width - inset * 2),
            Math.Max(1, bounds.Height - inset * 2));
        spriteBatch.Draw(Game1.staminaRect, inner, fill);
    }

    private static void DrawChestDestinationTextInBounds(
        SpriteBatch spriteBatch,
        string text,
        SpriteFont font,
        Rectangle bounds,
        Color color,
        float preferredScale,
        float minimumScale,
        bool centered)
    {
        float scale = GetChestDestinationTextScale(
            text,
            font,
            preferredScale,
            bounds.Width,
            bounds.Height,
            minimumScale);
        string fitted = FitChestDestinationText(text, font, bounds.Width, scale);
        Vector2 size = font.MeasureString(fitted) * scale;
        float x = centered ? bounds.Center.X - size.X / 2f : bounds.X;
        float y = bounds.Center.Y - size.Y / 2f;
        DrawChestDestinationText(spriteBatch, fitted, font, new Vector2(x, y), color, scale);
    }

    private static void DrawCenteredChestDestinationText(
        SpriteBatch spriteBatch,
        string text,
        SpriteFont font,
        Rectangle bounds,
        Color color,
        float preferredScale,
        int horizontalPadding,
        int verticalPadding,
        float minimumScale)
    {
        Rectangle inner = new(
            bounds.X + horizontalPadding / 2,
            bounds.Y + verticalPadding / 2,
            Math.Max(1, bounds.Width - horizontalPadding),
            Math.Max(1, bounds.Height - verticalPadding));
        DrawChestDestinationTextInBounds(
            spriteBatch,
            text,
            font,
            inner,
            color,
            preferredScale,
            minimumScale,
            centered: true);
    }

    private static void DrawChestDestinationText(
        SpriteBatch spriteBatch,
        string text,
        SpriteFont font,
        Vector2 position,
        Color color,
        float scale)
    {
        if (string.IsNullOrEmpty(text) || scale <= 0f)
            return;
        Vector2 snapped = new(MathF.Round(position.X), MathF.Round(position.Y));
        spriteBatch.DrawString(
            font,
            text,
            snapped + Vector2.One,
            Color.Black * 0.24f,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);
        spriteBatch.DrawString(font, text, snapped, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private static float GetChestDestinationTextScale(
        string text,
        SpriteFont font,
        float preferredScale,
        int maximumWidth,
        int maximumHeight,
        float minimumScale)
    {
        if (maximumWidth <= 0 || maximumHeight <= 0 || preferredScale <= 0f)
            return 0f;
        Vector2 natural = font.MeasureString(text);
        float widthScale = natural.X <= 0f ? preferredScale : maximumWidth / natural.X;
        float heightScale = natural.Y <= 0f ? preferredScale : maximumHeight / natural.Y;
        float widthConstrained = Math.Min(
            preferredScale,
            Math.Max(Math.Min(preferredScale, minimumScale), widthScale));
        return Math.Max(0f, Math.Min(heightScale, widthConstrained));
    }

    private static string FitChestDestinationText(string text, SpriteFont font, int maximumWidth, float scale)
    {
        if (string.IsNullOrEmpty(text) || maximumWidth <= 0 || scale <= 0f)
            return "";
        float unscaledWidth = maximumWidth / scale;
        if (font.MeasureString(text).X <= unscaledWidth)
            return text;

        const string ellipsis = "…";
        string trimmed = text;
        while (trimmed.Length > 0 && font.MeasureString(trimmed + ellipsis).X > unscaledWidth)
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
