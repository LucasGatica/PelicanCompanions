using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int MissingHatRetryTicks = 600;
    private const int MaxNpcHatFrameOffsetCacheEntries = 512;
    private const int MaxCosmeticItemIdLength = 512;
    private const int MaxCosmeticModDataEntries = 128;
    private const int MaxCosmeticModDataCharacters = 65536;
    private readonly Dictionary<NpcHatFrameOffsetKey, int> npcHatFrameOffsets = new();

    private bool HasCompanionEquippedHat(SquadMemberState member)
    {
        return this.npcCosmetics.TryGetValue(member.NpcName, out NpcCosmeticState? cosmetic)
            && cosmetic.EquippedHat is not null;
    }

    private Item? GetCompanionEquippedHat(SquadMemberState member)
    {
        return this.GetNpcEquippedHat(member.NpcName);
    }

    private Hat? GetNpcEquippedHat(string npcName)
    {
        if (!this.npcCosmetics.TryGetValue(npcName, out NpcCosmeticState? cosmetic)
            || cosmetic.EquippedHat is null)
        {
            return null;
        }

        if (this.npcHatCache.TryGetValue(npcName, out NpcHatCacheEntry cached)
            && Game1.ticks - cached.CheckedAtTick < MissingHatRetryTicks)
        {
            return cached.Hat;
        }

        Hat? hat = this.TryCreateItem(cosmetic.EquippedHat) as Hat;
        this.npcHatCache[npcName] = new NpcHatCacheEntry(hat, Game1.ticks);
        return hat;
    }

    private bool ChangeCompanionHat(SquadMemberState member)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId))
            return false;

        NPC? npc = this.GetNpcByName(member.NpcName);
        if (!CanUseNpcHatSlot(npc))
        {
            this.Warn("companion.hat.unsupported", new { npc = member.DisplayName });
            return false;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        int itemIndex = owner?.CurrentToolIndex ?? -1;
        Item? selectedItem = owner is not null && itemIndex >= 0 && itemIndex < owner.Items.Count
            ? owner.Items[itemIndex]
            : null;
        string expectedStateToken = this.GetNpcHatStateToken(member.NpcName);

        if (selectedItem is Hat)
        {
            SavedItemStack? saved = this.ToSavedItem(selectedItem);
            if (saved is null)
                return false;

            string token = SavedItemStackIdentity.CreateToken(saved);
            if (!Context.IsMainPlayer)
            {
                this.SendActionRequest(
                    "SetCompanionHat",
                    member.NpcName,
                    saved.QualifiedItemId,
                    index: itemIndex,
                    expectedItemToken: token,
                    expectedStateToken: expectedStateToken);
                return true;
            }

            return this.SetCompanionHat(
                member,
                itemIndex,
                saved.QualifiedItemId,
                ownerId,
                token,
                expectedStateToken);
        }

        if (selectedItem is not null || !this.HasCompanionEquippedHat(member))
        {
            this.Warn("companion.hat.select");
            return false;
        }

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "RemoveCompanionHat",
                member.NpcName,
                expectedStateToken: expectedStateToken);
            return true;
        }

        return this.RemoveCompanionHat(member, ownerId, expectedStateToken);
    }

    private bool SetCompanionHat(
        SquadMemberState member,
        int itemIndex,
        string expectedItemId,
        long ownerId,
        string? expectedItemToken,
        string? expectedStateToken)
    {
        if (!this.CanOwnerMutate(member, ownerId, showWarning: false))
            return false;

        if (!this.MatchesNpcHatState(member.NpcName, expectedStateToken))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        NPC? npc = this.GetNpcByName(member.NpcName);
        if (!CanUseNpcHatSlot(npc))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("companion.hat.unsupported", new { npc = member.DisplayName });
            return false;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null || itemIndex < 0 || itemIndex >= owner.Items.Count)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        Item? selectedItem = owner.Items[itemIndex];
        SavedItemStack? selectedSaved = selectedItem is Hat && selectedItem.Stack == 1
            ? this.ToSavedItem(selectedItem)
            : null;
        if (selectedSaved is null
            || !string.Equals(selectedSaved.QualifiedItemId, expectedItemId, StringComparison.Ordinal)
            || !SavedItemStackIdentity.Matches(selectedSaved, expectedItemToken))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        Hat? previousHat = null;
        if (this.HasCompanionEquippedHat(member))
        {
            previousHat = this.GetNpcEquippedHat(member.NpcName);
            if (previousHat is null)
            {
                if (this.ShouldShowFeedbackFor(ownerId))
                    this.Warn("companion.hat.unavailable");
                return false;
            }
        }

        Exception? exchangeError = null;
        try
        {
            // Hats have a maximum stack of one, so replacing the selected slot
            // with the old hat makes the exchange atomic even in a full bag.
            owner.Items[itemIndex] = previousHat;
        }
        catch (Exception ex)
        {
            exchangeError = ex;
            Item? currentSlotItem = itemIndex < owner.Items.Count ? owner.Items[itemIndex] : null;
            bool exchangeCommitted = ReferenceEquals(currentSlotItem, previousHat);
            if (!exchangeCommitted)
            {
                // The setter normally either commits or leaves the old value in
                // place. If an external handler left a third value behind, only
                // restore our original item when doing so can't overwrite it.
                if (currentSlotItem is null)
                {
                    try
                    {
                        owner.Items[itemIndex] = selectedItem;
                    }
                    catch (Exception rollbackEx)
                    {
                        this.Monitor.Log(
                            $"Could not restore the selected hat after a failed exchange for '{member.NpcName}': {rollbackEx}",
                            LogLevel.Error);
                    }
                }

                if (!owner.Items.Any(item => ReferenceEquals(item, selectedItem)))
                    this.PreserveHatAfterFailedExchange(owner, selectedItem!, selectedSaved, member.NpcName);

                this.Monitor.Log($"Could not exchange the selected hat for '{member.NpcName}': {ex}", LogLevel.Error);
                if (this.ShouldShowFeedbackFor(ownerId))
                    this.Warn("multiplayer.command_failed");
                return false;
            }
        }

        this.npcCosmetics[member.NpcName] = new NpcCosmeticState
        {
            NpcName = member.NpcName,
            EquippedHat = CompanionStateCopy.CloneItem(selectedSaved)
        };
        this.npcHatCache.Remove(member.NpcName);
        this.MarkStateDirty();
        if (exchangeError is not null)
        {
            this.Monitor.Log(
                $"The inventory setter reported an error after the hat exchange for '{member.NpcName}' had committed: {exchangeError}",
                LogLevel.Warn);
        }
        if (this.ShouldShowFeedbackFor(ownerId))
        {
            this.Info("companion.hat.equipped", new
            {
                npc = member.DisplayName,
                hat = selectedItem!.DisplayName
            });
        }
        return true;
    }

    private void PreserveHatAfterFailedExchange(
        Farmer owner,
        Item selectedItem,
        SavedItemStack selectedSaved,
        string npcName)
    {
        try
        {
            Item? remainder = owner.addItemToInventory(selectedItem);
            if (remainder is null || owner.Items.Any(item => ReferenceEquals(item, selectedItem)))
                return;
        }
        catch (Exception ex)
        {
            if (owner.Items.Any(item => ReferenceEquals(item, selectedItem)))
                return;

            this.Monitor.Log(
                $"Could not immediately return the selected hat after the failed exchange for '{npcName}': {ex}",
                LogLevel.Error);
        }

        // This branch requires a third-party inventory hook to both replace the
        // slot and fail. Persist the raw stack rather than risk deleting it.
        this.legacyOverflowItems.Add(CompanionStateCopy.CloneItem(selectedSaved));
        this.MarkStateDirty();
        this.Monitor.Log(
            $"Preserved the selected hat from a failed exchange for '{npcName}' in persistent overflow.",
            LogLevel.Error);
    }

    private bool RemoveCompanionHat(
        SquadMemberState member,
        long ownerId,
        string? expectedStateToken)
    {
        if (!this.CanOwnerMutate(member, ownerId, showWarning: false)
            || !this.HasCompanionEquippedHat(member))
        {
            return false;
        }

        if (!this.MatchesNpcHatState(member.NpcName, expectedStateToken))
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_stale");
            return false;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        Hat? hat = this.GetNpcEquippedHat(member.NpcName);
        if (owner is null || hat is null)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("companion.hat.unavailable");
            return false;
        }

        int inventoryBefore = CountInventoryStack(owner, hat.QualifiedItemId);
        Item? notAdded;
        try
        {
            notAdded = owner.addItemToInventory(hat);
        }
        catch (Exception ex)
        {
            int transferred = Math.Clamp(
                CountInventoryStack(owner, hat.QualifiedItemId) - inventoryBefore,
                0,
                1);
            if (transferred > 0)
            {
                this.RemoveNpcHatState(member.NpcName);
                this.MarkStateDirty();
            }

            this.Monitor.Log($"Could not return the equipped hat from '{member.NpcName}': {ex}", LogLevel.Error);
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn(transferred > 0 ? "multiplayer.command_failed" : "companion.hat.inventory_full");
            return transferred > 0;
        }

        if (notAdded is not null)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("companion.hat.inventory_full");
            return false;
        }

        string displayName = hat.DisplayName;
        this.RemoveNpcHatState(member.NpcName);
        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
        {
            this.Info("companion.hat.removed", new
            {
                npc = member.DisplayName,
                hat = displayName
            });
        }
        return true;
    }

    private void RemoveNpcHatState(string npcName)
    {
        this.npcCosmetics.Remove(npcName);
        this.npcHatCache.Remove(npcName);
    }

    private string GetNpcHatStateToken(string npcName)
    {
        return this.npcCosmetics.TryGetValue(npcName, out NpcCosmeticState? cosmetic)
            && cosmetic.EquippedHat is not null
                ? SavedItemStackIdentity.CreateToken(cosmetic.EquippedHat)
                : "";
    }

    private bool MatchesNpcHatState(string npcName, string? expectedStateToken)
    {
        return string.Equals(
            this.GetNpcHatStateToken(npcName),
            expectedStateToken ?? "",
            StringComparison.Ordinal);
    }

    private static bool CanUseNpcHatSlot(NPC? npc)
    {
        // Pets already have a frame- and breed-aware vanilla hat system. The
        // generic villager overlay would be visibly incorrect for them.
        return npc is not null && npc is not Pet;
    }

    private NpcCosmeticState ValidateAndCloneNpcCosmetic(NpcCosmeticState? source, string context)
    {
        SavedItemStack? hat = source?.EquippedHat;
        if (source is null
            || string.IsNullOrWhiteSpace(source.NpcName)
            || source.NpcName.Length > 128
            || hat is null
            || string.IsNullOrWhiteSpace(hat.QualifiedItemId)
            || hat.QualifiedItemId.Length > MaxCosmeticItemIdLength
            || !hat.QualifiedItemId.StartsWith("(H)", StringComparison.Ordinal)
            || hat.Stack != 1
            || hat.Quality is < 0 or > 4
            || (hat.PreservedParentItemId?.Length ?? 0) > MaxCosmeticItemIdLength
            || !HasBoundedCosmeticModData(hat.ModData))
        {
            throw new InvalidDataException($"{context} contains an invalid NPC hat entry.");
        }

        return CompanionStateCopy.CloneCosmetic(source);
    }

    private static bool HasBoundedCosmeticModData(Dictionary<string, string>? modData)
    {
        if (modData is null)
            return true;
        if (modData.Count > MaxCosmeticModDataEntries)
            return false;

        int totalCharacters = 0;
        foreach ((string key, string value) in modData)
        {
            if (key is null || value is null)
                return false;

            totalCharacters += key.Length + value.Length;
            if (totalCharacters > MaxCosmeticModDataCharacters)
                return false;
        }

        return true;
    }

    private void DrawNpcCosmeticHat(NPC npc, SpriteBatch spriteBatch, float alpha)
    {
        if (!Context.IsWorldReady
            || npc is Pet
            || npc.IsInvisible
            || npc.currentLocation != Game1.currentLocation)
        {
            return;
        }

        Hat? hat = this.GetNpcEquippedHat(npc.Name);
        if (hat is null)
            return;

        try
        {
            float scale = Math.Max(0.2f, npc.Scale);
            Vector2 local = npc.getLocalPosition(Game1.viewport);
            float centerX = local.X + npc.GetSpriteWidthForPositioning() * 2f;
            float bodyTop = local.Y
                + npc.GetBoundingBox().Height / 2f
                - npc.Sprite.SpriteHeight * 3f * scale;
            Vector2 position = new(
                centerX - 10f - 28f * scale,
                bodyTop + 20f * scale - 10f);
            position.Y += NpcHatRenderPolicy.ToWorldPixels(
                this.GetNpcHatFrameOffset(npc, measureIfMissing: false),
                scale,
                Game1.pixelZoom);
            float layerDepth = npc.drawOnTop
                ? 0.993f
                : Math.Clamp(npc.StandingPixel.Y / 10000f + 0.000001f, 0f, 0.999f);

            hat.draw(
                spriteBatch,
                position,
                4f * scale / 3f,
                alpha,
                layerDepth,
                npc.FacingDirection,
                useAnimalTexture: false);
        }
        catch (Exception ex)
        {
            // A temporarily broken custom asset should not throw every frame.
            // Retry after the same bounded delay used for unresolved hats.
            this.npcHatCache[npc.Name] = new NpcHatCacheEntry(null, Game1.ticks);
            this.Monitor.Log($"Could not draw the cosmetic hat for '{npc.Name}'; it will be retried. {ex.Message}", LogLevel.Warn);
        }
    }

    private void PrepareVisibleNpcHatFrameOffsets()
    {
        GameLocation? location = Game1.currentLocation;
        if (location is null)
            return;

        foreach (NPC npc in location.characters)
        {
            if (this.npcCosmetics.ContainsKey(npc.Name))
                this.GetNpcHatFrameOffset(npc, measureIfMissing: true);
        }

        if (Game1.CurrentEvent?.actors is not { } eventActors)
            return;

        foreach (NPC npc in eventActors)
        {
            if (this.npcCosmetics.ContainsKey(npc.Name))
                this.GetNpcHatFrameOffset(npc, measureIfMissing: true);
        }
    }

    private int GetNpcHatFrameOffset(NPC npc, bool measureIfMissing)
    {
        AnimatedSprite sprite = npc.Sprite;
        if (!NpcHatRenderPolicy.IsVanillaWalkingStepFrame(
                sprite.CurrentFrame,
                sprite.framesPerAnimation,
                sprite.SpriteWidth,
                sprite.SpriteHeight))
        {
            return 0;
        }

        Rectangle currentSource = sprite.SourceRect;
        Rectangle baselineSource = new(
            currentSource.X - sprite.SpriteWidth,
            currentSource.Y,
            currentSource.Width,
            currentSource.Height);
        Texture2D texture = sprite.Texture;
        if (!texture.Bounds.Contains(currentSource)
            || !texture.Bounds.Contains(baselineSource))
        {
            return 0;
        }

        NpcHatFrameOffsetKey key = new(
            texture,
            currentSource.X,
            currentSource.Y,
            currentSource.Width,
            currentSource.Height);
        if (this.npcHatFrameOffsets.TryGetValue(key, out int cached))
            return cached;
        if (!measureIfMissing)
            return 0;

        int measured = 0;
        try
        {
            Rectangle framePairSource = new(
                baselineSource.X,
                baselineSource.Y,
                baselineSource.Width + currentSource.Width,
                baselineSource.Height);
            int pixelCount = framePairSource.Width * framePairSource.Height;
            Color[] framePairPixels = new Color[pixelCount];
            texture.GetData(0, framePairSource, framePairPixels, 0, pixelCount);
            measured = NpcHatRenderPolicy.GetHeadTopDelta(
                FindStableOpaqueTopRow(
                    framePairPixels,
                    framePairSource.Width,
                    startX: 0,
                    regionWidth: baselineSource.Width),
                FindStableOpaqueTopRow(
                    framePairPixels,
                    framePairSource.Width,
                    startX: baselineSource.Width,
                    regionWidth: currentSource.Width));
        }
        catch (Exception ex)
        {
            // Reading one tiny frame pair is done once per texture/frame.
            // Fail closed so an unusual GPU texture can't destabilize drawing.
            this.Monitor.Log(
                $"Could not measure walking hat offset for '{npc.Name}'; using the fixed anchor. {ex.Message}",
                LogLevel.Debug);
        }

        if (this.npcHatFrameOffsets.Count >= MaxNpcHatFrameOffsetCacheEntries)
            this.npcHatFrameOffsets.Clear();
        this.npcHatFrameOffsets[key] = measured;
        return measured;
    }

    private static int FindStableOpaqueTopRow(
        IReadOnlyList<Color> pixels,
        int rowWidth,
        int startX,
        int regionWidth)
    {
        if (rowWidth <= 0 || startX < 0 || regionWidth <= 0 || startX + regionWidth > rowWidth)
            return -1;

        int rows = pixels.Count / rowWidth;
        int[] opaquePixelsByRow = new int[rows];
        for (int y = 0; y < rows; y++)
        {
            int rowStart = y * rowWidth + startX;
            for (int x = 0; x < regionWidth; x++)
            {
                if (pixels[rowStart + x].A > 0)
                    opaquePixelsByRow[y]++;
            }
        }

        // Extremely narrow custom sprites may never reach the stability
        // threshold; their first visible pixel is still the safest anchor.
        return NpcHatRenderPolicy.FindStableOpaqueTopRow(
            opaquePixelsByRow,
            Math.Min(regionWidth, 3));
    }

    private readonly record struct NpcHatFrameOffsetKey(
        Texture2D Texture,
        int SourceX,
        int SourceY,
        int SourceWidth,
        int SourceHeight);
}
