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
    private void ValidateLoadedMembers()
    {
        foreach (SquadMemberState member in this.members.Values.ToList())
        {
            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            if (npc is null)
            {
                this.Monitor.Log($"Saved companion '{member.NpcName}' isn't currently available. Its ownership, progression, and inventory will be preserved for when its content mod returns.", LogLevel.Warn);
                this.SetTaskFailure(member, "companion.task_failure.npc_missing");
                continue;
            }

            if (HasIndependentNpcMovementSystem(npc))
            {
                if (member.Inventory is { Count: > 0 })
                {
                    this.legacyOverflowItems.AddRange(member.Inventory);
                    member.Inventory.Clear();
                }

                this.members.Remove(member.NpcName);
                this.deferredNpcRestores[member.NpcName] = CreateDeferredNpcRestore(member);
                this.Monitor.Log(
                    $"Saved companion '{member.NpcName}' uses an independent vanilla movement system and was safely dismissed; its items were preserved.",
                    LogLevel.Warn);
                continue;
            }

            member.DisplayName = npc.displayName;
            this.EnsureOriginalNpcMovementSpeedCaptured(member, npc);
            if (member.LastFailureReasonKey == "companion.task_failure.npc_missing")
                this.SetTaskFailure(member, "");
        }
    }

    private void RestorePersistedMemberPositions(bool logFailures = true)
    {
        foreach (SquadMemberState member in this.members.Values)
        {
            try
            {
                if (member.Mode is not (CompanionMode.Waiting or CompanionMode.ParkedForDisconnect)
                    || string.IsNullOrWhiteSpace(member.WaitingLocationName))
                {
                    continue;
                }

                NPC? npc = this.GetNpcByName(member.NpcName);
                GameLocation? location = Game1.getLocationFromName(member.WaitingLocationName);
                if (npc is null || location is null)
                {
                    if (logFailures)
                    {
                        this.Monitor.Log(
                            $"Could not restore waiting position for '{member.NpcName}' in '{member.WaitingLocationName}'. The saved state was kept.",
                            LogLevel.Warn);
                    }
                    continue;
                }

                Vector2 waitingTile = NormalizeTile(new Vector2(member.WaitingTileX, member.WaitingTileY));
                if (npc.currentLocation != location || NormalizeTile(npc.Tile) != waitingTile)
                {
                    if (!this.PlaceNpc(npc, location, waitingTile))
                    {
                        if (logFailures)
                            this.Monitor.Log($"Could not find a safe waiting tile for '{member.NpcName}' in '{member.WaitingLocationName}'.", LogLevel.Warn);
                        continue;
                    }

                    Vector2 restoredTile = NormalizeTile(npc.Tile);
                    if (restoredTile != waitingTile)
                    {
                        member.WaitingTileX = restoredTile.X;
                        member.WaitingTileY = restoredTile.Y;
                        this.MarkStateDirty();
                    }
                }

                this.DisableNpcSchedule(npc, stopCurrentRoute: true);
            }
            catch (Exception ex)
            {
                if (logFailures)
                    this.Monitor.Log($"Waiting position restoration failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
            }
        }
    }

    private void ClearFollowState(string npcName)
    {
        this.controlledNpcLeases.RemoveWhere(npc => string.Equals(npc.Name, npcName, StringComparison.OrdinalIgnoreCase));
        this.lastFollowTargets.Remove(npcName);
        this.lastFollowTargetDistances.Remove(npcName);
        this.lastFollowPathTicks.Remove(npcName);
        this.lastFollowProgressPositions.Remove(npcName);
        this.activeRecallTargets.Remove(npcName);
        this.followNoProgressTicks.Remove(npcName);
        this.followRecoveryUntilTick.Remove(npcName);
        this.disconnectedFollowRecovery.Remove(npcName);
        this.lastMovementDebugNoticeTicks.Remove(npcName);
        this.companionMovementControllers.Remove(npcName);
        foreach (string key in this.followDestinationsThisUpdate
            .Where(pair => string.Equals(pair.Value, npcName, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList())
        {
            this.followDestinationsThisUpdate.Remove(key);
        }
    }

    private List<SavedItemStack> NormalizeLoadedMember(SquadMemberState member)
    {
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = member.NpcName;

        if (!Enum.IsDefined(member.Mode))
            member.Mode = CompanionMode.Following;

        if (!Enum.IsDefined(member.PreferredWorkSpecialty))
            member.PreferredWorkSpecialty = CompanionWorkSpecialty.ClearArea;

        if (!member.ClearArea)
        {
            if (member.SearchWood && !member.SearchMining)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Wood;
            else if (member.SearchMining && !member.SearchWood)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Mining;
        }

        member.Level = Math.Clamp(member.Level <= 0 ? CompanionProgression.GetLevelForXp(member.Xp) : member.Level, 1, CompanionProgression.MaxLevel);
        member.Xp = Math.Max(0, member.Xp);
        int actualLevel = CompanionProgression.GetLevelForXp(member.Xp);
        if (actualLevel > member.Level)
        {
            member.UnspentSkillPoints += actualLevel - member.Level;
            member.Level = actualLevel;
        }

        if (member.Level >= CompanionProgression.MaxLevel && !member.BonusLevelTenPointGranted)
        {
            member.UnspentSkillPoints++;
            member.BonusLevelTenPointGranted = true;
        }

        member.UnlockedSkillIds ??= new List<string>();
        member.UnlockedSkillIds = member.UnlockedSkillIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        member.UnspentSkillPoints += CompanionProgression.GetLegacySkillPointRefund(member.UnlockedSkillIds);
        member.UnspentSkillPoints = Math.Max(0, member.UnspentSkillPoints);
        member.UnlockedSkillIds = member.UnlockedSkillIds
            .Where(p => CompanionProgression.Skills.Any(skill => string.Equals(skill.Id, p, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<SavedItemStack> validInventory = new();
        List<SavedItemStack> overflow = new();
        member.Inventory ??= new List<SavedItemStack>();
        foreach (SavedItemStack? saved in member.Inventory)
        {
            if (saved is null || string.IsNullOrWhiteSpace(saved.QualifiedItemId) || saved.Stack <= 0)
                throw new InvalidDataException($"Companion '{member.NpcName}' contains an invalid inventory stack.");

            // Keep unresolved custom items in the persisted overflow instead of
            // leaving invisible holes which would shift panel click indices.
            if (this.TryCreateItem(saved) is null)
                overflow.Add(saved);
            else
                validInventory.Add(saved);
        }

        overflow.AddRange(validInventory.Skip(this.config.CompanionInventorySlots));
        member.Inventory = validInventory.Take(this.config.CompanionInventorySlots).ToList();
        // Work queues and navigation controllers are intentionally transient.
        // Rebuild a truthful idle mode after load instead of persisting a
        // "working" label and target which no longer have a backing task.
        member.CurrentActivityKey = member.Mode switch
        {
            CompanionMode.Waiting => "companion.status.waiting",
            CompanionMode.ParkedForDisconnect => "companion.status.parked",
            _ => "companion.status.following"
        };
        member.LastTaskResultKey ??= "";
        member.LastFailureReasonKey ??= "";
        member.CurrentTargetKey = "";
        member.CurrentTargetX = -1;
        member.CurrentTargetY = -1;
        member.PreviewTargetKey = "";
        member.PreviewTargetX = -1;
        member.PreviewTargetY = -1;
        member.PreviewReasonKey = "";
        member.CurrentWorkIsDirect = false;
        member.RecentLoot = (member.RecentLoot ?? new List<RecentCompanionLoot>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.QualifiedItemId) && p.Stack > 0)
            .OrderByDescending(p => p.AddedAtUtcTicks)
            .Take(RecentLootLimit)
            .ToList();

        return overflow;
    }

    private void ReloadOverflowInventoryIntoSquad()
    {
        if (this.legacyOverflowItems.Count == 0)
            return;

        int index = 0;
        while (index < this.legacyOverflowItems.Count)
        {
            SavedItemStack saved = this.legacyOverflowItems[index];
            Item? item = this.TryCreateItem(saved);
            if (item is null)
            {
                // The providing content mod may be temporarily missing. Keep the
                // raw stack in save data so reinstalling that content can restore it.
                index++;
                continue;
            }

            int originalStack = saved.Stack;
            int squadBefore = CountItemStack(this.squadInventory, saved.QualifiedItemId);
            Item? notAdded;
            try
            {
                notAdded = this.AddToSquadInventory(item);
            }
            catch (Exception ex)
            {
                int transferred = Math.Clamp(CountItemStack(this.squadInventory, saved.QualifiedItemId) - squadBefore, 0, originalStack);
                if (transferred >= originalStack)
                    this.legacyOverflowItems.RemoveAt(index);
                else if (transferred > 0)
                {
                    saved.Stack = originalStack - transferred;
                    index++;
                }
                else
                {
                    index++;
                }

                if (transferred > 0)
                    this.MarkStateDirty();

                this.Monitor.Log($"Overflow inventory reload failed for '{saved.QualifiedItemId}' and was isolated: {ex}", LogLevel.Error);
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
                this.legacyOverflowItems.RemoveAt(index);
            else
            {
                saved.Stack = remainingStack;
                index++;
            }

            this.MarkStateDirty();
        }
    }

    private void RebalanceMemberInventoriesForCapacity()
    {
        int capacity = this.config.CompanionInventorySlots;
        foreach (SquadMemberState member in this.members.Values)
        {
            member.Inventory ??= new List<SavedItemStack>();
            if (member.Inventory.Count <= capacity)
                continue;

            this.legacyOverflowItems.AddRange(member.Inventory.Skip(capacity));
            member.Inventory = member.Inventory.Take(capacity).ToList();
        }

        this.ReloadOverflowInventoryIntoSquad();
    }

    private void LoadNpcProfiles()
    {
        try
        {
            this.npcProfiles = this.Helper.GameContent.Load<Dictionary<string, NpcCompanionProfile>>(NpcConfigAssetKey)
                ?? new Dictionary<string, NpcCompanionProfile>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            this.npcProfiles = new Dictionary<string, NpcCompanionProfile>(StringComparer.OrdinalIgnoreCase);
            this.Monitor.Log($"Failed loading NPC companion config. Generic dialogue fallback will be used. {ex}", LogLevel.Warn);
        }
    }

    private SavedModState BuildSaveData()
    {
        List<SquadMemberState> detachedMembers = this.members.Values
            .Select(member =>
            {
                SquadMemberState copy = CompanionStateCopy.CloneMember(member);
                copy.CurrentWorkIsDirect = this.HasActiveWorkDirective(member)
                    || (this.pendingTasks.TryGetValue(member.NpcName, out PendingCompanionTask? task)
                        && task.Kind != CompanionTaskKind.MovingToWait
                        && !task.UsesConfiguredAutonomy);
                return copy;
            })
            .ToList();

        return new SavedModState
        {
            Version = CurrentSaveVersion,
            Revision = this.stateRevision,
            Members = detachedMembers,
            TaskTogglesByPlayer = this.taskToggles.ToDictionary(p => p.Key.ToString(), p => p.Value),
            SquadInventory = this.squadInventory.Select(this.ToSavedItem).Where(p => p is not null).Cast<SavedItemStack>().ToList(),
            LegacyOverflowItems = this.legacyOverflowItems.Select(CompanionStateCopy.CloneItem).ToList(),
            PendingNpcRestores = this.deferredNpcRestores.Values.Select(CompanionStateCopy.CloneRestore).ToList(),
            HostRules = this.BuildHostRules()
        };
    }

    private CompanionHostRules BuildHostRules()
    {
        return new CompanionHostRules
        {
            UseSquadInventory = this.config.UseSquadInventory,
            EnableCompanionProgression = this.config.EnableCompanionProgression,
            CompanionInventorySlots = this.config.CompanionInventorySlots,
            CompanionWorkRadius = this.config.CompanionWorkRadius,
            CompanionWorkReturnDistance = this.config.CompanionWorkReturnDistance,
            FriendshipRequirement = this.config.FriendshipRequirement,
            MaxSquadSize = this.config.MaxSquadSize,
            RecruitAllNpcs = this.config.RecruitAllNpcs,
            EnableGathering = this.config.EnableGathering,
            ProtectBeehouseFlowers = this.config.ProtectBeehouseFlowers,
            HarvestingMode = this.config.HarvestingMode,
            ForagingMode = this.config.ForagingMode,
            LumberingMode = this.config.LumberingMode,
            MiningMode = this.config.MiningMode,
            WateringMode = this.config.WateringMode,
            PettingMode = this.config.PettingMode
        };
    }

    private SavedItemStack? ToSavedItem(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.QualifiedItemId) || item.Stack <= 0)
            return null;

        SavedItemStack saved = new()
        {
            QualifiedItemId = item.QualifiedItemId,
            Stack = item.Stack,
            Quality = item.Quality,
            ModData = item.modData.Pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
        };

        if (item is SObject obj)
            saved.PreservedParentItemId = obj.preservedParentSheetIndex.Value;

        if (item is StardewValley.Objects.ColoredObject colored)
        {
            Color color = colored.color.Value;
            saved.HasColor = true;
            saved.ColorR = color.R;
            saved.ColorG = color.G;
            saved.ColorB = color.B;
            saved.ColorA = color.A;
        }

        return saved;
    }

    private Item? TryCreateItem(SavedItemStack saved)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(saved.QualifiedItemId) || saved.Stack <= 0)
                return null;

            Item? item = ItemRegistry.Create(saved.QualifiedItemId, saved.Stack, saved.Quality, allowNull: true);
            if (item is null)
                return null;

            if (saved.HasColor
                && StardewValley.Objects.ColoredObject.TrySetColor(
                    item,
                    new Color(saved.ColorR, saved.ColorG, saved.ColorB, saved.ColorA),
                    out StardewValley.Objects.ColoredObject? colored)
                && colored is not null)
            {
                item = colored;
            }

            item.Stack = saved.Stack;
            item.Quality = saved.Quality;

            if (item is SObject obj && !string.IsNullOrWhiteSpace(saved.PreservedParentItemId))
                obj.preservedParentSheetIndex.Value = saved.PreservedParentItemId;

            foreach ((string key, string value) in saved.ModData ?? new Dictionary<string, string>())
                item.modData[key] = value;

            return item;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not restore squad inventory item '{saved.QualifiedItemId}': {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    private void MarkStateDirty()
    {
        if (!Context.IsMainPlayer || this.saveWritesBlocked)
            return;

        this.stateRevision = this.stateRevision == long.MaxValue ? 1 : this.stateRevision + 1;
        this.stateSnapshotDirty = true;
    }

    private void RequestStateSnapshot()
    {
        if (Context.IsOnHostComputer || !Context.IsWorldReady)
            return;

        this.Helper.Multiplayer.SendMessage(
            true,
            MessageStateRequest,
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    private void SendStateSnapshot(long? playerId = null, bool force = false)
    {
        if (!Context.IsMainPlayer || this.saveWritesBlocked || (!force && !this.stateSnapshotDirty))
            return;
        if (Game1.ticks < this.nextStateSnapshotRetryTick)
            return;

        try
        {
            if (!playerId.HasValue && Game1.getOnlineFarmers().Count() <= 1)
            {
                this.stateSnapshotDirty = false;
                this.stateSnapshotFailureLogged = false;
                this.nextStateSnapshotRetryTick = 0;
                return;
            }

            long[]? playerIds = playerId.HasValue ? new[] { playerId.Value } : null;
            this.Helper.Multiplayer.SendMessage(
                this.BuildSaveData(),
                MessageStateSnapshot,
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: playerIds);

            if (!playerId.HasValue)
                this.stateSnapshotDirty = false;

            this.stateSnapshotFailureLogged = false;
            this.nextStateSnapshotRetryTick = 0;
        }
        catch (Exception ex)
        {
            // Retry on a later update, but don't turn one custom item or map
            // into an exception every half-second in the SMAPI update loop.
            this.stateSnapshotDirty = true;
            this.nextStateSnapshotRetryTick = Game1.ticks + 600;
            if (!this.stateSnapshotFailureLogged)
            {
                this.stateSnapshotFailureLogged = true;
                this.Monitor.Log($"Companion multiplayer state could not be sent and will be retried: {ex}", LogLevel.Error);
            }
        }
    }

    private void SendStateUnavailable(long playerId)
    {
        if (!Context.IsMainPlayer)
            return;

        this.Helper.Multiplayer.SendMessage(
            true,
            MessageStateUnavailable,
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { playerId });
    }

    private void ApplyStateSnapshot(SavedModState data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (Context.IsOnHostComputer || data.Revision <= this.lastAppliedStateRevision)
            return;

        PreparedStateSnapshot prepared = this.PrepareStateSnapshot(data);

        // Commit only after the full payload has been validated, normalized, and
        // materialized. A malformed custom item or member can no longer clear a
        // client's last known-good snapshot or consume its revision.
        this.ResetRuntimeState(clearProfiles: false);
        foreach ((string npcName, SquadMemberState member) in prepared.Members)
            this.members.Add(npcName, member);
        foreach ((long ownerId, bool enabled) in prepared.TaskToggles)
            this.taskToggles.Add(ownerId, enabled);
        this.squadInventory.AddRange(prepared.SquadInventory);
        this.legacyOverflowItems.AddRange(prepared.OverflowItems);
        this.replicatedHostRules = prepared.HostRules;
        this.stateRevision = prepared.Revision;
        this.stateSnapshotDirty = false;
        this.lastAppliedStateRevision = prepared.Revision;
        this.InvalidateTargetPreviews();
    }

    private PreparedStateSnapshot PrepareStateSnapshot(SavedModState data)
    {
        if (data.Version < 1 || data.Version > CurrentSaveVersion)
        {
            throw new InvalidDataException(
                $"Snapshot schema {data.Version} is outside the supported range 1-{CurrentSaveVersion}.");
        }

        if (data.Revision < 0)
            throw new InvalidDataException("The multiplayer snapshot has a negative revision.");

        List<SquadMemberState> incomingMembers = data.Members ?? new List<SquadMemberState>();
        if (incomingMembers.Count > 256)
            throw new InvalidDataException("The multiplayer snapshot contains too many companions.");

        Dictionary<string, SquadMemberState> preparedMembers = new(StringComparer.OrdinalIgnoreCase);
        foreach (SquadMemberState? incomingMember in incomingMembers)
        {
            if (incomingMember is null || string.IsNullOrWhiteSpace(incomingMember.NpcName))
                throw new InvalidDataException("The multiplayer snapshot contains an invalid or duplicate companion entry.");

            SquadMemberState member = CompanionStateCopy.CloneMember(incomingMember);
            NormalizeReplicatedMember(member);
            if (!preparedMembers.TryAdd(member.NpcName, member))
                throw new InvalidDataException($"The multiplayer snapshot contains duplicate NPC key '{member.NpcName}'.");
        }

        Dictionary<long, bool> preparedToggles = new();
        foreach ((string key, bool value) in data.TaskTogglesByPlayer ?? new Dictionary<string, bool>())
        {
            if (long.TryParse(key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ownerId))
                preparedToggles[ownerId] = value;
        }

        List<SavedItemStack> incomingSquadInventory = data.SquadInventory ?? new List<SavedItemStack>();
        List<SavedItemStack> incomingOverflow = data.LegacyOverflowItems ?? new List<SavedItemStack>();
        if (incomingSquadInventory.Count > 4096 || incomingOverflow.Count > 4096)
            throw new InvalidDataException("The multiplayer snapshot contains an unreasonable number of item stacks.");

        List<Item> preparedInventory = new();
        List<SavedItemStack> preparedOverflow = new();
        foreach (SavedItemStack? incoming in incomingSquadInventory)
        {
            SavedItemStack saved = ValidateSnapshotItem(incoming);
            Item? item = this.TryCreateItem(saved);
            if (item is not null)
                preparedInventory.Add(item);
            else
                preparedOverflow.Add(saved);
        }

        foreach (SavedItemStack? incoming in incomingOverflow)
            preparedOverflow.Add(ValidateSnapshotItem(incoming));

        CompanionHostRules? hostRules = data.HostRules is null
            ? null
            : NormalizeHostRules(data.HostRules);
        return new PreparedStateSnapshot(
            data.Revision,
            preparedMembers,
            preparedToggles,
            preparedInventory,
            preparedOverflow,
            hostRules);
    }

    private static SavedItemStack ValidateSnapshotItem(SavedItemStack? incoming)
    {
        if (incoming is null || string.IsNullOrWhiteSpace(incoming.QualifiedItemId) || incoming.Stack <= 0)
            throw new InvalidDataException("The multiplayer snapshot contains an invalid item stack.");

        return CompanionStateCopy.CloneItem(incoming);
    }

    private sealed record PreparedStateSnapshot(
        long Revision,
        Dictionary<string, SquadMemberState> Members,
        Dictionary<long, bool> TaskToggles,
        List<Item> SquadInventory,
        List<SavedItemStack> OverflowItems,
        CompanionHostRules? HostRules);

    private static CompanionHostRules NormalizeHostRules(CompanionHostRules source)
    {
        CompanionHostRules rules = CompanionStateCopy.CloneHostRules(source);
        rules.CompanionInventorySlots = Math.Clamp(rules.CompanionInventorySlots, 1, 10);
        rules.CompanionWorkRadius = Math.Clamp(rules.CompanionWorkRadius, 3, 20);
        rules.CompanionWorkReturnDistance = Math.Clamp(rules.CompanionWorkReturnDistance, rules.CompanionWorkRadius, 40);
        rules.FriendshipRequirement = Math.Clamp(rules.FriendshipRequirement, 0, 14);
        rules.MaxSquadSize = Math.Clamp(rules.MaxSquadSize, 1, 12);
        rules.ProtectBeehouseFlowers = Math.Max(0, rules.ProtectBeehouseFlowers);
        rules.HarvestingMode = Enum.IsDefined(rules.HarvestingMode) ? rules.HarvestingMode : TaskMode.Disabled;
        rules.ForagingMode = Enum.IsDefined(rules.ForagingMode) ? rules.ForagingMode : TaskMode.Disabled;
        rules.LumberingMode = Enum.IsDefined(rules.LumberingMode) ? rules.LumberingMode : TaskMode.Disabled;
        rules.MiningMode = Enum.IsDefined(rules.MiningMode) ? rules.MiningMode : TaskMode.Disabled;
        rules.WateringMode = Enum.IsDefined(rules.WateringMode) ? rules.WateringMode : TaskMode.Disabled;
        rules.PettingMode = Enum.IsDefined(rules.PettingMode) ? rules.PettingMode : TaskMode.Disabled;
        return rules;
    }

    /*
     * Keep the helpers below close to snapshot application: they are the only
     * bridge from an untrusted network DTO into client-visible runtime state.
     */
    private static void NormalizeReplicatedMember(SquadMemberState member)
    {
        if (!Enum.IsDefined(member.Mode))
            member.Mode = CompanionMode.Following;
        if (!Enum.IsDefined(member.PreferredWorkSpecialty))
            member.PreferredWorkSpecialty = CompanionWorkSpecialty.ClearArea;

        member.DisplayName ??= member.NpcName;
        member.Level = Math.Clamp(member.Level, 1, CompanionProgression.MaxLevel);
        member.Xp = Math.Max(0, member.Xp);
        member.UnspentSkillPoints = Math.Max(0, member.UnspentSkillPoints);
        member.UnlockedSkillIds = (member.UnlockedSkillIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        member.Inventory = (member.Inventory ?? new List<SavedItemStack>())
            .Where(saved => saved is not null && !string.IsNullOrWhiteSpace(saved.QualifiedItemId) && saved.Stack > 0)
            .Select(CompanionStateCopy.CloneItem)
            .ToList();
        member.RecentLoot = (member.RecentLoot ?? new List<RecentCompanionLoot>())
            .Where(loot => loot is not null && !string.IsNullOrWhiteSpace(loot.QualifiedItemId) && loot.Stack > 0)
            .OrderByDescending(loot => loot.AddedAtUtcTicks)
            .Take(RecentLootLimit)
            .Select(CompanionStateCopy.CloneLoot)
            .ToList();
        member.CurrentActivityKey ??= "companion.status.following";
        member.LastTaskResultKey ??= "";
        member.LastFailureReasonKey ??= "";
        member.CurrentTargetKey ??= "";
        member.PreviewTargetKey ??= "";
        member.PreviewReasonKey ??= "";
    }

    private bool IsCompanionProgressionEnabled()
    {
        return this.replicatedHostRules?.EnableCompanionProgression ?? this.config.EnableCompanionProgression;
    }

    private int GetCompanionInventoryCapacity()
    {
        return this.replicatedHostRules?.CompanionInventorySlots ?? this.config.CompanionInventorySlots;
    }

    private int GetConfiguredWorkRadius()
    {
        return this.replicatedHostRules?.CompanionWorkRadius ?? this.config.CompanionWorkRadius;
    }

    private TaskMode GetConfiguredTaskMode(CompanionTaskKind kind)
    {
        CompanionHostRules? rules = this.replicatedHostRules;
        return kind switch
        {
            CompanionTaskKind.Lumbering => rules?.LumberingMode ?? this.config.LumberingMode,
            CompanionTaskKind.Mining => rules?.MiningMode ?? this.config.MiningMode,
            CompanionTaskKind.Watering => rules?.WateringMode ?? this.config.WateringMode,
            CompanionTaskKind.Gathering => rules?.ForagingMode ?? this.config.ForagingMode,
            CompanionTaskKind.Harvesting => rules?.HarvestingMode ?? this.config.HarvestingMode,
            CompanionTaskKind.Petting => rules?.PettingMode ?? this.config.PettingMode,
            _ => TaskMode.Disabled
        };
    }


    private bool TryRegisterCommand(long playerId, string? commandId)
    {
        return this.commandReplayGuard.TryRegister(playerId, commandId);
    }

    private void SendActionRequest(
        string action,
        string npcName = "",
        string argument = "",
        Vector2? tile = null,
        int index = -1,
        string expectedItemToken = "",
        bool? desiredEnabled = null,
        string? expectedLocationName = null)
    {
        Vector2 normalizedTile = NormalizeTile(tile ?? Vector2.Zero);
        this.Helper.Multiplayer.SendMessage(
            new SquadActionMessage
            {
                CommandId = Guid.NewGuid().ToString("N"),
                Action = action,
                NpcName = npcName,
                Argument = argument,
                LocationName = expectedLocationName ?? Game1.currentLocation?.NameOrUniqueName ?? "",
                TileX = (int)normalizedTile.X,
                TileY = (int)normalizedTile.Y,
                Index = index,
                ExpectedItemToken = expectedItemToken,
                DesiredEnabled = desiredEnabled
            },
            MessageActionRequest,
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
}
