using Microsoft.Xna.Framework;
using StardewValley;

namespace PelicanCompanions;

internal sealed partial class CompanionPanelMenu
{
    private static string GetInventoryFilterTranslationKey(CompanionInventoryFilter filter)
    {
        return filter switch
        {
            CompanionInventoryFilter.DepositWood => "companion.inventory.filter_wood",
            CompanionInventoryFilter.DepositMinerals => "companion.inventory.filter_minerals",
            CompanionInventoryFilter.KeepFood => "companion.inventory.filter_food",
            _ => "companion.inventory.filter_unknown"
        };
    }

    private bool TryBeginInventoryDrag(SquadMemberState member, int x, int y)
    {
        if (this.inventoryDrag is not null)
        {
            if (this.TryGetInventoryEndpointAt(x, y, out CompanionInventoryEndpoint destination))
                this.CompleteInventoryDrag(member, destination);
            else
                this.inventoryDrag = null;
            return true;
        }

        CompanionInventoryWorkspace workspace = this.GetInventoryWorkspaceForPanel(
            member,
            forceRefresh: true);
        if (!this.TryGetInventorySourceAt(
                workspace,
                x,
                y,
                out CompanionInventoryEndpoint source,
                out int sourceIndex,
                out Item? item,
                out string expectedItemToken,
                out Rectangle sourceBounds)
            || item is null
            || string.IsNullOrWhiteSpace(expectedItemToken))
        {
            return false;
        }

        this.inventoryDrag = new InventoryDragState(
            member.NpcName,
            source,
            sourceIndex,
            item,
            expectedItemToken,
            workspace.ChestId,
            workspace.ChestLocationName,
            workspace.ChestTileX,
            workspace.ChestTileY,
            sourceBounds,
            this.activatingFocusedControl);
        this.focusInventoryDestinationOnNextRebuild =
            this.activatingFocusedControl;
        Game1.playSound("dwop");
        return true;
    }

    public override void releaseLeftClick(int x, int y)
    {
        if (this.inventoryDrag is not InventoryDragState drag)
        {
            base.releaseLeftClick(x, y);
            return;
        }

        SquadMemberState? member = this.GetSelectedMember();
        if (member is null
            || !string.Equals(member.NpcName, drag.NpcName, StringComparison.OrdinalIgnoreCase))
        {
            this.inventoryDrag = null;
            this.focusInventoryDestinationOnNextRebuild = false;
            return;
        }

        CompanionInventoryEndpoint destination;
        if (!this.TryGetInventoryEndpointAt(x, y, out destination)
            || destination == drag.Source)
        {
            if (!drag.SourceBounds.Contains(x, y))
            {
                this.inventoryDrag = null;
                this.focusInventoryDestinationOnNextRebuild = false;
                Game1.playSound("bigDeSelect");
                return;
            }

            destination = GetQuickTransferDestination(drag.Source);
        }

        this.CompleteInventoryDrag(member, destination);
    }

    private void CompleteInventoryDrag(
        SquadMemberState member,
        CompanionInventoryEndpoint destination)
    {
        if (this.inventoryDrag is not InventoryDragState drag)
            return;

        this.inventoryDrag = null;
        this.focusInventoryDestinationOnNextRebuild = false;
        if (!string.Equals(
                member.NpcName,
                drag.NpcName,
                StringComparison.OrdinalIgnoreCase)
            || destination == drag.Source)
        {
            Game1.playSound("bigDeSelect");
            return;
        }

        bool accepted = this.transferInventoryItem(
            member,
            new CompanionInventoryTransferRequest(
                drag.Source,
                destination,
                drag.SourceIndex,
                drag.ExpectedItemToken,
                drag.ChestId,
                drag.ChestLocationName,
                drag.ChestTileX,
                drag.ChestTileY));
        this.InvalidateInventoryWorkspaceCache();
        Game1.playSound(accepted ? "coin" : "cancel");
    }

    private bool TryGetInventorySourceAt(
        CompanionInventoryWorkspace workspace,
        int x,
        int y,
        out CompanionInventoryEndpoint endpoint,
        out int index,
        out Item? item,
        out string expectedItemToken,
        out Rectangle bounds)
    {
        foreach ((Rectangle candidate, int candidateIndex) in this.inventorySlotsBounds)
        {
            if (!candidate.Contains(x, y))
                continue;
            endpoint = CompanionInventoryEndpoint.Companion;
            index = candidateIndex;
            item = candidateIndex >= 0 && candidateIndex < workspace.CompanionItems.Count
                ? workspace.CompanionItems[candidateIndex]
                : null;
            expectedItemToken = candidateIndex >= 0
                && candidateIndex < workspace.CompanionItemTokens.Count
                    ? workspace.CompanionItemTokens[candidateIndex]
                    : "";
            bounds = candidate;
            return item is not null;
        }

        foreach ((Rectangle candidate, int candidateIndex) in this.playerInventorySlotsBounds)
        {
            if (!candidate.Contains(x, y))
                continue;
            endpoint = CompanionInventoryEndpoint.Player;
            index = candidateIndex;
            item = candidateIndex >= 0 && candidateIndex < workspace.PlayerItems.Count
                ? workspace.PlayerItems[candidateIndex]
                : null;
            expectedItemToken = candidateIndex >= 0
                && candidateIndex < workspace.PlayerItemTokens.Count
                    ? workspace.PlayerItemTokens[candidateIndex]
                    : "";
            bounds = candidate;
            return item is not null;
        }

        foreach ((Rectangle candidate, int candidateIndex) in this.chestInventorySlotsBounds)
        {
            if (!candidate.Contains(x, y))
                continue;
            endpoint = CompanionInventoryEndpoint.Chest;
            index = candidateIndex;
            item = candidateIndex >= 0 && candidateIndex < workspace.ChestItems.Count
                ? workspace.ChestItems[candidateIndex]
                : null;
            expectedItemToken = candidateIndex >= 0
                && candidateIndex < workspace.ChestItemTokens.Count
                    ? workspace.ChestItemTokens[candidateIndex]
                    : "";
            bounds = candidate;
            return item is not null;
        }

        endpoint = default;
        index = -1;
        item = null;
        expectedItemToken = "";
        bounds = new Rectangle();
        return false;
    }

