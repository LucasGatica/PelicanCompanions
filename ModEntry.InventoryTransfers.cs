using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const float InventoryChestInteractionDistance = 4f;
    private readonly HashSet<string> inventoryTransferTokenWarnings = new(StringComparer.Ordinal);

    private static bool TryParseInventoryTransferEndpoints(
        string? value,
        out CompanionInventoryEndpoint source,
        out CompanionInventoryEndpoint destination)
    {
        source = default;
        destination = default;
        string[] parts = (value ?? "").Split('>', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && Enum.TryParse(parts[0], ignoreCase: true, out source)
            && Enum.IsDefined(source)
            && Enum.TryParse(parts[1], ignoreCase: true, out destination)
            && Enum.IsDefined(destination)
            && source != destination;
    }

    private CompanionInventoryRulesState GetCompanionInventoryRules(SquadMemberState member)
    {
        return this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName).InventoryRules;
    }

    private bool GetCompanionInventoryFilter(
        SquadMemberState member,
        CompanionInventoryFilter filter)
    {
        return CompanionInventoryFilterPolicy.Get(this.GetCompanionInventoryRules(member), filter);
    }

    private bool ToggleCompanionInventoryFilter(
        SquadMemberState member,
        CompanionInventoryFilter filter)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId) || !Enum.IsDefined(filter))
            return false;

        bool desired = !this.GetCompanionInventoryFilter(member, filter);
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "SetInventoryFilter",
                member.NpcName,
                filter.ToString(),
                desiredEnabled: desired);
            return true;
        }

        return this.SetCompanionInventoryFilter(member, ownerId, filter, desired);
    }

    private bool SetCompanionInventoryFilter(
        SquadMemberState member,
        long ownerId,
        CompanionInventoryFilter filter,
        bool enabled)
    {
        if (!this.CanOwnerMutate(member, ownerId, showWarning: false)
            || !Enum.IsDefined(filter))
        {
            return false;
        }

        CompanionInventoryRulesState rules = this.GetCompanionInventoryRules(member);
        if (CompanionInventoryFilterPolicy.Get(rules, filter) == enabled)
            return true;
        if (!CompanionInventoryFilterPolicy.Set(rules, filter, enabled))
            return false;

        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
        {
            this.Info(
                enabled ? "companion.inventory.filter_enabled" : "companion.inventory.filter_disabled",
                new { filter = this.Tr(GetInventoryFilterTranslationKey(filter)) });
        }
        return true;
    }

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

    private CompanionInventoryWorkspace GetCompanionInventoryWorkspace(SquadMemberState member)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        List<Item?> playerItems = owner is null
            ? Array.Empty<Item?>()
                .ToList()
            : owner.Items.Cast<Item?>().ToList();
        List<string> playerTokens = playerItems
            .Select(this.GetInventoryTransferItemToken)
            .ToList();
        List<Item> companionItems = new();
        List<string> companionTokens = new();
        foreach (SavedItemStack saved in member.Inventory)
        {
            Item? item = this.TryCreateItem(saved);
            if (item is null)
                continue;
            companionItems.Add(item);
            companionTokens.Add(SavedItemStackIdentity.CreateToken(saved));
        }

        if (owner is not null
            && this.TryGetNearbyAssignedChest(member, owner, out ResolvedDepositChest resolved)
            && IsChestAvailableForManualTransfer(resolved.Chest))
        {
            List<Item?> chestItems;
            try
            {
                chestItems = resolved.Chest.Items.Cast<Item?>().ToList();
            }
            catch
            {
                chestItems = new List<Item?>();
            }
            List<string> chestTokens = chestItems
                .Select(this.GetInventoryTransferItemToken)
                .ToList();

            return new CompanionInventoryWorkspace(
                playerItems,
                playerTokens,
                companionItems,
                companionTokens,
                chestItems,
                chestTokens,
                ChestAvailable: true,
                ChestDisplayName: resolved.Chest.DisplayName,
                ChestId: resolved.ChestId,
                ChestLocationName: resolved.Location.NameOrUniqueName,
                ChestTileX: (int)resolved.Tile.X,
                ChestTileY: (int)resolved.Tile.Y);
        }

        return new CompanionInventoryWorkspace(
            playerItems,
            playerTokens,
            companionItems,
            companionTokens,
            Array.Empty<Item?>(),
            Array.Empty<string>(),
            ChestAvailable: false,
            ChestDisplayName: "",
            ChestId: "",
            ChestLocationName: "",
            ChestTileX: -1,
            ChestTileY: -1);
    }

    private string GetInventoryTransferItemToken(Item? item)
    {
        if (item is null)
            return "";

        try
        {
            SavedItemStack? saved = this.ToSavedItem(item);
            return saved is null ? "" : SavedItemStackIdentity.CreateToken(saved);
        }
        catch (Exception ex)
        {
            string warningKey = item.GetType().FullName ?? item.GetType().Name;
            if (this.inventoryTransferTokenWarnings.Count < 256
                && this.inventoryTransferTokenWarnings.Add(warningKey))
            {
                this.Monitor.Log(
                    $"An item of type '{warningKey}' can't be transferred through the companion panel and will remain visible but disabled: {ex.Message}",
                    LogLevel.Warn);
            }
            return "";
        }
    }

    private static bool IsChestAvailableForManualTransfer(Chest chest)
    {
        try
        {
            return !chest.GetMutex().IsLocked()
                || chest.GetMutex().IsLockHeld();
        }
        catch
        {
            return false;
        }
    }

    private bool TryTransferInventoryItemFromPanel(
        SquadMemberState member,
        CompanionInventoryTransferRequest request)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId)
            || !Enum.IsDefined(request.Source)
            || !Enum.IsDefined(request.Destination)
            || request.Source == request.Destination
            || request.SourceIndex < 0
            || string.IsNullOrWhiteSpace(request.ExpectedItemToken))
        {
            return false;
        }

        int authoritativeIndex = request.SourceIndex;
        SavedItemStack? saved;
        switch (request.Source)
        {
            case CompanionInventoryEndpoint.Player:
            {
                Farmer? owner = this.GetOwnerFarmer(ownerId);
                Item? source = owner is not null
                    && request.SourceIndex < owner.Items.Count
                        ? owner.Items[request.SourceIndex]
                        : null;
                saved = source is null ? null : this.ToSavedItem(source);
                break;
            }

            case CompanionInventoryEndpoint.Companion:
                if (!this.TryMapVisibleInventoryIndex(
                        member,
                        request.SourceIndex,
                        out authoritativeIndex,
                        out SavedItemStack companionSaved))
                {
                    return false;
                }
                saved = companionSaved;
                break;

            case CompanionInventoryEndpoint.Chest:
            {
                Farmer? owner = this.GetOwnerFarmer(ownerId);
                if (owner is null
                    || !this.TryGetExpectedNearbyChest(
                        member,
                        owner,
                        request.ChestId,
                        request.ChestLocationName,
                        request.ChestTileX,
                        request.ChestTileY,
                        out ResolvedDepositChest resolved)
                    || request.SourceIndex >= resolved.Chest.Items.Count)
                {
                    return false;
                }

                Item? source = resolved.Chest.Items[request.SourceIndex];
                saved = source is null ? null : this.ToSavedItem(source);
                break;
            }

            default:
                return false;
        }

        if (saved is null
            || !SavedItemStackIdentity.Matches(
                saved,
                request.ExpectedItemToken))
            return false;

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "TransferInventory",
                member.NpcName,
                $"{request.Source}>{request.Destination}",
                tile: new Vector2(
                    request.ChestTileX,
                    request.ChestTileY),
                index: authoritativeIndex,
                expectedItemToken: request.ExpectedItemToken,
                expectedStateToken: request.ChestId,
                expectedLocationName: request.ChestLocationName);
            return true;
        }

        return this.TransferInventoryItem(
            member,
            ownerId,
            request.Source,
            request.Destination,
            authoritativeIndex,
            request.ExpectedItemToken,
            request.ChestId,
            request.ChestLocationName,
            request.ChestTileX,
            request.ChestTileY);
    }

    private bool TransferInventoryItem(
        SquadMemberState member,
        long ownerId,
        CompanionInventoryEndpoint sourceEndpoint,
        CompanionInventoryEndpoint destinationEndpoint,
        int sourceIndex,
        string? expectedItemToken,
        string? expectedChestId,
        string? expectedChestLocationName,
        int expectedChestTileX,
        int expectedChestTileY)
    {
        if (!this.CanOwnerMutate(member, ownerId, showWarning: false)
            || !Enum.IsDefined(sourceEndpoint)
            || !Enum.IsDefined(destinationEndpoint)
            || sourceEndpoint == destinationEndpoint
            || sourceIndex < 0)
        {
            return false;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return false;

        ResolvedDepositChest? nearbyChest = null;
        if (sourceEndpoint == CompanionInventoryEndpoint.Chest
            || destinationEndpoint == CompanionInventoryEndpoint.Chest)
        {
            if (!this.TryGetExpectedNearbyChest(
                    member,
                    owner,
                    expectedChestId,
                    expectedChestLocationName,
                    expectedChestTileX,
                    expectedChestTileY,
                    out nearbyChest))
            {
                this.WarnForPlayer(
                    ownerId,
                    "companion.inventory.chest_unavailable");
                return false;
            }

            try
            {
                if (!nearbyChest.Chest.GetMutex().IsLockHeld())
                {
                    ResolvedDepositChest queuedChest = nearbyChest;
                    ChestMutationQueueResult queueResult =
                        this.QueueChestMutation(
                        queuedChest.Chest,
                        executeLocked: () =>
                        {
                            try
                            {
                                if (!this.members.TryGetValue(
                                        member.NpcName,
                                        out SquadMemberState? liveMember)
                                    || !ReferenceEquals(liveMember, member)
                                    || liveMember.OwnerId != ownerId)
                                {
                                    this.WarnForPlayer(
                                        ownerId,
                                        "multiplayer.command_stale");
                                    return false;
                                }

                                Farmer? liveOwner = this.GetOwnerFarmer(ownerId);
                                if (liveOwner is null
                                    || !this.TryGetExpectedNearbyChest(
                                        liveMember,
                                        liveOwner,
                                        expectedChestId,
                                        expectedChestLocationName,
                                        expectedChestTileX,
                                        expectedChestTileY,
                                        out ResolvedDepositChest currentChest)
                                    || !IsSameResolvedDepositChest(
                                        currentChest,
                                        queuedChest)
                                    || !queuedChest.Chest.GetMutex().IsLockHeld())
                                {
                                    this.WarnForPlayer(
                                        ownerId,
                                        "companion.inventory.chest_unavailable");
                                    return false;
                                }

                                return this.TransferInventoryItemLocked(
                                    liveMember,
                                    liveOwner,
                                    currentChest,
                                    sourceEndpoint,
                                    destinationEndpoint,
                                    sourceIndex,
                                    expectedItemToken);
                            }
                            finally
                            {
                                this.SendStateSnapshot(ownerId, force: true);
                            }
                        },
                        lockFailed: () =>
                        {
                            this.WarnForPlayer(
                                ownerId,
                                "companion.inventory.chest_unavailable");
                            this.SendStateSnapshot(ownerId, force: true);
                        },
                        description:
                            $"manual inventory transfer for '{member.NpcName}'");
                    return queueResult
                        != ChestMutationQueueResult.CompletedUnsuccessfully;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log(
                    $"Could not acquire the assigned chest for a manual transfer: {ex.Message}",
                    LogLevel.Warn);
                this.WarnForPlayer(
                    ownerId,
                    "companion.inventory.chest_unavailable");
                return false;
            }
        }

        return this.TransferInventoryItemLocked(
            member,
            owner,
            nearbyChest,
            sourceEndpoint,
            destinationEndpoint,
            sourceIndex,
            expectedItemToken);
    }

    private bool TransferInventoryItemLocked(
        SquadMemberState member,
        Farmer owner,
        ResolvedDepositChest? nearbyChest,
        CompanionInventoryEndpoint sourceEndpoint,
        CompanionInventoryEndpoint destinationEndpoint,
        int sourceIndex,
        string? expectedItemToken)
    {
        long ownerId = member.OwnerId;
        if (!this.TryGetTransferSource(
                member,
                owner,
                nearbyChest,
                sourceEndpoint,
                sourceIndex,
                expectedItemToken,
                out Item sourceItem))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        if (destinationEndpoint == CompanionInventoryEndpoint.Companion
            && !this.CanPersistAsCompanionCargo(sourceItem))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
            {
                this.Warn(sourceItem is Tool
                    ? "companion.inventory.tool_requires_slot"
                    : "companion.inventory.item_unsupported");
            }
            return false;
        }

        int originalStack = Math.Max(1, sourceItem.Stack);
        string displayName = sourceItem.DisplayName;
        if (!this.TryExtractTransferSource(
                member,
                owner,
                nearbyChest,
                sourceEndpoint,
                sourceIndex,
                expectedItemToken,
                out Item extractedItem))
        {
            this.WarnForPlayer(ownerId, "multiplayer.command_stale");
            return false;
        }

        Item? remainder = destinationEndpoint switch
        {
            CompanionInventoryEndpoint.Player =>
                this.AddExactTransferToFarmerInventory(
                    owner,
                    extractedItem,
                    "manual companion inventory transfer"),
            CompanionInventoryEndpoint.Companion =>
                this.AddToCompanionInventory(member, extractedItem),
            CompanionInventoryEndpoint.Chest when nearbyChest is not null =>
                this.AddExactTransferToChest(
                    nearbyChest.Chest,
                    extractedItem,
                    "manual companion inventory transfer"),
            _ => extractedItem
        };
        int remaining = Math.Clamp(remainder?.Stack ?? 0, 0, originalStack);
        int moved = originalStack - remaining;
        if (remainder is not null)
        {
            try
            {
                this.RestoreTransferSource(
                    member,
                    owner,
                    nearbyChest,
                    sourceEndpoint,
                    sourceIndex,
                    remainder);
            }
            catch (Exception ex)
            {
                this.Monitor.Log(
                    $"Inventory transfer rollback failed for '{member.NpcName}'. Preserving the remainder with its owner instead: {ex}",
                    LogLevel.Error);
                this.PreserveFailedTransferRemainder(
                    owner,
                    remainder,
                    "manual companion inventory rollback");
                this.MarkStateDirty();
                this.WarnForPlayer(ownerId, "multiplayer.command_failed");
                return false;
            }
        }

        if (moved <= 0)
        {
            this.WarnForPlayer(
                ownerId,
                "companion.inventory.destination_full");
            return false;
        }

        this.MarkStateDirty();
        this.InfoForPlayer(ownerId, "companion.inventory.transfer_complete", new
        {
            item = displayName,
            count = moved
        });
        return true;
    }

    private bool TryGetTransferSource(
        SquadMemberState member,
        Farmer owner,
        ResolvedDepositChest? nearbyChest,
        CompanionInventoryEndpoint endpoint,
        int index,
        string? expectedToken,
        out Item item)
    {
        item = null!;
        SavedItemStack? saved = null;
        switch (endpoint)
        {
            case CompanionInventoryEndpoint.Player:
                if (index >= owner.Items.Count || owner.Items[index] is not Item playerItem)
                    return false;
                item = playerItem;
                saved = this.ToSavedItem(playerItem);
                break;

            case CompanionInventoryEndpoint.Companion:
                if (index >= member.Inventory.Count)
                    return false;
                saved = member.Inventory[index];
                Item? restored = this.TryCreateItem(saved);
                if (restored is null)
                    return false;
                item = restored;
                break;

            case CompanionInventoryEndpoint.Chest:
                if (nearbyChest is null
                    || index >= nearbyChest.Chest.Items.Count
                    || nearbyChest.Chest.Items[index] is not Item chestItem)
                {
                    return false;
                }
                item = chestItem;
                saved = this.ToSavedItem(chestItem);
                break;
        }

        return saved is not null && SavedItemStackIdentity.Matches(saved, expectedToken);
    }

    private bool TryExtractTransferSource(
        SquadMemberState member,
        Farmer owner,
        ResolvedDepositChest? nearbyChest,
        CompanionInventoryEndpoint endpoint,
        int index,
        string? expectedToken,
        out Item item)
    {
        item = null!;
        switch (endpoint)
        {
            case CompanionInventoryEndpoint.Player:
            {
                if (index >= owner.Items.Count
                    || owner.Items[index] is not Item current
                    || this.ToSavedItem(current) is not SavedItemStack saved
                    || !SavedItemStackIdentity.Matches(saved, expectedToken))
                {
                    return false;
                }

                owner.Items[index] = null;
                item = current;
                return true;
            }

            case CompanionInventoryEndpoint.Companion:
            {
                if (index >= member.Inventory.Count)
                    return false;
                SavedItemStack current = member.Inventory[index];
                if (!SavedItemStackIdentity.Matches(current, expectedToken)
                    || this.TryCreateItem(current) is not Item restored)
                {
                    return false;
                }

                member.Inventory.RemoveAt(index);
                item = restored;
                return true;
            }

            case CompanionInventoryEndpoint.Chest:
            {
                if (nearbyChest is null)
                    return false;
                if (index >= nearbyChest.Chest.Items.Count
                    || nearbyChest.Chest.Items[index] is not Item current
                    || this.ToSavedItem(current) is not SavedItemStack saved
                    || !SavedItemStackIdentity.Matches(saved, expectedToken))
                {
                    return false;
                }

                nearbyChest.Chest.Items.RemoveAt(index);
                item = current;
                return true;
            }

            default:
                return false;
        }
    }

    private void RestoreTransferSource(
        SquadMemberState member,
        Farmer owner,
        ResolvedDepositChest? nearbyChest,
        CompanionInventoryEndpoint endpoint,
        int index,
        Item remainder)
    {
        switch (endpoint)
        {
            case CompanionInventoryEndpoint.Player:
                if (index < 0
                    || index >= owner.Items.Count
                    || owner.Items[index] is not null)
                {
                    throw new InvalidDataException(
                        "The reserved player inventory cell changed during rollback.");
                }
                owner.Items[index] = remainder;
                return;

            case CompanionInventoryEndpoint.Companion:
                SavedItemStack saved = this.ToSavedItem(remainder)
                    ?? throw new InvalidDataException(
                        "The companion remainder could not be serialized during rollback.");
                member.Inventory.Insert(
                    Math.Clamp(index, 0, member.Inventory.Count),
                    saved);
                return;

            case CompanionInventoryEndpoint.Chest:
                if (nearbyChest is null)
                {
                    throw new InvalidDataException(
                        "The assigned chest disappeared during rollback.");
                }
                nearbyChest.Chest.Items.Insert(
                    Math.Clamp(index, 0, nearbyChest.Chest.Items.Count),
                    remainder);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint));
        }
    }

    private void PreserveFailedTransferRemainder(
        Farmer owner,
        Item remainder,
        string context)
    {
        Item? notAdded = this.AddExactTransferToFarmerInventory(
            owner,
            remainder,
            context);
        if (notAdded is null)
            return;

        GameLocation? location = owner.currentLocation;
        if (location is not null)
        {
            this.DropItemSafely(
                notAdded,
                location,
                NormalizeTile(owner.Tile),
                owner.FacingDirection,
                context);
            return;
        }

        SavedItemStack? saved = this.ToSavedItem(notAdded);
        if (saved is not null)
        {
            // A transfer requires an online farmer with a location, so this is
            // only a final defensive fallback for a location disappearing in
            // the same callback.
            this.legacyOverflowItems.Add(saved);
            this.MarkStateDirty();
            return;
        }

        throw new InvalidDataException(
            "The transfer remainder could not be recovered.");
    }

    private bool CanPersistAsCompanionCargo(Item item)
    {
        if (item is Tool
            || item is not SObject obj
            || item.GetType() != typeof(SObject)
                && item.GetType() != typeof(ColoredObject)
            || obj.bigCraftable.Value
            || item.maximumStackSize() <= 1)
        {
            return false;
        }

        SavedItemStack? saved = this.ToSavedItem(item);
        Item? restored = saved is null ? null : this.TryCreateItem(saved);
        SavedItemStack? roundTrip = restored is null ? null : this.ToSavedItem(restored);
        return saved is not null
            && roundTrip is not null
            && restored!.GetType() == item.GetType()
            && SavedItemStackIdentity.Matches(roundTrip, SavedItemStackIdentity.CreateToken(saved));
    }

    /// <summary>
    /// Moves the escrowed instance itself into a farmer inventory. This is
    /// intentionally separate from reward routing, whose clone-first behavior
    /// is useful for reconciling vanilla debris but would discard custom
    /// NetFields on a manual player/chest transfer.
    /// </summary>
    private Item? AddExactTransferToFarmerInventory(
        Farmer owner,
        Item item,
        string context)
    {
        int originalStack = Math.Max(1, item.Stack);
        int before = CountCompatibleFarmerStack(owner, item);
        try
        {
            Item? reportedRemainder = owner.addItemToInventory(item);
            if (ContainsItemReference(owner.Items, item))
                return null;
            if (reportedRemainder is null)
                return null;
            if (ReferenceEquals(reportedRemainder, item))
                return item;

            // Vanilla returns the passed instance, but retain the escrowed
            // object even if a custom inventory implementation returns a copy.
            item.Stack = Math.Clamp(reportedRemainder.Stack, 1, originalStack);
            return item;
        }
        catch (Exception ex)
        {
            int after = CountCompatibleFarmerStack(owner, item);
            int transferred = Math.Clamp(after - before, 0, originalStack);
            this.Monitor.Log(
                $"Farmer inventory transfer failed during {context}; {transferred} committed item(s) were reconciled. {ex}",
                LogLevel.Error);

            if (ContainsItemReference(owner.Items, item))
                return null;

            int remaining = originalStack - transferred;
            if (remaining <= 0)
                return null;
            item.Stack = remaining;
            return item;
        }
    }

    /// <summary>Moves the escrowed instance itself into a locked chest.</summary>
    private Item? AddExactTransferToChest(
        Chest chest,
        Item item,
        string context)
    {
        int originalStack = Math.Max(1, item.Stack);
        int before = CountCompatibleChestStack(chest, item);
        try
        {
            Item? reportedRemainder = chest.addItem(item);
            if (ContainsItemReference(chest.Items, item))
                return null;
            if (reportedRemainder is null)
                return null;
            if (ReferenceEquals(reportedRemainder, item))
                return item;

            item.Stack = Math.Clamp(reportedRemainder.Stack, 1, originalStack);
            return item;
        }
        catch (Exception ex)
        {
            int after = CountCompatibleChestStack(chest, item);
            int transferred = Math.Clamp(after - before, 0, originalStack);
            this.Monitor.Log(
                $"Chest transfer failed during {context}; {transferred} committed item(s) were reconciled. {ex}",
                LogLevel.Error);

            if (ContainsItemReference(chest.Items, item))
                return null;

            int remaining = originalStack - transferred;
            if (remaining <= 0)
                return null;
            item.Stack = remaining;
            return item;
        }
    }

    private static int CountCompatibleFarmerStack(Farmer owner, Item template)
    {
        long total = 0;
        try
        {
            foreach (Item? candidate in owner.Items)
            {
                try
                {
                    if (candidate is not null
                        && (ReferenceEquals(candidate, template)
                            || candidate.canStackWith(template)))
                    {
                        total += Math.Max(0, candidate.Stack);
                    }
                }
                catch
                {
                    // A malformed custom stack is ignored symmetrically.
                }
            }
        }
        catch
        {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, total);
    }

    private static bool ContainsItemReference(
        IEnumerable<Item?> items,
        Item expected)
    {
        try
        {
            return items.Any(item => ReferenceEquals(item, expected));
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetNearbyAssignedChest(
        SquadMemberState member,
        Farmer owner,
        out ResolvedDepositChest resolved)
    {
        resolved = null!;
        if (!this.TryResolveAssignedChest(member, out ResolvedDepositChest candidate)
            || owner.currentLocation != candidate.Location
            || Vector2.Distance(NormalizeTile(owner.Tile), candidate.Tile) > InventoryChestInteractionDistance
            || !IsNormalPlayerChest(candidate.Chest))
        {
            return false;
        }

        resolved = candidate;
        return true;
    }

    private bool TryGetExpectedNearbyChest(
        SquadMemberState member,
        Farmer owner,
        string? expectedChestId,
        string? expectedLocationName,
        int expectedTileX,
        int expectedTileY,
        out ResolvedDepositChest resolved)
    {
        resolved = null!;
        if (string.IsNullOrWhiteSpace(expectedChestId)
            || string.IsNullOrWhiteSpace(expectedLocationName)
            || !CompanionChestRoutingPolicy.TryNormalizeChestId(
                expectedChestId,
                out string normalizedExpectedId)
            || !this.TryGetNearbyAssignedChest(
                member,
                owner,
                out ResolvedDepositChest candidate)
            || !string.Equals(
                candidate.ChestId,
                normalizedExpectedId,
                StringComparison.Ordinal)
            || !string.Equals(
                candidate.Location.NameOrUniqueName,
                expectedLocationName,
                StringComparison.Ordinal)
            || (int)candidate.Tile.X != expectedTileX
            || (int)candidate.Tile.Y != expectedTileY)
        {
            return false;
        }

        resolved = candidate;
        return true;
    }

    private bool ShouldDepositCompanionItem(SquadMemberState member, Item item)
    {
        CompanionInventoryRulesState rules = this.GetCompanionInventoryRules(member);
        bool isWood = IsWoodCargo(item);
        bool isMineral = IsMineralCargo(item);
        bool isFood = IsFoodCargo(item);
        return CompanionInventoryFilterPolicy.ShouldDeposit(rules, isWood, isMineral, isFood);
    }

    private SmartDepositMode GetEffectiveSmartDepositMode()
    {
        return this.replicatedHostRules?.SmartDeposit ?? this.config.SmartDeposit;
    }

    /// <summary>Runs before new work so a full cargo can be cleared first.</summary>
    private bool TrySmartDepositBeforeWork(SquadMemberState member)
    {
        if (this.GetEffectiveSmartDepositMode() == SmartDepositMode.Disabled
            || member.Inventory.Count < this.GetCompanionInventoryCapacity()
            || !this.HasCompanionDepositCargo(member))
        {
            return false;
        }

        bool complete = this.TryDepositCompanionInventoryToAssignedChest(member, showFeedback: false);
        if (complete || member.Inventory.Count < this.GetCompanionInventoryCapacity())
            return false;
        if (this.IsCompanionChestDepositPending(member))
            return true;

        this.SetCompanionActivity(member, "companion.status.deposit_blocked");
        this.SetTaskFailure(member, "companion.task_failure.smart_deposit_unavailable");
        return true;
    }

    private void TrySmartDepositAfterTask(SquadMemberState member)
    {
        SmartDepositMode mode = this.GetEffectiveSmartDepositMode();
        bool shouldDeposit = mode == SmartDepositMode.AfterEveryTask
            || mode == SmartDepositMode.WhenFull
                && member.Inventory.Count >= this.GetCompanionInventoryCapacity();
        if (shouldDeposit && this.HasCompanionDepositCargo(member))
            this.TryDepositCompanionInventoryToAssignedChest(member, showFeedback: false);
    }

    private static bool IsWoodCargo(Item item)
    {
        return item.QualifiedItemId is "(O)388" or "(O)709" or "(O)169"
            || item.HasContextTag("wood_item")
            || item.HasContextTag("category_wood");
    }

    private static bool IsMineralCargo(Item item)
    {
        return item.QualifiedItemId is
                "(O)378" or "(O)380" or "(O)382" or "(O)384" or "(O)386"
                or "(O)390" or "(O)330" or "(O)334" or "(O)335" or "(O)336"
                or "(O)337" or "(O)909" or "(O)910"
            || item.HasContextTag("ore_item")
            || item.HasContextTag("mineral_item")
            || item.HasContextTag("gem_item")
            || item.HasContextTag("category_minerals")
            || item.HasContextTag("category_gem")
            || item.HasContextTag("category_metal_resources");
    }

    private static bool IsFoodCargo(Item item)
    {
        return item is SObject obj && obj.Edibility != SObject.inedible
            || item.HasContextTag("food_item")
            || item.HasContextTag("edible");
    }
}
