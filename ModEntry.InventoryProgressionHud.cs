using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Integrations.GenericModConfigMenu;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private void OpenSquadInventoryMenu()
    {
        bool useSquadInventory = this.replicatedHostRules?.UseSquadInventory ?? this.config.UseSquadInventory;
        if (!useSquadInventory && this.squadInventory.Count == 0)
        {
            this.Warn("squad.inventory_disabled_empty");
            return;
        }

        string summary = this.squadInventory.Count == 0
            ? this.Tr("squad.inventory_empty")
            : string.Join(Environment.NewLine, this.squadInventory.Select(p => this.Tr("squad.inventory_line", new { item = p.DisplayName, count = p.Stack })));

        List<Response> responses = new()
        {
            new Response("Withdraw", this.Tr("squad.withdraw_all")),
            new Response("Cancel", this.Tr("generic.cancel"))
        };

        Game1.currentLocation.createQuestionDialogue(
            this.Tr("squad.inventory_title") + Environment.NewLine + summary,
            responses.ToArray(),
            (_, answer) =>
            {
                if (answer == "Withdraw")
                    this.WithdrawSquadInventory();
            });
    }

    private void WithdrawSquadInventory()
    {
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("WithdrawSquadInventory");
            return;
        }

        this.WithdrawSquadInventory(Game1.player.UniqueMultiplayerID);
    }

    private void WithdrawSquadInventory(long ownerId)
    {
        if (this.squadInventory.Count == 0)
        {
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Info("squad.inventory_empty");
            return;
        }

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null)
            return;

        int index = 0;
        while (index < this.squadInventory.Count)
        {
            Item source = this.squadInventory[index];
            int originalStack = Math.Max(0, source.Stack);
            Item copy;
            try
            {
                copy = source.getOne();
                copy.Stack = originalStack;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't clone squad inventory item '{source.QualifiedItemId}' for withdrawal: {ex.Message}", LogLevel.Warn);
                index++;
                continue;
            }

            int inventoryBefore = CountInventoryStack(owner, source.QualifiedItemId);
            Item? notAdded;
            try
            {
                notAdded = owner.addItemToInventory(copy);
            }
            catch (Exception ex)
            {
                int transferred = Math.Clamp(CountInventoryStack(owner, source.QualifiedItemId) - inventoryBefore, 0, originalStack);
                if (transferred > 0)
                {
                    int remainder = originalStack - transferred;
                    if (remainder <= 0)
                        this.squadInventory.RemoveAt(index);
                    else
                    {
                        source.Stack = remainder;
                        index++;
                    }

                    this.MarkStateDirty();
                }
                else
                {
                    index++;
                }

                this.Monitor.Log($"Squad inventory withdrawal failed for '{source.QualifiedItemId}' and was isolated: {ex}", LogLevel.Error);
                continue;
            }

            int remainingStack = Math.Clamp(notAdded?.Stack ?? 0, 0, originalStack);
            int moved = originalStack - remainingStack;
            if (moved <= 0)
            {
                index++;
                continue;
            }

            if (remainingStack == 0)
                this.squadInventory.RemoveAt(index);
            else
            {
                source.Stack = remainingStack;
                index++;
            }

            this.MarkStateDirty();
        }

        // A failed world drop or an earlier capacity reduction may have left
        // persistent overflow waiting for a shared slot. Refill newly freed
        // slots before reporting the final state.
        this.ReloadOverflowInventoryIntoSquad();

        if (this.ShouldShowFeedbackFor(ownerId))
        {
            if (this.squadInventory.Count == 0)
                this.Info("squad.withdraw_complete");
            else
                this.Warn("squad.withdraw_partial");
        }
    }

    private static int CountInventoryStack(Farmer owner, string qualifiedItemId)
    {
        return CountItemStack(owner.Items, qualifiedItemId);
    }

    private static int CountItemStack(IEnumerable<Item?> items, string qualifiedItemId)
    {
        long total = 0;
        foreach (Item? item in items)
        {
            try
            {
                if (item is not null && string.Equals(item.QualifiedItemId, qualifiedItemId, StringComparison.Ordinal))
                    total += Math.Max(0, item.Stack);
            }
            catch
            {
                // A malformed custom item shouldn't make error recovery throw a
                // second exception and hide the original transfer failure.
            }
        }

        return (int)Math.Min(int.MaxValue, total);
    }

    private Item? AddToSquadInventory(Item item)
    {
        Item copy = CloneItemStack(item);
        foreach (Item existing in this.squadInventory)
        {
            if (existing.canStackWith(copy))
            {
                int remainder = existing.addToStack(copy);
                this.MarkStateDirty();
                if (remainder <= 0)
                    return null;

                copy.Stack = remainder;
            }
        }

        if (this.squadInventory.Count >= SquadInventoryCapacity)
            return copy;

        this.squadInventory.Add(copy);
        this.MarkStateDirty();
        return null;
    }

    private Item? AddToCompanionInventory(SquadMemberState member, Item item)
    {
        int originalStack = Math.Max(1, item.Stack);
        int inventoryBefore = CountSavedItemStack(member.Inventory, item.QualifiedItemId);
        Item copy;
        try
        {
            copy = CloneItemStack(item);
            for (int i = 0; i < member.Inventory.Count; i++)
            {
                Item? existing = this.TryCreateItem(member.Inventory[i]);
                if (existing is null)
                    continue;

                if (!existing.canStackWith(copy))
                    continue;

                int remainder = existing.addToStack(copy);
                SavedItemStack? savedExisting = this.ToSavedItem(existing)
                    ?? throw new InvalidOperationException($"Couldn't serialize merged inventory stack '{existing.QualifiedItemId}'.");
                member.Inventory[i] = savedExisting;
                this.MarkStateDirty();

                if (remainder <= 0)
                    return null;

                copy.Stack = remainder;
            }

            if (member.Inventory.Count >= this.config.CompanionInventorySlots)
                return copy;

            SavedItemStack? saved = this.ToSavedItem(copy);
            if (saved is null)
                return copy;

            member.Inventory.Add(saved);
            this.MarkStateDirty();
            return null;
        }
        catch (Exception ex)
        {
            int transferred = Math.Clamp(
                CountSavedItemStack(member.Inventory, item.QualifiedItemId) - inventoryBefore,
                0,
                originalStack);
            this.Monitor.Log($"Companion reward transfer failed for '{item.QualifiedItemId}' and was reconciled: {ex}", LogLevel.Error);
            return this.CreateRemainderStack(item, originalStack - transferred);
        }
    }

    private static int CountSavedItemStack(IEnumerable<SavedItemStack> items, string qualifiedItemId)
    {
        long total = items
            .Where(saved => string.Equals(saved.QualifiedItemId, qualifiedItemId, StringComparison.Ordinal))
            .Sum(saved => (long)Math.Max(0, saved.Stack));
        return (int)Math.Min(int.MaxValue, total);
    }

    private Item? AddToFarmerInventorySafely(Farmer owner, Item item, string context)
    {
        int originalStack = Math.Max(1, item.Stack);
        Item candidate;
        try
        {
            candidate = CloneItemStack(item);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't clone '{item.QualifiedItemId}' for {context}: {ex.Message}", LogLevel.Warn);
            return this.CreateRemainderStack(item, originalStack);
        }

        int inventoryBefore = CountInventoryStack(owner, item.QualifiedItemId);
        try
        {
            return owner.addItemToInventory(candidate);
        }
        catch (Exception ex)
        {
            int transferred = Math.Clamp(
                CountInventoryStack(owner, item.QualifiedItemId) - inventoryBefore,
                0,
                originalStack);
            this.Monitor.Log($"Farmer inventory transfer failed for '{item.QualifiedItemId}' during {context} and was reconciled: {ex}", LogLevel.Error);
            return this.CreateRemainderStack(item, originalStack - transferred);
        }
    }

    private Item? AddToSquadInventorySafely(Item item, string context)
    {
        int originalStack = Math.Max(1, item.Stack);
        int inventoryBefore = CountItemStack(this.squadInventory, item.QualifiedItemId);
        try
        {
            return this.AddToSquadInventory(item);
        }
        catch (Exception ex)
        {
            int transferred = Math.Clamp(
                CountItemStack(this.squadInventory, item.QualifiedItemId) - inventoryBefore,
                0,
                originalStack);
            this.Monitor.Log($"Squad inventory transfer failed for '{item.QualifiedItemId}' during {context} and was reconciled: {ex}", LogLevel.Error);
            return this.CreateRemainderStack(item, originalStack - transferred);
        }
    }

    private Item? CreateRemainderStack(Item template, int stack)
    {
        if (stack <= 0)
            return null;

        try
        {
            Item remainder = CloneItemStack(template);
            remainder.Stack = stack;
            return remainder;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't clone remainder for '{template.QualifiedItemId}'; reusing the transient reward instance. {ex.Message}", LogLevel.Warn);
            template.Stack = stack;
            return template;
        }
    }

    private void DropItemSafely(Item item, GameLocation location, Vector2 tile, int direction, string context)
    {
        int debrisCountBefore;
        try
        {
            debrisCountBefore = location.debris.Count;
        }
        catch
        {
            debrisCountBefore = -1;
        }

        try
        {
            Game1.createItemDebris(item, tile * 64f, direction, location);
        }
        catch (Exception ex)
        {
            bool debrisWasAdded = false;
            try
            {
                debrisWasAdded = debrisCountBefore >= 0 && location.debris.Count > debrisCountBefore;
            }
            catch
            {
                // Fall through to persistent overflow when the location can't
                // prove that the debris commit completed.
            }

            if (debrisWasAdded)
            {
                this.Monitor.Log($"World-drop for '{item.QualifiedItemId}' reported an error after debris was added during {context}; no duplicate fallback was created. {ex.Message}", LogLevel.Warn);
                return;
            }

            SavedItemStack? saved = null;
            try
            {
                saved = this.ToSavedItem(item);
            }
            catch (Exception serializationError)
            {
                this.Monitor.Log($"Full serialization failed for dropped remainder '{item.QualifiedItemId}'; attempting an identity/stack fallback. {serializationError.Message}", LogLevel.Warn);
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.QualifiedItemId) && item.Stack > 0)
                    {
                        saved = new SavedItemStack
                        {
                            QualifiedItemId = item.QualifiedItemId,
                            Stack = item.Stack,
                            Quality = item.Quality
                        };
                    }
                }
                catch
                {
                    // The final log below records the unrecoverable custom item.
                }
            }

            if (saved is not null)
            {
                this.legacyOverflowItems.Add(saved);
                this.MarkStateDirty();
                this.Monitor.Log($"Couldn't drop '{saved.QualifiedItemId}' after {context}; its remainder was preserved in persistent overflow instead. {ex.Message}", LogLevel.Error);
                return;
            }

            this.Monitor.Log($"Couldn't drop or serialize custom remainder '{item.GetType().FullName}' after {context}. {ex}", LogLevel.Error);
        }
    }

    private void RouteTaskRewardOrDrop(SquadMemberState member, Item item, GameLocation location, Vector2 tile, string sourceKey)
    {
        this.RecordCompanionLoot(member, item, sourceKey);

        Item? notAdded = this.AddToCompanionInventory(member, item);
        if (notAdded is null)
            return;

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is not null)
            notAdded = this.AddToFarmerInventorySafely(owner, notAdded, sourceKey);
        if (notAdded is null)
            return;

        this.DropItemSafely(notAdded, location, tile, owner?.FacingDirection ?? 2, sourceKey);
        this.SetTaskFailure(member, "companion.task_failure.inventory_full_world_drop");
        this.Warn("companion.inventory.full", new { npc = member.DisplayName });
    }

    private static Item CloneItemStack(Item item)
    {
        Item copy = item.getOne();
        copy.Stack = Math.Max(1, item.Stack);
        return copy;
    }

    private void AddCompanionXp(SquadMemberState member, int amount)
    {
        if (!this.config.EnableCompanionProgression || amount <= 0)
            return;

        int oldLevel = member.Level <= 0 ? 1 : member.Level;
        member.Xp = Math.Max(0, member.Xp + amount);
        this.MarkStateDirty();
        int newLevel = CompanionProgression.GetLevelForXp(member.Xp);
        if (newLevel <= oldLevel)
        {
            member.Level = oldLevel;
            return;
        }

        member.Level = newLevel;
        member.UnspentSkillPoints += newLevel - oldLevel;
        if (oldLevel < CompanionProgression.MaxLevel
            && newLevel >= CompanionProgression.MaxLevel
            && !member.BonusLevelTenPointGranted)
        {
            member.UnspentSkillPoints++;
            member.BonusLevelTenPointGranted = true;
        }

        if (this.config.ShowCompanionLevelUpHud)
        {
            this.ShowCompanionHudNotice(
                member,
                this.Tr("companion.level_up", new { npc = member.DisplayName, level = newLevel, points = member.UnspentSkillPoints }),
                itemQualifiedId: null,
                new Color(96, 165, 220));
            Game1.playSound("newArtifact");
        }

        NPC? npc = this.GetNpcByName(member.NpcName);
        if (npc is not null)
        {
            this.Say(
                npc,
                "LevelUp",
                force: false,
                ownerIdOverride: member.OwnerId,
                context: new CompanionDialogueContext { Level = newLevel });
        }
    }

    private void RecordCompanionLoot(SquadMemberState member, Item item, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(item.QualifiedItemId))
            return;

        RecentCompanionLoot loot = new()
        {
            QualifiedItemId = item.QualifiedItemId,
            DisplayName = item.DisplayName,
            Stack = Math.Max(1, item.Stack),
            SourceKey = sourceKey,
            AddedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks
        };

        member.RecentLoot.Insert(0, loot);
        if (member.RecentLoot.Count > RecentLootLimit)
            member.RecentLoot.RemoveRange(RecentLootLimit, member.RecentLoot.Count - RecentLootLimit);
        this.MarkStateDirty();

        if (this.IsImportantLoot(item, member.OwnerId))
        {
            this.ShowCompanionHudNotice(
                member,
                this.Tr("companion.loot.important", new { npc = member.DisplayName, item = item.DisplayName, count = item.Stack }),
                item.QualifiedItemId,
                new Color(218, 170, 65));
            Game1.playSound("discoverMineral");

            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
            {
                this.Say(
                    npc,
                    "LootFound",
                    force: false,
                    ownerIdOverride: member.OwnerId,
                    context: new CompanionDialogueContext
                    {
                        ItemName = item.DisplayName,
                        ItemId = item.QualifiedItemId
                    });
            }
        }
    }

    private bool IsImportantLoot(Item item, long ownerId)
    {
        string id = item.QualifiedItemId;
        if (id is "(O)909" or "(O)386" or "(O)384" or "(O)74" or "(O)72" or "(O)60" or "(O)62" or "(O)64" or "(O)66" or "(O)68" or "(O)70")
            return true;

        try
        {
            return item.sellToStorePrice(ownerId) >= 250;
        }
        catch
        {
            return false;
        }
    }

    private void ShowCompanionHudNotice(SquadMemberState member, string text, string? itemQualifiedId, Color accent)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        int now = Game1.ticks;
        this.companionHudNotices.RemoveAll(p => now - p.StartedTick > p.DurationTicks);
        this.companionHudNotices.Add(new CompanionHudNotice(member.NpcName, text, itemQualifiedId, now, CompanionHudNoticeDurationTicks, accent));
        if (this.companionHudNotices.Count > 4)
            this.companionHudNotices.RemoveRange(0, this.companionHudNotices.Count - 4);
    }

    private void ShowMovementDebugNotice(SquadMemberState member, string key, object? tokens = null)
    {
        if (!this.config.ShowCompanionMovementDebug || member.OwnerId != Game1.player.UniqueMultiplayerID)
            return;

        int now = Game1.ticks;
        if (this.lastMovementDebugNoticeTicks.TryGetValue(member.NpcName, out int lastTick)
            && now - lastTick < CompanionHudNoticeDurationTicks)
        {
            return;
        }

        this.lastMovementDebugNoticeTicks[member.NpcName] = now;
        this.ShowCompanionHudNotice(member, this.Tr(key, tokens), itemQualifiedId: null, new Color(90, 165, 220));
    }

    private void DrawCompanionHudNotices(SpriteBatch b)
    {
        int now = Game1.ticks;
        this.companionHudNotices.RemoveAll(p => now - p.StartedTick > p.DurationTicks);
        List<CompanionHudNotice> localNotices = this.companionHudNotices
            .Where(notice => this.members.TryGetValue(notice.NpcName, out SquadMemberState? member)
                && member.OwnerId == Game1.player.UniqueMultiplayerID)
            .TakeLast(3)
            .ToList();
        if (localNotices.Count == 0)
            return;

        int width = Math.Min(360, Math.Max(1, Game1.uiViewport.Width - 40));
        const int height = 72;
        int x = this.config.CompanionQuickHudSide == CompanionQuickHudSide.Right
            ? 20
            : Math.Max(20, Game1.uiViewport.Width - width - (Game1.uiViewport.Width >= 600 ? 96 : 20));
        int y = this.config.CompanionQuickHudSide == CompanionQuickHudSide.Left
            && Game1.uiViewport.Height >= 520
                ? 214
                : 82;
        foreach (CompanionHudNotice notice in localNotices)
        {
            float age = Math.Clamp((now - notice.StartedTick) / (float)Math.Max(1, notice.DurationTicks), 0f, 1f);
            float alpha = age < 0.78f ? 1f : Math.Clamp(1f - (age - 0.78f) / 0.22f, 0f, 1f);
            Rectangle bounds = new(x, y, width, height);
            b.Draw(Game1.staminaRect, bounds, Color.Black * 0.45f * alpha);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6), new Color(248, 238, 216) * alpha);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, 6, bounds.Height), notice.Accent * alpha);

            Rectangle iconBounds = new(bounds.X + 14, bounds.Y + 10, 52, 52);
            b.Draw(Game1.staminaRect, iconBounds, new Color(255, 247, 226) * alpha);
            if (!string.IsNullOrWhiteSpace(notice.ItemQualifiedId))
                this.DrawHudItemIcon(b, notice.ItemQualifiedId, iconBounds, alpha);
            else
                this.DrawHudPortrait(b, this.GetNpcByName(notice.NpcName), iconBounds, alpha);

            Utility.drawTextWithShadow(
                b,
                this.FitHudText(notice.Text, Game1.smallFont, width - 86),
                Game1.smallFont,
                new Vector2(bounds.X + 78, bounds.Y + 23),
                new Color(62, 42, 27) * alpha);
            y += height + 8;
        }
    }

    private void DrawCompanionMovementDebug(SpriteBatch b)
    {
        if (!this.config.ShowCompanionMovementDebug || Game1.activeClickableMenu is not null)
            return;

        List<SquadMemberState> localMembers = this.GetLocalMembers().Take(6).ToList();
        if (localMembers.Count == 0)
            return;

        List<string> lines = new()
        {
            this.Tr("companion.movement_debug.title", new { formation = this.Tr($"config.enum.{this.config.CompanionFormationMode}") })
        };

        foreach (SquadMemberState member in localMembers)
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
            string distance = "--";
            if (npc is not null && owner is not null && npc.currentLocation == owner.currentLocation)
                distance = Vector2.Distance(NormalizeTile(npc.Tile), NormalizeTile(owner.Tile)).ToString("0.0");

            string target = "--";
            if (this.lastFollowTargets.TryGetValue(member.NpcName, out Vector2 followTarget))
                target = $"{(int)followTarget.X},{(int)followTarget.Y}";
            else if (member.CurrentTargetX >= 0 && member.CurrentTargetY >= 0)
                target = $"{member.CurrentTargetX},{member.CurrentTargetY}";

            string recovery = this.followRecoveryUntilTick.ContainsKey(member.NpcName)
                ? this.Tr("companion.movement_debug.recovery")
                : "";
            lines.Add(this.Tr("companion.movement_debug.line", new
            {
                npc = member.DisplayName,
                status = this.GetCompanionStatusText(member),
                distance,
                target,
                recovery
            }));
        }

        int width = Math.Min(
            Math.Clamp((int)lines.Max(p => Game1.smallFont.MeasureString(p).X) + 24, 280, 520),
            Math.Max(1, Game1.uiViewport.Width - 40));
        int height = 18 + lines.Count * 24;
        int x = this.config.CompanionQuickHudSide == CompanionQuickHudSide.Right
            ? 20
            : Math.Max(20, Game1.uiViewport.Width - width - (Game1.uiViewport.Width >= 600 ? 96 : 20));
        int y = Math.Max(20, Game1.uiViewport.Height - height - 28);
        Rectangle bounds = new(x, y, width, height);

        b.Draw(Game1.staminaRect, bounds, Color.Black * 0.58f);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, bounds.Width, 3), new Color(90, 165, 220) * 0.9f);

        int textY = bounds.Y + 10;
        foreach (string line in lines)
        {
            Utility.drawTextWithShadow(
                b,
                this.FitHudText(line, Game1.smallFont, width - 22),
                Game1.smallFont,
                new Vector2(bounds.X + 12, textY),
                Color.White);
            textY += 24;
        }
    }

    private void DrawHudItemIcon(SpriteBatch b, string itemQualifiedId, Rectangle bounds, float alpha)
    {
        try
        {
            Item item = ItemRegistry.Create(itemQualifiedId, allowNull: false);
            item.drawInMenu(b, new Vector2(bounds.X + 4, bounds.Y + 4), 0.8f);
        }
        catch
        {
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 12, bounds.Y + 12, bounds.Width - 24, bounds.Height - 24), Color.Gold * alpha);
        }
    }

    private void DrawHudPortrait(SpriteBatch b, NPC? npc, Rectangle bounds, float alpha)
    {
        if (npc is not null)
        {
            try
            {
                Texture2D portrait = Game1.content.Load<Texture2D>($"Portraits/{npc.Name}");
                b.Draw(portrait, new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8), new Rectangle(0, 0, 64, 64), Color.White * alpha);
                return;
            }
            catch
            {
                // Some companions are pets or custom actors without portraits.
            }
        }

        if (npc?.Sprite?.Texture is not null)
            b.Draw(npc.Sprite.Texture, new Rectangle(bounds.X + 12, bounds.Y + 5, 28, 42), npc.Sprite.SourceRect, Color.White * alpha);
    }

    private string FitHudText(string text, SpriteFont font, int width)
    {
        if (width <= 0)
            return "";

        if (font.MeasureString(text).X <= width)
            return text;

        const string suffix = "...";
        while (text.Length > 0 && font.MeasureString(text + suffix).X > width)
            text = text[..^1];

        return text.Length == 0 ? "" : text + suffix;
    }

    private void ShowCompanionWorkSignal(NPC npc, GameLocation location, Vector2 tile, string kind)
    {
        Color color = kind switch
        {
            "wood" => new Color(111, 143, 76),
            "mining" => new Color(126, 132, 150),
            "forage" => new Color(91, 160, 90),
            "water" => new Color(80, 145, 210),
            "harvest" => new Color(196, 145, 62),
            "pet" => new Color(218, 126, 154),
            "return" => new Color(90, 165, 220),
            _ => new Color(225, 176, 76)
        };

        try
        {
            location.temporarySprites.Add(new TemporaryAnimatedSprite(10, tile * 64f + new Vector2(20f, -18f), color, 8, false, 45f)
            {
                alphaFade = 0.025f,
                motion = new Vector2(0f, -0.35f),
                scale = 0.8f,
                layerDepth = Math.Clamp((tile.Y * 64f + 96f) / 10000f, 0f, 1f)
            });
        }
        catch
        {
            // Visual signal is non-critical; keep the task running if a custom map rejects sprites.
        }

        if (kind is "forage" or "water" or "harvest" or "pet" or "return")
        {
            npc.jumpWithoutSound(2f);
            npc.shake(90);
        }
    }

    private bool HasSkill(SquadMemberState member, string skillId)
    {
        return member.UnlockedSkillIds.Contains(skillId, StringComparer.OrdinalIgnoreCase);
    }
}
