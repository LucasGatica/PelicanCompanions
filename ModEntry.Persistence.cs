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

    private void RestorePersistedMemberPositions(
        bool logFailures = true,
        bool restoreWorkAreas = true,
        bool retryUnavailableWorkAreasOnly = false)
    {
        foreach (SquadMemberState member in this.members.Values)
        {
            try
            {
                if (retryUnavailableWorkAreasOnly
                    && this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: false))
                {
                    continue;
                }

                NPC? npc = this.GetNpcByName(member.NpcName);
                bool workAreaNeedsRecovery = npc?.currentLocation is null
                    || !string.Equals(
                        npc.currentLocation.NameOrUniqueName,
                        member.WorkAreaLocationName,
                        StringComparison.Ordinal)
                    || this.workAreaPositionRecoveryNeeded.Contains(member.NpcName);
                bool restoreWorkArea = restoreWorkAreas
                    && member.WorkAreaActive
                    && !string.IsNullOrWhiteSpace(member.WorkAreaLocationName)
                    && (!retryUnavailableWorkAreasOnly || workAreaNeedsRecovery);
                bool restoreWaiting = member.Mode is CompanionMode.Waiting or CompanionMode.ParkedForDisconnect
                    && !string.IsNullOrWhiteSpace(member.WaitingLocationName);
                if (!restoreWorkArea && !restoreWaiting)
                {
                    continue;
                }

                // An explicit Wait pauses (but doesn't forget) an area order,
                // so its exact waiting position takes precedence on reload.
                string locationName = restoreWaiting
                    ? member.WaitingLocationName!
                    : member.WorkAreaLocationName;
                GameLocation? location = Game1.getLocationFromName(locationName);
                bool restoringActiveWorkArea = restoreWorkArea
                    && !restoreWaiting
                    && member.Mode == CompanionMode.Following;
                if (restoringActiveWorkArea)
                    this.workAreaPositionRecoveryNeeded.Add(member.NpcName);
                if (npc is null || location is null)
                {
                    if (restoringActiveWorkArea && npc is not null)
                    {
                        this.SetCompanionActivity(member, "companion.status.work_area_paused");
                        this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
                    }
                    if (logFailures)
                    {
                        this.Monitor.Log(
                            $"Could not restore persisted companion position for '{member.NpcName}' in '{locationName}'. The saved state was kept.",
                            LogLevel.Warn);
                    }
                    continue;
                }

                Vector2 desiredTile = restoreWaiting
                    ? NormalizeTile(new Vector2(member.WaitingTileX, member.WaitingTileY))
                    : GetPersistedWorkAreaRestoreTile(member, npc, location);
                if (npc.currentLocation != location || NormalizeTile(npc.Tile) != desiredTile)
                {
                    if (!this.PlaceNpc(npc, location, desiredTile))
                    {
                        if (restoringActiveWorkArea)
                        {
                            this.SetCompanionActivity(member, "companion.status.work_area_paused");
                            this.SetTaskFailure(member, "companion.task_failure.work_area_unavailable");
                        }
                        if (logFailures)
                            this.Monitor.Log($"Could not find a safe persisted tile for '{member.NpcName}' in '{locationName}'.", LogLevel.Warn);
                        continue;
                    }

                    Vector2 restoredTile = NormalizeTile(npc.Tile);
                    if (restoreWaiting && restoredTile != desiredTile)
                    {
                        member.WaitingTileX = restoredTile.X;
                        member.WaitingTileY = restoredTile.Y;
                        this.MarkStateDirty();
                    }
                }

                this.DisableNpcSchedule(npc, stopCurrentRoute: true);
                if (restoringActiveWorkArea)
                {
                    this.workAreaPositionRecoveryNeeded.Remove(member.NpcName);
                    if (!this.AreTasksEnabled(member.OwnerId))
                    {
                        this.SetCompanionActivity(member, "companion.status.work_area_paused");
                        this.SetTaskFailure(member, "companion.task_failure.tasks_disabled");
                    }
                    else
                    {
                        if (member.LastFailureReasonKey is "companion.task_failure.work_area_unavailable"
                            or "companion.task_failure.tasks_disabled")
                        {
                            this.SetTaskFailure(member, "");
                        }
                        this.SetCompanionActivity(member, "companion.status.work_area");
                        this.priorityTaskPlanningMembers.Add(member.NpcName);
                        int deferredScanTick = Game1.ticks + 1;
                        this.nextTaskScanTick = this.nextTaskScanTick <= Game1.ticks
                            ? deferredScanTick
                            : Math.Min(this.nextTaskScanTick, deferredScanTick);
                    }
                }
            }
            catch (Exception ex)
            {
                if (logFailures)
                    this.Monitor.Log($"Waiting position restoration failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
            }
        }
    }

    private static Vector2 GetPersistedWorkAreaRestoreTile(
        SquadMemberState member,
        NPC npc,
        GameLocation location)
    {
        if (member.WorkAreaRegionKind == CompanionWorkRegionKind.DelimitedSquare)
        {
            int centerOffset = Math.Max(0, member.WorkAreaSize - 1) / 2;
            return NormalizeTile(new Vector2(
                member.WorkAreaMinX + centerOffset,
                member.WorkAreaMinY + centerOffset));
        }

        if (member.WorkAreaRegionKind == CompanionWorkRegionKind.FarmWide
            && npc.currentLocation == location)
        {
            return NormalizeTile(npc.Tile);
        }

        int fallbackX = Math.Max(0, member.WorkAreaCenterX);
        int fallbackY = Math.Max(0, member.WorkAreaCenterY);
        return NormalizeTile(new Vector2(fallbackX, fallbackY));
    }

    private void ClearFollowState(string npcName)
    {
        this.controlledNpcLeases.RemoveWhere(npc => string.Equals(npc.Name, npcName, StringComparison.OrdinalIgnoreCase));
        this.ClearFollowNavigationState(npcName);
        foreach (FollowPathTargetKey key in this.failedFollowPathTargets.Keys
            .Where(key => string.Equals(key.NpcName, npcName, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            this.failedFollowPathTargets.Remove(key);
        }
    }

    private void ClearFollowNavigationState(string npcName)
    {
        this.lastFollowTargets.Remove(npcName);
        this.lastFollowTargetDistances.Remove(npcName);
        this.lastFollowPathTicks.Remove(npcName);
        this.lastFollowProgressPositions.Remove(npcName);
        this.activeRecallTargets.Remove(npcName);
        this.activeRecallActivatedTicks.Remove(npcName);
        this.recoveredFollowTargets.Remove(npcName);
        this.followNoProgressTicks.Remove(npcName);
        this.lastDisconnectedProbeTicks.Remove(npcName);
        this.followRecoveryUntilTick.Remove(npcName);
        this.disconnectedFollowRecovery.Remove(npcName);
        this.disconnectedFollowBackoffs.Remove(npcName);
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

        member.WorkAreaOrderId ??= "";
        member.WorkAreaLocationName ??= "";
        if (member.WorkAreaActive && !CompanionWorkAreaPolicy.IsActiveStateValid(
                member.WorkAreaActive,
                member.WorkAreaOrderId,
                member.WorkAreaLocationName,
                member.WorkAreaRegionKind,
                member.WorkAreaCenterX,
                member.WorkAreaCenterY,
                member.WorkAreaRadius,
                member.WorkAreaMinX,
                member.WorkAreaMinY,
                member.WorkAreaSize,
                member.WorkAreaSpecialty))
        {
            ClearPersistedWorkArea(member);
        }
        else if (!member.WorkAreaActive)
        {
            if (!Enum.IsDefined(member.WorkAreaSpecialty))
                member.WorkAreaSpecialty = CompanionWorkSpecialty.ClearArea;
            NormalizeInactivePersistedWorkArea(member);
        }

        if (!member.ClearArea)
        {
            if (member.SearchWood && !member.SearchMining && !member.SearchWatering)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Wood;
            else if (member.SearchMining && !member.SearchWood && !member.SearchWatering)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Mining;
            else if (member.SearchWatering && !member.SearchWood && !member.SearchMining)
                member.PreferredWorkSpecialty = CompanionWorkSpecialty.Watering;
        }

        this.NormalizeLoadedProfile(member.Profile);
        member.RecentDialogueKeys = (member.RecentDialogueKeys ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(4)
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
            CompanionMode.OriginalRoutine => "companion.status.original_routine",
            _ when member.WorkAreaActive => "companion.status.work_area",
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
        return overflow;
    }

    private void NormalizeLoadedProfile(CompanionProfileState profile)
    {
        if (string.IsNullOrWhiteSpace(profile.NpcName))
            throw new InvalidDataException("The companion profile list contains an unnamed entry.");

        profile.Level = Math.Clamp(
            profile.Level <= 0 ? CompanionProgression.GetLevelForXp(profile.Xp) : profile.Level,
            1,
            CompanionProgression.MaxLevel);
        profile.Xp = Math.Max(0, profile.Xp);
        int actualLevel = CompanionProgression.GetLevelForXp(profile.Xp);
        if (actualLevel > profile.Level)
        {
            profile.UnspentSkillPoints += actualLevel - profile.Level;
            profile.Level = actualLevel;
        }

        if (profile.Level >= CompanionProgression.MaxLevel && !profile.BonusLevelTenPointGranted)
        {
            profile.UnspentSkillPoints++;
            profile.BonusLevelTenPointGranted = true;
        }

        profile.UnlockedSkillIds ??= new List<string>();
        profile.UnlockedSkillIds = profile.UnlockedSkillIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        profile.UnspentSkillPoints += CompanionProgression.GetLegacySkillPointRefund(profile.UnlockedSkillIds);
        profile.UnspentSkillPoints = Math.Max(0, profile.UnspentSkillPoints);
        profile.UnlockedSkillIds = profile.UnlockedSkillIds
            .Where(id => CompanionProgression.Skills.Any(skill => string.Equals(skill.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.RecentLoot = (profile.RecentLoot ?? new List<RecentCompanionLoot>())
            .Where(loot => loot is not null && !string.IsNullOrWhiteSpace(loot.QualifiedItemId) && loot.Stack > 0)
            .OrderByDescending(loot => loot.AddedAtUtcTicks)
            .Take(RecentLootLimit)
            .ToList();
    }

    private CompanionProfileState GetOrCreateCompanionProfile(string npcName)
    {
        if (this.companionProfiles.TryGetValue(npcName, out CompanionProfileState? profile))
            return profile;

        profile = CompanionProfilePolicy.Create(npcName);
        this.companionProfiles.Add(npcName, profile);
        return profile;
    }

    private void ReloadOverflowInventoryIntoSquad()
    {
        if (this.legacyOverflowItems.Count == 0)
            return;

        int index = 0;
        while (index < this.legacyOverflowItems.Count)
        {
            SavedItemStack saved = this.legacyOverflowItems[index];
            SavedItemStack materialized = CompanionStateCopy.CloneItem(saved);
            bool ownerScoped = TryTakeLegacyEquipmentRecoveryOwner(
                materialized,
                out long recoveryOwnerId,
                out string recoveryNpcName);
            Item? item = this.TryCreateItem(materialized);
            if (item is null)
            {
                // The providing content mod may be temporarily missing. Keep the
                // raw stack in save data so reinstalling that content can restore it.
                index++;
                continue;
            }

            if (ownerScoped)
            {
                Farmer? recoveryOwner = Game1.getAllFarmers()
                    .FirstOrDefault(farmer => farmer.UniqueMultiplayerID == recoveryOwnerId);
                if (recoveryOwner is null)
                {
                    index++;
                    continue;
                }

                int recoveryOriginalStack = saved.Stack;
                int inventoryBefore = CountInventoryStack(recoveryOwner, materialized.QualifiedItemId);
                Item? ownerRemainder;
                try
                {
                    ownerRemainder = recoveryOwner.addItemToInventory(item);
                }
                catch (Exception ex)
                {
                    int transferred = Math.Clamp(
                        CountInventoryStack(recoveryOwner, materialized.QualifiedItemId) - inventoryBefore,
                        0,
                        recoveryOriginalStack);
                    if (transferred >= recoveryOriginalStack)
                        this.legacyOverflowItems.RemoveAt(index);
                    else if (transferred > 0)
                    {
                        saved.Stack = recoveryOriginalStack - transferred;
                        index++;
                    }
                    else
                    {
                        index++;
                    }

                    if (transferred > 0)
                        this.MarkStateDirty();
                    this.Monitor.Log(
                        $"Owner-scoped equipment recovery failed for '{recoveryNpcName}/{saved.QualifiedItemId}' and was isolated: {ex}",
                        LogLevel.Error);
                    continue;
                }

                int recoveryRemainingStack = Math.Clamp(
                    ownerRemainder?.Stack ?? 0,
                    0,
                    recoveryOriginalStack);
                int recoveryMoved = recoveryOriginalStack - recoveryRemainingStack;
                if (recoveryMoved <= 0)
                {
                    index++;
                    continue;
                }

                if (recoveryRemainingStack == 0)
                    this.legacyOverflowItems.RemoveAt(index);
                else
                {
                    saved.Stack = recoveryRemainingStack;
                    index++;
                }

                this.MarkStateDirty();
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
            CompanionProfiles = this.companionProfiles.Values.Select(CompanionStateCopy.CloneProfile).ToList(),
            OperationalProfiles = this.operationalProfiles.Values
                .Select(CompanionOperationsStateCopy.CloneOperationalProfile)
                .ToList(),
            OwnerLogistics = this.ownerLogistics.Values
                .Select(CompanionOperationsStateCopy.CloneOwnerLogistics)
                .ToList(),
            NpcCosmetics = this.npcCosmetics.Values.Select(CompanionStateCopy.CloneCosmetic).ToList(),
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

        if (item is Tool tool)
        {
            saved.HasToolData = true;
            saved.ToolUpgradeLevel = tool.UpgradeLevel;
            if (tool is WateringCan wateringCan)
                saved.WateringCanWaterLeft = wateringCan.WaterLeft;
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

            if (saved.HasToolData)
            {
                if (item is not Tool tool)
                    return null;
                if (!CompanionEquipmentPolicy.IsValidUpgradeLevel(saved.ToolUpgradeLevel))
                    return null;
                if (item is WateringCan)
                {
                    if (!CompanionEquipmentPolicy.IsValidWateringCanState(
                            saved.ToolUpgradeLevel,
                            saved.WateringCanWaterLeft))
                    {
                        return null;
                    }
                }
                else if (saved.WateringCanWaterLeft != 0)
                {
                    return null;
                }

                tool.UpgradeLevel = saved.ToolUpgradeLevel;
                if (tool is WateringCan wateringCan)
                    wateringCan.WaterLeft = saved.WateringCanWaterLeft;
            }

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
        this.ResetRuntimeState(clearProfiles: false, preserveCosmeticRuntime: true);
        foreach ((string npcName, CompanionProfileState profile) in prepared.CompanionProfiles)
            this.companionProfiles.Add(npcName, profile);
        foreach ((CompanionOperationalProfileKey key, CompanionOperationalProfileState profile) in prepared.OperationalProfiles)
            this.operationalProfiles.Add(key, profile);
        foreach ((long ownerId, CompanionOwnerLogisticsState logistics) in prepared.OwnerLogistics)
            this.ownerLogistics.Add(ownerId, logistics);
        foreach ((string npcName, SquadMemberState member) in prepared.Members)
            this.members.Add(npcName, member);
        foreach ((string npcName, NpcCosmeticState cosmetic) in prepared.NpcCosmetics)
            this.npcCosmetics.Add(npcName, cosmetic);
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

        List<CompanionProfileState> incomingProfiles = data.CompanionProfiles ?? new List<CompanionProfileState>();
        if (incomingProfiles.Count > 256)
            throw new InvalidDataException("The multiplayer snapshot contains too many companion profiles.");

        Dictionary<string, CompanionProfileState> preparedProfiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompanionProfileState? incomingProfile in incomingProfiles)
        {
            if (incomingProfile is null || string.IsNullOrWhiteSpace(incomingProfile.NpcName))
                throw new InvalidDataException("The multiplayer snapshot contains an invalid companion profile.");

            CompanionProfileState profile = CompanionStateCopy.CloneProfile(incomingProfile);
            NormalizeReplicatedProfile(profile);
            if (!preparedProfiles.TryAdd(profile.NpcName, profile))
                throw new InvalidDataException($"The multiplayer snapshot contains duplicate profile key '{profile.NpcName}'.");
        }

        List<CompanionOperationalProfileState> incomingOperationalProfiles = data.OperationalProfiles
            ?? new List<CompanionOperationalProfileState>();
        if (incomingOperationalProfiles.Count > 1024)
            throw new InvalidDataException("The multiplayer snapshot contains too many operational companion profiles.");

        Dictionary<CompanionOperationalProfileKey, CompanionOperationalProfileState> preparedOperationalProfiles = new();
        foreach (CompanionOperationalProfileState? incoming in incomingOperationalProfiles)
        {
            if (incoming is null)
                throw new InvalidDataException("The multiplayer snapshot contains a null operational companion profile.");

            CompanionOperationalProfileState operational = CompanionOperationsStateCopy.CloneOperationalProfile(incoming);
            this.NormalizeOperationalProfile(operational, rejectUnavailableTools: true);
            CompanionOperationalProfileKey key = GetOperationalProfileKey(operational.OwnerId, operational.NpcName);
            if (!preparedOperationalProfiles.TryAdd(key, operational))
            {
                throw new InvalidDataException(
                    $"The multiplayer snapshot contains duplicate operational owner/NPC key '{operational.OwnerId}/{operational.NpcName}'.");
            }
        }

        List<CompanionOwnerLogisticsState> incomingOwnerLogistics = data.OwnerLogistics
            ?? new List<CompanionOwnerLogisticsState>();
        if (incomingOwnerLogistics.Count > 256)
            throw new InvalidDataException("The multiplayer snapshot contains too many owner logistics records.");

        Dictionary<long, CompanionOwnerLogisticsState> preparedOwnerLogistics = new();
        foreach (CompanionOwnerLogisticsState? incoming in incomingOwnerLogistics)
        {
            if (incoming is null)
                throw new InvalidDataException("The multiplayer snapshot contains a null owner logistics record.");

            CompanionOwnerLogisticsState logistics = CompanionOperationsStateCopy.CloneOwnerLogistics(incoming);
            this.NormalizeOwnerLogistics(logistics, rejectUntrustedText: true);
            if (!preparedOwnerLogistics.TryAdd(logistics.OwnerId, logistics))
                throw new InvalidDataException($"The multiplayer snapshot contains duplicate owner logistics ID '{logistics.OwnerId}'.");
        }

        List<SquadMemberState> incomingMembers = data.Members ?? new List<SquadMemberState>();
        if (incomingMembers.Count > 256)
            throw new InvalidDataException("The multiplayer snapshot contains too many companions.");

        Dictionary<string, SquadMemberState> preparedMembers = new(StringComparer.OrdinalIgnoreCase);
        List<SavedItemStack> preparedOverflow = new();
        foreach (SquadMemberState? incomingMember in incomingMembers)
        {
            if (incomingMember is null || string.IsNullOrWhiteSpace(incomingMember.NpcName))
                throw new InvalidDataException("The multiplayer snapshot contains an invalid or duplicate companion entry.");

            SquadMemberState member = CompanionStateCopy.CloneMember(incomingMember);
            NormalizeReplicatedMember(member);
            if (!preparedProfiles.TryGetValue(member.NpcName, out CompanionProfileState? profile))
            {
                if (data.Version >= CompanionProfilesSaveVersion)
                    throw new InvalidDataException($"Active companion '{member.NpcName}' has no permanent profile.");

                profile = CompanionProfilePolicy.MigrateLegacyMember(member);
                NormalizeReplicatedProfile(profile);
                preparedProfiles.Add(profile.NpcName, profile);
            }

            CompanionProfilePolicy.Attach(member, profile);
            if (data.Version >= OperationalProfilesSaveVersion
                && member.Inventory.Any(saved => saved is not null
                    && (saved.HasToolData
                        || saved.QualifiedItemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidDataException($"Companion '{member.NpcName}' has a tool mixed into cargo in a schema-13 snapshot.");
            }

            CompanionOperationalProfileKey operationalKey = GetOperationalProfileKey(member.OwnerId, member.NpcName);
            if (!preparedOperationalProfiles.TryGetValue(operationalKey, out CompanionOperationalProfileState? operational))
            {
                if (data.Version >= OperationalProfilesSaveVersion)
                {
                    throw new InvalidDataException(
                        $"Active companion '{member.OwnerId}/{member.NpcName}' has no operational profile.");
                }

                operational = new CompanionOperationalProfileState
                {
                    OwnerId = member.OwnerId,
                    NpcName = member.NpcName,
                    Equipment = new CompanionEquipmentState(),
                    Routine = new CompanionRoutineState()
                };
                preparedOperationalProfiles.Add(operationalKey, operational);
            }

            List<SavedItemStack> displacedTools = this.MigrateLegacyMemberEquipment(
                member,
                operational,
                migrateIntoSlots: data.Version < OperationalProfilesSaveVersion,
                logMigration: false);
            if (data.Version >= OperationalProfilesSaveVersion && displacedTools.Count > 0)
                throw new InvalidDataException($"Companion '{member.NpcName}' has a tool mixed into cargo in a schema-13 snapshot.");
            preparedOverflow.AddRange(displacedTools);
            this.NormalizeOperationalProfile(operational, rejectUnavailableTools: true);

            if (!preparedMembers.TryAdd(member.NpcName, member))
                throw new InvalidDataException($"The multiplayer snapshot contains duplicate NPC key '{member.NpcName}'.");
        }

        Dictionary<long, bool> preparedToggles = new();
        foreach ((string key, bool value) in data.TaskTogglesByPlayer ?? new Dictionary<string, bool>())
        {
            if (long.TryParse(key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ownerId))
                preparedToggles[ownerId] = value;
        }

        List<NpcCosmeticState> incomingCosmetics = data.NpcCosmetics ?? new List<NpcCosmeticState>();
        if (incomingCosmetics.Count > 256)
            throw new InvalidDataException("The multiplayer snapshot contains too many NPC cosmetics.");

        Dictionary<string, NpcCosmeticState> preparedCosmetics = new(StringComparer.OrdinalIgnoreCase);
        foreach (NpcCosmeticState? incoming in incomingCosmetics)
        {
            NpcCosmeticState cosmetic = this.ValidateAndCloneNpcCosmetic(incoming, "The multiplayer snapshot");
            if (!preparedCosmetics.TryAdd(cosmetic.NpcName, cosmetic))
                throw new InvalidDataException($"The multiplayer snapshot contains duplicate cosmetic NPC key '{cosmetic.NpcName}'.");
        }

        List<SavedItemStack> incomingSquadInventory = data.SquadInventory ?? new List<SavedItemStack>();
        List<SavedItemStack> incomingOverflow = data.LegacyOverflowItems ?? new List<SavedItemStack>();
        if (incomingSquadInventory.Count > 4096 || incomingOverflow.Count > 4096)
            throw new InvalidDataException("The multiplayer snapshot contains an unreasonable number of item stacks.");

        List<Item> preparedInventory = new();
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
            preparedProfiles,
            preparedOperationalProfiles,
            preparedOwnerLogistics,
            preparedMembers,
            preparedCosmetics,
            preparedToggles,
            preparedInventory,
            preparedOverflow,
            hostRules);
    }

    private static SavedItemStack ValidateSnapshotItem(SavedItemStack? incoming)
    {
        if (incoming is null || string.IsNullOrWhiteSpace(incoming.QualifiedItemId) || incoming.Stack <= 0)
            throw new InvalidDataException("The multiplayer snapshot contains an invalid item stack.");
        if (incoming.HasToolData
            && (incoming.Stack != 1
                || !incoming.QualifiedItemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase)
                || !CompanionEquipmentPolicy.IsValidUpgradeLevel(incoming.ToolUpgradeLevel)
                || !CompanionEquipmentPolicy.IsValidWateringCanState(
                    incoming.ToolUpgradeLevel,
                    incoming.WateringCanWaterLeft)))
        {
            throw new InvalidDataException("The multiplayer snapshot contains invalid persisted tool state.");
        }
        if (!incoming.HasToolData
            && (incoming.ToolUpgradeLevel != 0 || incoming.WateringCanWaterLeft != 0))
        {
            throw new InvalidDataException("The multiplayer snapshot contains tool fields without tool state.");
        }

        return CompanionStateCopy.CloneItem(incoming);
    }

    private sealed record PreparedStateSnapshot(
        long Revision,
        Dictionary<string, CompanionProfileState> CompanionProfiles,
        Dictionary<CompanionOperationalProfileKey, CompanionOperationalProfileState> OperationalProfiles,
        Dictionary<long, CompanionOwnerLogisticsState> OwnerLogistics,
        Dictionary<string, SquadMemberState> Members,
        Dictionary<string, NpcCosmeticState> NpcCosmetics,
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
        member.WorkAreaOrderId ??= "";
        member.WorkAreaLocationName ??= "";
        if (member.WorkAreaActive && !CompanionWorkAreaPolicy.IsActiveStateValid(
                member.WorkAreaActive,
                member.WorkAreaOrderId,
                member.WorkAreaLocationName,
                member.WorkAreaRegionKind,
                member.WorkAreaCenterX,
                member.WorkAreaCenterY,
                member.WorkAreaRadius,
                member.WorkAreaMinX,
                member.WorkAreaMinY,
                member.WorkAreaSize,
                member.WorkAreaSpecialty))
        {
            ClearPersistedWorkArea(member);
        }
        else if (!member.WorkAreaActive)
        {
            if (!Enum.IsDefined(member.WorkAreaSpecialty))
                member.WorkAreaSpecialty = CompanionWorkSpecialty.ClearArea;
            NormalizeInactivePersistedWorkArea(member);
        }

        member.DisplayName ??= member.NpcName;
        member.RecentDialogueKeys = (member.RecentDialogueKeys ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(4)
            .ToList();
        member.Inventory = (member.Inventory ?? new List<SavedItemStack>())
            .Where(saved => saved is not null && !string.IsNullOrWhiteSpace(saved.QualifiedItemId) && saved.Stack > 0)
            .Select(CompanionStateCopy.CloneItem)
            .ToList();
        member.CurrentActivityKey ??= "companion.status.following";
        member.LastTaskResultKey ??= "";
        member.LastFailureReasonKey ??= "";
        member.CurrentTargetKey ??= "";
        member.PreviewTargetKey ??= "";
        member.PreviewReasonKey ??= "";
    }

    private static void NormalizeReplicatedProfile(CompanionProfileState profile)
    {
        if (string.IsNullOrWhiteSpace(profile.NpcName))
            throw new InvalidDataException("The multiplayer snapshot contains an unnamed companion profile.");

        profile.Level = Math.Clamp(profile.Level, 1, CompanionProgression.MaxLevel);
        profile.Xp = Math.Max(0, profile.Xp);
        profile.UnspentSkillPoints = Math.Max(0, profile.UnspentSkillPoints);
        profile.UnlockedSkillIds = (profile.UnlockedSkillIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.RecentLoot = (profile.RecentLoot ?? new List<RecentCompanionLoot>())
            .Where(loot => loot is not null && !string.IsNullOrWhiteSpace(loot.QualifiedItemId) && loot.Stack > 0)
            .OrderByDescending(loot => loot.AddedAtUtcTicks)
            .Take(RecentLootLimit)
            .Select(CompanionStateCopy.CloneLoot)
            .ToList();
    }

    private static void ClearPersistedWorkArea(SquadMemberState member)
    {
        member.WorkAreaActive = false;
        member.WorkAreaOrderId = "";
        member.WorkAreaLocationName = "";
        member.WorkAreaRegionKind = CompanionWorkRegionKind.Circle;
        member.WorkAreaCenterX = -1;
        member.WorkAreaCenterY = -1;
        member.WorkAreaRadius = 8;
        member.WorkAreaMinX = -1;
        member.WorkAreaMinY = -1;
        member.WorkAreaSize = 0;
        member.WorkAreaSpecialty = CompanionWorkSpecialty.ClearArea;
    }

    private static void NormalizeInactivePersistedWorkArea(SquadMemberState member)
    {
        if (!Enum.IsDefined(member.WorkAreaRegionKind))
            member.WorkAreaRegionKind = CompanionWorkRegionKind.Circle;

        member.WorkAreaRadius = CompanionWorkAreaPolicy.NormalizeRadius(member.WorkAreaRadius);
        if (member.WorkAreaRegionKind == CompanionWorkRegionKind.DelimitedSquare)
        {
            if (CompanionWorkAreaPolicy.IsSquareGeometryValid(
                    member.WorkAreaMinX,
                    member.WorkAreaMinY,
                    member.WorkAreaSize))
            {
                member.WorkAreaSize = CompanionWorkAreaPolicy.NormalizeSquareSize(member.WorkAreaSize);
                return;
            }

            member.WorkAreaRegionKind = CompanionWorkRegionKind.Circle;
        }

        member.WorkAreaMinX = -1;
        member.WorkAreaMinY = -1;
        member.WorkAreaSize = 0;
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

    private string SendActionRequest(
        string action,
        string npcName = "",
        string argument = "",
        Vector2? tile = null,
        int index = -1,
        string expectedItemToken = "",
        string expectedStateToken = "",
        bool? desiredEnabled = null,
        string? expectedLocationName = null)
    {
        Vector2 normalizedTile = NormalizeTile(tile ?? Vector2.Zero);
        string commandId = Guid.NewGuid().ToString("N");
        this.Helper.Multiplayer.SendMessage(
            new SquadActionMessage
            {
                CommandId = commandId,
                Action = action,
                NpcName = npcName,
                Argument = argument,
                LocationName = expectedLocationName ?? Game1.currentLocation?.NameOrUniqueName ?? "",
                TileX = (int)normalizedTile.X,
                TileY = (int)normalizedTile.Y,
                Index = index,
                ExpectedItemToken = expectedItemToken,
                ExpectedStateToken = expectedStateToken,
                DesiredEnabled = desiredEnabled
            },
            MessageActionRequest,
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        return commandId;
    }
}