    private bool TryGetInventoryEndpointAt(
        int x,
        int y,
        out CompanionInventoryEndpoint endpoint)
    {
        foreach ((Rectangle bounds, CompanionInventoryEndpoint candidate) in this.inventoryPaneBounds)
        {
            if (!bounds.Contains(x, y))
                continue;
            endpoint = candidate;
            return true;
        }

        endpoint = default;
        return false;
    }

    private static CompanionInventoryEndpoint GetQuickTransferDestination(
        CompanionInventoryEndpoint source)
    {
        return source switch
        {
            CompanionInventoryEndpoint.Player => CompanionInventoryEndpoint.Companion,
            CompanionInventoryEndpoint.Companion => CompanionInventoryEndpoint.Player,
            CompanionInventoryEndpoint.Chest => CompanionInventoryEndpoint.Companion,
            _ => CompanionInventoryEndpoint.Companion
        };
    }

    private CompanionInventoryWorkspace GetInventoryWorkspaceForPanel(
        SquadMemberState member,
        bool forceRefresh = false)
    {
        if (!forceRefresh
            && this.inventoryWorkspaceCache is not null
            && string.Equals(
                this.inventoryWorkspaceCacheNpcName,
                member.NpcName,
                StringComparison.OrdinalIgnoreCase)
            && Game1.ticks <= this.inventoryWorkspaceCacheUntilTick)
        {
            return this.inventoryWorkspaceCache;
        }

        CompanionInventoryWorkspace workspace = this.getInventoryWorkspace(member);
        this.inventoryWorkspaceCache = workspace;
        this.inventoryWorkspaceCacheNpcName = member.NpcName;
        this.inventoryWorkspaceCacheUntilTick = Game1.ticks + 10;
        return workspace;
    }

    private void InvalidateInventoryWorkspaceCache()
    {
        this.inventoryWorkspaceCache = null;
        this.inventoryWorkspaceCacheNpcName = "";
        this.inventoryWorkspaceCacheUntilTick = 0;
    }

    private void ResetInventoryViewport()
    {
        this.focusInventoryDestinationOnNextRebuild = false;
        this.controllerInventoryPageEndpoint = null;
        this.inventoryPageOffsets.Clear();
        this.InvalidateInventoryWorkspaceCache();
    }

    private bool TryScrollInventoryPane(
        int direction,
        int x,
        int y,
        bool rememberFocusedPane = false)
    {
        if (direction == 0)
            return false;

        InventoryPanePageState? page = this.inventoryPanePages
            .LastOrDefault(candidate => candidate.Bounds.Contains(x, y));
        if (page is null)
            return false;

        int totalRows = (int)Math.Ceiling(
            page.TotalSlots / (double)Math.Max(1, page.Columns));
        int visibleRows = Math.Max(
            1,
            page.VisibleCapacity / Math.Max(1, page.Columns));
        int maxOffset = Math.Max(0, totalRows - visibleRows)
            * Math.Max(1, page.Columns);
        int delta = direction > 0 ? -page.Columns : page.Columns;
        int nextOffset = Math.Clamp(
            page.Offset + delta,
            0,
            maxOffset);
        if (nextOffset == page.Offset)
            return false;

        this.inventoryPageOffsets[page.Endpoint] = nextOffset;
        if (rememberFocusedPane)
        {
            this.controllerInventoryPageEndpoint = page.Endpoint;
            this.controllerInventoryPageFocus =
                this.focusedControlIndex >= 0
                    && this.focusedControlIndex < this.focusTargets.Count
                        ? this.focusTargets[this.focusedControlIndex].Center
                        : page.Bounds.Center;
        }
        else
        {
            this.controllerInventoryPageEndpoint = null;
        }
        Game1.playSound("shwip");
        return true;
    }

    private bool TryScrollFocusedInventoryPane(int direction)
    {
        if (this.inventoryPanePages.Count == 0)
            return false;

        InventoryPanePageState? remembered =
            this.controllerInventoryPageEndpoint is CompanionInventoryEndpoint endpoint
                ? this.inventoryPanePages.FirstOrDefault(
                    page => page.Endpoint == endpoint)
                : null;
        Point focus = remembered?.Bounds.Center
            ?? (this.focusedControlIndex >= 0
                && this.focusedControlIndex < this.focusTargets.Count
                    ? this.focusTargets[this.focusedControlIndex].Center
                    : this.inventoryPanePages[0].Bounds.Center);
        return this.TryScrollInventoryPane(
            direction,
            focus.X,
            focus.Y,
            rememberFocusedPane: true);
    }
}
