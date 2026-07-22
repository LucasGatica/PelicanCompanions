using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int MaximumPersistedLocationNameLength = 256;
    private const int MaximumPersistedChestIdLength = 64;
    private const int EquipmentJournalVersion = 1;
    private const int MaximumEquipmentJournalEntries = 4096;
    private const int MaximumEquipmentJournalLength = 1_000_000;
    private const string EquipmentJournalModDataKey = "Lucas.PelicanCompanions/EquipmentJournal";
    private const string LegacyEquipmentRecoveryOwnerKey = "Lucas.PelicanCompanions/InternalRecoveryOwner";
    private const string LegacyEquipmentRecoveryNpcKey = "Lucas.PelicanCompanions/InternalRecoveryNpc";

    private readonly record struct EquipmentJournalSnapshot(bool Exists, string Value);

    private static CompanionOperationalProfileKey GetOperationalProfileKey(long ownerId, string npcName)
    {
        return CompanionEquipmentPolicy.CreateKey(ownerId, npcName);
    }

    private bool TryGetOperationalProfile(
        long ownerId,
        string npcName,
        out CompanionOperationalProfileState profile)
    {
        return this.operationalProfiles.TryGetValue(GetOperationalProfileKey(ownerId, npcName), out profile!);
    }

    private CompanionOperationalProfileState GetOrCreateOperationalProfile(long ownerId, string npcName)
    {
        if (ownerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(ownerId));
        if (string.IsNullOrWhiteSpace(npcName))
            throw new ArgumentException("An NPC name is required.", nameof(npcName));

        CompanionOperationalProfileKey key = GetOperationalProfileKey(ownerId, npcName);
        if (this.operationalProfiles.TryGetValue(key, out CompanionOperationalProfileState? existing))
            return existing;

        CompanionOperationalProfileState created = new()
        {
            OwnerId = ownerId,
            NpcName = npcName,
            Equipment = new CompanionEquipmentState(),
            Routine = new CompanionRoutineState()
        };
        this.operationalProfiles.Add(key, created);
        return created;
    }

    private static EquipmentJournalSnapshot CaptureEquipmentJournal(Farmer owner)
    {
        return owner.modData.TryGetValue(EquipmentJournalModDataKey, out string? value)
            ? new EquipmentJournalSnapshot(true, value ?? "")
            : new EquipmentJournalSnapshot(false, "");
    }

    private static void RestoreEquipmentJournal(Farmer owner, EquipmentJournalSnapshot snapshot)
    {
        if (snapshot.Exists)
            owner.modData[EquipmentJournalModDataKey] = snapshot.Value;
        else
            owner.modData.Remove(EquipmentJournalModDataKey);
    }

    private void WriteOwnerEquipmentJournal(Farmer owner)
    {
        long ownerId = owner.UniqueMultiplayerID;
        CompanionEquipmentJournalState journal = new()
        {
            Version = EquipmentJournalVersion,
            OwnerId = ownerId
        };
        foreach (CompanionOperationalProfileState profile in this.operationalProfiles.Values
            .Where(profile => profile.OwnerId == ownerId)
            .OrderBy(profile => profile.NpcName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (CompanionEquipmentSlot slot in Enum.GetValues<CompanionEquipmentSlot>())
            {
                SavedItemStack? item = GetEquipmentSlot(profile.Equipment, slot);
                journal.Entries.Add(new CompanionEquipmentJournalEntry
                {
                    NpcName = profile.NpcName,
                    Slot = slot,
                    Item = item is null ? null : CompanionStateCopy.CloneItem(item)
                });
            }
        }

        string json = JsonSerializer.Serialize(journal);
        if (json.Length > MaximumEquipmentJournalLength)
            throw new InvalidDataException("The equipment checkpoint is unreasonably large.");
        owner.modData[EquipmentJournalModDataKey] = json;
    }

    private void RefreshOwnerEquipmentJournals()
    {
        HashSet<long> ownersWithProfiles = this.operationalProfiles.Values
            .Select(profile => profile.OwnerId)
            .ToHashSet();
        foreach (Farmer owner in Game1.getAllFarmers())
        {
            if (ownersWithProfiles.Contains(owner.UniqueMultiplayerID)
                || owner.modData.ContainsKey(EquipmentJournalModDataKey))
            {
                this.WriteOwnerEquipmentJournal(owner);
            }
        }
    }

    private void ApplyOwnerEquipmentJournals(bool missingJournalIsAuthoritativeEmpty)
    {
        foreach (Farmer owner in Game1.getAllFarmers())
        {
            if (!owner.modData.TryGetValue(EquipmentJournalModDataKey, out string? json)
                || string.IsNullOrWhiteSpace(json))
            {
                if (missingJournalIsAuthoritativeEmpty)
                    this.ClearOwnerEquipmentWithoutJournal(owner.UniqueMultiplayerID);
                continue;
            }
            if (json.Length > MaximumEquipmentJournalLength)
                throw new InvalidDataException("An equipment checkpoint is unreasonably large.");

            CompanionEquipmentJournalState journal = JsonSerializer.Deserialize<CompanionEquipmentJournalState>(json)
                ?? throw new InvalidDataException("An equipment checkpoint is empty or malformed.");
            if (journal.Version != EquipmentJournalVersion
                || journal.OwnerId != owner.UniqueMultiplayerID
                || journal.Entries is null
                || journal.Entries.Count > MaximumEquipmentJournalEntries)
            {
                throw new InvalidDataException("An equipment checkpoint has an unsupported version, owner, or size.");
            }

            Dictionary<string, Dictionary<CompanionEquipmentSlot, SavedItemStack?>> grouped =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (CompanionEquipmentJournalEntry? entry in journal.Entries)
            {
                if (entry is null
                    || string.IsNullOrWhiteSpace(entry.NpcName)
                    || entry.NpcName.Length > MaximumPersistedLocationNameLength
                    || !Enum.IsDefined(entry.Slot))
                {
                    throw new InvalidDataException("An equipment checkpoint contains an invalid entry.");
                }

                if (!grouped.TryGetValue(entry.NpcName, out Dictionary<CompanionEquipmentSlot, SavedItemStack?>? slots))
                {
                    slots = new Dictionary<CompanionEquipmentSlot, SavedItemStack?>();
                    grouped.Add(entry.NpcName, slots);
                }
                if (!slots.TryAdd(
                        entry.Slot,
                        entry.Item is null ? null : CompanionStateCopy.CloneItem(entry.Item)))
                {
                    throw new InvalidDataException("An equipment checkpoint contains a duplicate NPC/slot entry.");
                }
            }

            Dictionary<CompanionOperationalProfileKey, CompanionOperationalProfileState> prepared = new();
            foreach ((string npcName, Dictionary<CompanionEquipmentSlot, SavedItemStack?> slots) in grouped)
            {
                if (slots.Count != Enum.GetValues<CompanionEquipmentSlot>().Length)
                    throw new InvalidDataException("An equipment checkpoint doesn't contain all four slots for an NPC.");

                CompanionOperationalProfileKey key = GetOperationalProfileKey(owner.UniqueMultiplayerID, npcName);
                CompanionOperationalProfileState profile = this.operationalProfiles.TryGetValue(key, out CompanionOperationalProfileState? existing)
                    ? CompanionOperationsStateCopy.CloneOperationalProfile(existing)
                    : new CompanionOperationalProfileState
                    {
                        OwnerId = owner.UniqueMultiplayerID,
                        NpcName = npcName,
                        Equipment = new CompanionEquipmentState(),
                        Routine = new CompanionRoutineState()
                    };
                profile.Equipment = new CompanionEquipmentState();
                foreach ((CompanionEquipmentSlot slot, SavedItemStack? item) in slots)
                    SetEquipmentSlot(profile.Equipment, slot, item);
                this.NormalizeOperationalProfile(profile, rejectUnavailableTools: false);
                prepared.Add(key, profile);
            }

            // The journal is a complete equipment snapshot for this owner, not
            // a sparse patch. A payload profile absent from the vanilla-side
            // checkpoint may belong to a mod payload which advanced without
            // the matching farmer-inventory commit; clear its slots before the
            // prepared overlay so the same tool can't exist on both sides.
            foreach ((CompanionOperationalProfileKey key, CompanionOperationalProfileState existing) in
                     this.operationalProfiles
                         .Where(pair => pair.Key.OwnerId == owner.UniqueMultiplayerID)
                         .ToList())
            {
                if (prepared.ContainsKey(key))
                    continue;

                CompanionOperationalProfileState cleared =
                    CompanionOperationsStateCopy.CloneOperationalProfile(existing);
                cleared.Equipment = new CompanionEquipmentState();
                this.operationalProfiles[key] = cleared;
            }

            foreach ((CompanionOperationalProfileKey key, CompanionOperationalProfileState profile) in prepared)
                this.operationalProfiles[key] = profile;
        }
    }

    private void ClearOwnerEquipmentWithoutJournal(long ownerId)
    {
        foreach ((CompanionOperationalProfileKey key, CompanionOperationalProfileState existing) in
                 this.operationalProfiles
                     .Where(pair => pair.Key.OwnerId == ownerId)
                     .ToList())
        {
            CompanionOperationalProfileState cleared =
                CompanionOperationsStateCopy.CloneOperationalProfile(existing);
            cleared.Equipment = new CompanionEquipmentState();
            this.operationalProfiles[key] = cleared;
        }
    }

    private void NormalizeOperationalProfile(
        CompanionOperationalProfileState profile,
        bool rejectUnavailableTools)
    {
        if (profile.OwnerId <= 0 || string.IsNullOrWhiteSpace(profile.NpcName))
            throw new InvalidDataException("An operational companion profile has an invalid owner or NPC name.");

        profile.Equipment ??= new CompanionEquipmentState();
        this.NormalizeEquipmentSlot(profile.Equipment, CompanionEquipmentSlot.Axe, rejectUnavailableTools);
        this.NormalizeEquipmentSlot(profile.Equipment, CompanionEquipmentSlot.Pickaxe, rejectUnavailableTools);
        this.NormalizeEquipmentSlot(profile.Equipment, CompanionEquipmentSlot.WateringCan, rejectUnavailableTools);
        this.NormalizeEquipmentSlot(profile.Equipment, CompanionEquipmentSlot.FishingRod, rejectUnavailableTools);

        profile.Routine ??= new CompanionRoutineState();
        profile.Routine.Revision = Math.Max(0, profile.Routine.Revision);
        profile.Routine.ScheduledDayIndex = Math.Max(-1, profile.Routine.ScheduledDayIndex);
        if (!Enum.IsDefined(profile.Routine.CompletionBehavior))
            profile.Routine.CompletionBehavior = CompanionRoutineCompletionBehavior.Follow;

        profile.Routine.Execution ??= new CompanionRoutineExecutionState();
        profile.Routine.Execution.AppliedDayIndex = Math.Max(-1, profile.Routine.Execution.AppliedDayIndex);
        profile.Routine.Execution.AppliedBlockHour = NormalizePersistedRoutineHour(profile.Routine.Execution.AppliedBlockHour);
        profile.Routine.Execution.AppliedRevision = Math.Max(-1, profile.Routine.Execution.AppliedRevision);
        profile.Routine.Execution.CompletedDayIndex = Math.Max(-1, profile.Routine.Execution.CompletedDayIndex);
        profile.Routine.Execution.CompletedBlockHour = NormalizePersistedRoutineHour(profile.Routine.Execution.CompletedBlockHour);

        if (rejectUnavailableTools)
        {
            foreach (CompanionRoutineAreaPreset? incomingArea in profile.Routine.AreaPresets
                         ?? Enumerable.Empty<CompanionRoutineAreaPreset>())
            {
                if (incomingArea?.LocationName?.Length > MaximumPersistedLocationNameLength)
                    throw new InvalidDataException("A routine area location name is unreasonably long.");
            }

            ValidateSnapshotChestText(profile.ChestDestination);
        }

        profile.Routine.Hours = CompanionRoutinePolicy.NormalizeHours(profile.Routine.Hours).ToList();
        profile.Routine.AreaPresets = (profile.Routine.AreaPresets ?? new List<CompanionRoutineAreaPreset>())
            .Where(CompanionRoutinePolicy.IsValidAreaPreset)
            .Select(area => new CompanionRoutineAreaPreset
            {
                Specialty = area.Specialty,
                LocationName = area.LocationName,
                RegionKind = area.RegionKind,
                CenterX = area.CenterX,
                CenterY = area.CenterY,
                Radius = area.RegionKind == CompanionWorkRegionKind.Circle
                    ? CompanionWorkAreaPolicy.NormalizeRadius(area.Radius)
                    : area.Radius,
                MinX = area.MinX,
                MinY = area.MinY,
                Size = area.RegionKind == CompanionWorkRegionKind.DelimitedSquare
                    ? CompanionWorkAreaPolicy.NormalizeSquareSize(area.Size)
                    : area.Size
            })
            .GroupBy(area => area.Specialty)
            .Select(group => group.Last())
            .ToList();

        if (!CompanionChestRoutingPolicy.IsValid(profile.ChestDestination))
            profile.ChestDestination = null;
    }

    private void NormalizeOwnerLogistics(CompanionOwnerLogisticsState logistics)
    {
        if (logistics.OwnerId <= 0)
            throw new InvalidDataException("An owner logistics record has an invalid owner ID.");
        if (!CompanionChestRoutingPolicy.IsValid(logistics.DefaultChestDestination))
            logistics.DefaultChestDestination = null;
    }

    private void NormalizeOwnerLogistics(
        CompanionOwnerLogisticsState logistics,
        bool rejectUntrustedText)
    {
        if (rejectUntrustedText)
            ValidateSnapshotChestText(logistics.DefaultChestDestination);
        this.NormalizeOwnerLogistics(logistics);
    }

    private static int NormalizePersistedRoutineHour(int hour)
    {
        return hour is >= CompanionRoutinePolicy.FirstHour and <= CompanionRoutinePolicy.LastHour
            ? hour
            : -1;
    }

    private static void ValidateSnapshotChestText(CompanionChestDestinationState? destination)
    {
        if (destination?.LocationName?.Length > MaximumPersistedLocationNameLength
            || destination?.ChestId?.Length > MaximumPersistedChestIdLength)
        {
            throw new InvalidDataException("A chest destination contains an unreasonably long string.");
        }
    }

    private void NormalizeEquipmentSlot(
        CompanionEquipmentState equipment,
        CompanionEquipmentSlot slot,
        bool rejectUnavailableTools)
    {
        SavedItemStack? saved = GetEquipmentSlot(equipment, slot);
        if (saved is null)
            return;
        if (string.IsNullOrWhiteSpace(saved.QualifiedItemId) || saved.Stack != 1)
            throw new InvalidDataException($"Equipment slot '{slot}' contains an invalid item stack.");

        if (!saved.HasToolData
            || !CompanionEquipmentPolicy.IsValidUpgradeLevel(saved.ToolUpgradeLevel)
            || slot == CompanionEquipmentSlot.WateringCan
                && !CompanionEquipmentPolicy.IsValidWateringCanState(saved.ToolUpgradeLevel, saved.WateringCanWaterLeft)
            || slot != CompanionEquipmentSlot.WateringCan && saved.WateringCanWaterLeft != 0)
        {
            throw new InvalidDataException($"Equipment slot '{slot}' contains invalid persisted tool state.");
        }

        saved.ModData ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Item? item = this.TryCreateItem(saved);
        if (item is null)
        {
            // The host may legitimately have a tool whose content provider is
            // absent on this client. Preserve the authoritative DTO and render
            // it as unavailable instead of rejecting the entire snapshot.
            if (!saved.QualifiedItemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Equipment slot '{slot}' doesn't contain a tool.");
            return;
        }

        if (!IsToolForSlot(item, slot))
            throw new InvalidDataException($"Equipment slot '{slot}' contains incompatible item '{saved.QualifiedItemId}'.");
    }

    private List<SavedItemStack> MigrateLegacyMemberEquipment(SquadMemberState member, bool migrateIntoSlots)
    {
        CompanionOperationalProfileState operational = this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName);
        return this.MigrateLegacyMemberEquipment(member, operational, migrateIntoSlots, logMigration: true);
    }

    private List<SavedItemStack> MigrateLegacyMemberEquipment(
        SquadMemberState member,
        CompanionOperationalProfileState operational,
        bool migrateIntoSlots,
        bool logMigration)
    {
        List<SavedItemStack> displaced = new();
        member.Inventory ??= new List<SavedItemStack>();
        for (int index = member.Inventory.Count - 1; index >= 0; index--)
        {
            SavedItemStack? saved = member.Inventory[index];
            if (saved is null)
                continue;
            Item? item = this.TryCreateItem(saved);
            if (item is not Tool tool)
            {
                if (item is null
                    && saved.QualifiedItemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
                {
                    // A temporarily unavailable legacy tool can't be typed into
                    // one of the four vanilla slots yet. Keep it in recovery,
                    // but tag the original owner/NPC so it never becomes shared
                    // squad loot when the content provider returns.
                    displaced.Add(CreateOwnerScopedLegacyEquipmentRecovery(saved, member));
                    member.Inventory.RemoveAt(index);
                }
                continue;
            }

            SavedItemStack faithful = this.ToSavedItem(tool)
                ?? throw new InvalidDataException($"Legacy tool '{saved.QualifiedItemId}' couldn't be migrated.");
            if (!TryGetEquipmentSlot(tool, out CompanionEquipmentSlot slot))
            {
                // Unsupported/custom tools can't remain mixed into schema-13
                // cargo, since clients deliberately reject that ambiguity.
                // Preserve the exact stack in recovery instead.
                displaced.Add(CreateOwnerScopedLegacyEquipmentRecovery(faithful, member));
                member.Inventory.RemoveAt(index);
                continue;
            }

            if (migrateIntoSlots && GetEquipmentSlot(operational.Equipment, slot) is null)
            {
                SetEquipmentSlot(operational.Equipment, slot, faithful);
                if (logMigration)
                {
                    this.Monitor.Log(
                        $"Migrated legacy companion tool '{saved.QualifiedItemId}' into {member.OwnerId}/{member.NpcName}/{slot}.",
                        LogLevel.Info);
                }
            }
            else
            {
                // A pathological old save may contain multiple rods. Preserve
                // extras in the legacy recovery inventory without leaving tools
                // mixed into task loot/cargo.
                displaced.Add(CreateOwnerScopedLegacyEquipmentRecovery(faithful, member));
            }

            member.Inventory.RemoveAt(index);
        }

        displaced.Reverse();
        return displaced;
    }

    private static SavedItemStack CreateOwnerScopedLegacyEquipmentRecovery(
        SavedItemStack saved,
        SquadMemberState member)
    {
        SavedItemStack scoped = CompanionStateCopy.CloneItem(saved);
        scoped.ModData[LegacyEquipmentRecoveryOwnerKey] = member.OwnerId.ToString();
        scoped.ModData[LegacyEquipmentRecoveryNpcKey] = member.NpcName;
        return scoped;
    }

    private static bool TryTakeLegacyEquipmentRecoveryOwner(
        SavedItemStack saved,
        out long ownerId,
        out string npcName)
    {
        saved.ModData ??= new Dictionary<string, string>(StringComparer.Ordinal);
        ownerId = 0;
        long parsedOwnerId = 0;
        bool scoped = saved.ModData.TryGetValue(LegacyEquipmentRecoveryOwnerKey, out string? rawOwner)
            && long.TryParse(rawOwner, out parsedOwnerId)
            && parsedOwnerId > 0;
        if (scoped)
            ownerId = parsedOwnerId;
        npcName = saved.ModData.TryGetValue(LegacyEquipmentRecoveryNpcKey, out string? rawNpc)
            ? rawNpc ?? ""
            : "";
        saved.ModData.Remove(LegacyEquipmentRecoveryOwnerKey);
        saved.ModData.Remove(LegacyEquipmentRecoveryNpcKey);
        return scoped;
    }

    private bool TryGetEquippedTool<TTool>(
        SquadMemberState member,
        CompanionEquipmentSlot slot,
        out TTool tool)
        where TTool : Tool
    {
        tool = null!;
        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName);
        SavedItemStack? saved = GetEquipmentSlot(profile.Equipment, slot);
        if (saved is null || this.TryCreateItem(saved) is not TTool restored)
            return false;

        tool = restored;
        return true;
    }

    private Item? GetCompanionEquipmentItem(SquadMemberState member, CompanionEquipmentSlot slot)
    {
        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName);
        SavedItemStack? saved = GetEquipmentSlot(profile.Equipment, slot);
        return saved is null ? null : this.TryCreateItem(saved);
    }

    private bool HasCompanionEquipmentItem(SquadMemberState member, CompanionEquipmentSlot slot)
    {
        return GetEquipmentSlot(
            this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName).Equipment,
            slot) is not null;
    }

    /// <summary>Whether a companion can execute a task with its own equipped tool right now.</summary>
    private bool HasUsableCompanionToolForTask(SquadMemberState member, CompanionTaskKind kind)
    {
        return kind switch
        {
            CompanionTaskKind.Lumbering => this.TryGetEquippedTool<Axe>(
                member,
                CompanionEquipmentSlot.Axe,
                out _),
            CompanionTaskKind.Mining => this.TryGetEquippedTool<Pickaxe>(
                member,
                CompanionEquipmentSlot.Pickaxe,
                out _),
            CompanionTaskKind.Watering => this.TryGetEquippedTool(
                    member,
                    CompanionEquipmentSlot.WateringCan,
                    out WateringCan wateringCan)
                && wateringCan.WaterLeft > 0,
            CompanionTaskKind.Fishing => this.TryGetEquippedTool<FishingRod>(
                member,
                CompanionEquipmentSlot.FishingRod,
                out _),
            _ => true
        };
    }

    private bool HasEmptyCompanionWateringCan(SquadMemberState member)
    {
        return this.TryGetEquippedTool(
                member,
                CompanionEquipmentSlot.WateringCan,
                out WateringCan wateringCan)
            && wateringCan.WaterLeft <= 0;
    }

    private string GetRequiredEquipmentWarningKey(
        CompanionTaskKind kind,
        IEnumerable<SquadMemberState>? candidates = null)
    {
        if (kind == CompanionTaskKind.Watering
            && candidates?.Any(this.HasEmptyCompanionWateringCan) == true)
        {
            return "tasks.watering_can_empty";
        }

        return kind switch
        {
            CompanionTaskKind.Lumbering => "tasks.need_axe",
            CompanionTaskKind.Mining => "tasks.need_pickaxe",
            CompanionTaskKind.Watering => "tasks.need_watering_can",
            CompanionTaskKind.Fishing => "fishing.no_rod",
            _ => "commands.no_followers"
        };
    }

    private string GetRequiredEquipmentFailureKey(
        SquadMemberState member,
        CompanionTaskKind kind)
    {
        if (kind == CompanionTaskKind.Watering
            && this.HasEmptyCompanionWateringCan(member))
        {
            return "companion.task_failure.watering_can_empty";
        }

        return kind switch
        {
            CompanionTaskKind.Lumbering => "companion.task_failure.need_axe",
            CompanionTaskKind.Mining => "companion.task_failure.need_pickaxe",
            CompanionTaskKind.Watering => "companion.task_failure.need_watering_can",
            _ => "companion.task_failure.no_valid_target"
        };
    }

    private bool ChangeCompanionEquipment(SquadMemberState member, CompanionEquipmentSlot slot)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId))
            return false;

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        int itemIndex = owner?.CurrentToolIndex ?? -1;
        if (owner is null || itemIndex < 0 || itemIndex >= owner.Items.Count)
            return false;

        Item? selected = owner.Items[itemIndex];
        string selectedToken = "";
        if (selected is not null)
        {
            if (selected is not Tool selectedTool
                || !IsToolForSlot(selectedTool, slot)
                || !this.TryPrepareToolForEquipment(selectedTool, slot, ownerId, out SavedItemStack selectedSaved))
            {
                this.Warn("companion.equipment.wrong_tool", new { slot = this.Tr(GetEquipmentSlotNameKey(slot)) });
                return false;
            }
            selectedToken = SavedItemStackIdentity.CreateToken(selectedSaved);
        }

        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(ownerId, member.NpcName);
        string stateToken = GetEquipmentStateToken(GetEquipmentSlot(profile.Equipment, slot));
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest(
                "SetCompanionEquipment",
                member.NpcName,
                slot.ToString(),
                index: itemIndex,
                expectedItemToken: selectedToken,
                expectedStateToken: stateToken);
            return true;
        }

        return this.SetCompanionEquipment(
            member,
            slot,
            itemIndex,
            ownerId,
            selectedToken,
            stateToken);
    }

    private bool SetCompanionEquipment(
        SquadMemberState member,
        CompanionEquipmentSlot slot,
        int itemIndex,
        long ownerId,
        string? expectedItemToken,
        string? expectedStateToken)
    {
        if (!Enum.IsDefined(slot) || !this.CanOwnerMutate(member, ownerId, showWarning: false))
            return false;

        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner is null || itemIndex < 0 || itemIndex >= owner.Items.Count)
            return this.RejectStaleEquipmentCommand(ownerId);

        CompanionOperationalProfileState profile = this.GetOrCreateOperationalProfile(ownerId, member.NpcName);
        SavedItemStack? previousSaved = GetEquipmentSlot(profile.Equipment, slot);
        if (!string.Equals(GetEquipmentStateToken(previousSaved), expectedStateToken, StringComparison.Ordinal))
            return this.RejectStaleEquipmentCommand(ownerId);

        Item? selectedItem = owner.Items[itemIndex];
        SavedItemStack? selectedSaved = null;
        if (selectedItem is null)
        {
            if (!string.IsNullOrEmpty(expectedItemToken))
                return this.RejectStaleEquipmentCommand(ownerId);
            if (previousSaved is null)
                return false;
        }
        else
        {
            if (selectedItem is not Tool selectedTool
                || !IsToolForSlot(selectedTool, slot)
                || !this.TryPrepareToolForEquipment(selectedTool, slot, ownerId, out selectedSaved)
                || !SavedItemStackIdentity.Matches(selectedSaved, expectedItemToken))
            {
                return this.RejectStaleEquipmentCommand(ownerId);
            }
        }

        Item? previousItem = null;
        if (previousSaved is not null)
        {
            previousItem = this.TryCreateItem(previousSaved);
            if (previousItem is not Tool || !IsToolForSlot(previousItem, slot))
            {
                if (this.ShouldShowFeedbackFor(ownerId))
                    this.Warn("companion.equipment.unavailable");
                return false;
            }
        }

        SavedItemStack? rollbackSlot = previousSaved is null ? null : CompanionStateCopy.CloneItem(previousSaved);
        Item? rollbackSelected = selectedItem;
        EquipmentJournalSnapshot journalBefore;
        try
        {
            journalBefore = CaptureEquipmentJournal(owner);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Equipment transfer for '{member.NpcName}/{slot}' couldn't checkpoint the previous state: {ex}", LogLevel.Error);
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_failed");
            return false;
        }

        try
        {
            // This is a direct swap with the selected hotbar cell. No tool ever
            // passes through companion cargo, withdraw-all, or chest routing.
            owner.Items[itemIndex] = previousItem;
            SetEquipmentSlot(
                profile.Equipment,
                slot,
                selectedSaved is null ? null : CompanionStateCopy.CloneItem(selectedSaved));
            this.WriteOwnerEquipmentJournal(owner);
        }
        catch (Exception ex)
        {
            try
            {
                owner.Items[itemIndex] = rollbackSelected;
                SetEquipmentSlot(profile.Equipment, slot, rollbackSlot);
                RestoreEquipmentJournal(owner, journalBefore);
            }
            catch (Exception rollbackError)
            {
                this.Monitor.Log($"Equipment rollback also failed for '{member.NpcName}/{slot}': {rollbackError}", LogLevel.Error);
            }

            this.Monitor.Log($"Equipment transfer failed for '{member.NpcName}/{slot}' and was rolled back: {ex}", LogLevel.Error);
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("multiplayer.command_failed");
            return false;
        }

        this.MarkStateDirty();
        if (this.ShouldShowFeedbackFor(ownerId))
        {
            this.Info(selectedSaved is null
                ? "companion.equipment.removed"
                : previousSaved is null
                    ? "companion.equipment.equipped"
                    : "companion.equipment.swapped", new
            {
                npc = member.DisplayName,
                slot = this.Tr(GetEquipmentSlotNameKey(slot)),
                item = selectedItem?.DisplayName ?? previousItem?.DisplayName ?? ""
            });
        }
        return true;
    }

    private bool TryPrepareToolForEquipment(
        Tool tool,
        CompanionEquipmentSlot slot,
        long ownerId,
        out SavedItemStack saved)
    {
        saved = null!;
        try
        {
            if (tool.Stack != 1 || !IsToolForSlot(tool, slot))
                return false;
            if (tool.enchantments.Count > 0 || tool.previousEnchantments.Count > 0)
            {
                if (this.ShouldShowFeedbackFor(ownerId))
                    this.Warn("companion.equipment.enchantments_unsupported");
                return false;
            }
            if (tool is FishingRod fishingRod && fishingRod.attachments.Any(attachment => attachment is not null))
            {
                if (this.ShouldShowFeedbackFor(ownerId))
                    this.Warn("companion.equipment.attachments_unsupported");
                return false;
            }

            SavedItemStack serialized = this.ToSavedItem(tool)
                ?? throw new InvalidOperationException($"Couldn't serialize tool '{tool.QualifiedItemId}'.");
            if (this.TryCreateItem(serialized) is not Tool restored
                || !IsToolForSlot(restored, slot)
                || restored.UpgradeLevel != tool.UpgradeLevel
                || tool is WateringCan sourceCan && (restored is not WateringCan restoredCan || restoredCan.WaterLeft != sourceCan.WaterLeft)
                || tool is FishingRod sourceRod && (restored is not FishingRod restoredRod || restoredRod.AttachmentSlotsCount != sourceRod.AttachmentSlotsCount))
            {
                if (this.ShouldShowFeedbackFor(ownerId))
                    this.Warn("companion.equipment.unsupported");
                return false;
            }

            saved = serialized;
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not validate tool '{tool.QualifiedItemId}' for companion equipment: {ex}", LogLevel.Error);
            if (this.ShouldShowFeedbackFor(ownerId))
                this.Warn("companion.equipment.unsupported");
            return false;
        }
    }

    private bool RejectStaleEquipmentCommand(long ownerId)
    {
        if (this.ShouldShowFeedbackFor(ownerId))
            this.Warn("multiplayer.command_stale");
        return false;
    }

    private static SavedItemStack? GetEquipmentSlot(CompanionEquipmentState equipment, CompanionEquipmentSlot slot)
    {
        return slot switch
        {
            CompanionEquipmentSlot.Axe => equipment.Axe,
            CompanionEquipmentSlot.Pickaxe => equipment.Pickaxe,
            CompanionEquipmentSlot.WateringCan => equipment.WateringCan,
            CompanionEquipmentSlot.FishingRod => equipment.FishingRod,
            _ => null
        };
    }

    private static void SetEquipmentSlot(
        CompanionEquipmentState equipment,
        CompanionEquipmentSlot slot,
        SavedItemStack? item)
    {
        switch (slot)
        {
            case CompanionEquipmentSlot.Axe:
                equipment.Axe = item;
                break;
            case CompanionEquipmentSlot.Pickaxe:
                equipment.Pickaxe = item;
                break;
            case CompanionEquipmentSlot.WateringCan:
                equipment.WateringCan = item;
                break;
            case CompanionEquipmentSlot.FishingRod:
                equipment.FishingRod = item;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    private static bool TryGetEquipmentSlot(Tool tool, out CompanionEquipmentSlot slot)
    {
        Type type = tool.GetType();
        slot = type switch
        {
            _ when type == typeof(Axe) => CompanionEquipmentSlot.Axe,
            _ when type == typeof(Pickaxe) => CompanionEquipmentSlot.Pickaxe,
            _ when type == typeof(WateringCan) => CompanionEquipmentSlot.WateringCan,
            _ when type == typeof(FishingRod) => CompanionEquipmentSlot.FishingRod,
            _ => (CompanionEquipmentSlot)(-1)
        };
        return Enum.IsDefined(slot);
    }

    private static bool IsToolForSlot(Item item, CompanionEquipmentSlot slot)
    {
        Type type = item.GetType();
        return slot switch
        {
            CompanionEquipmentSlot.Axe => type == typeof(Axe),
            CompanionEquipmentSlot.Pickaxe => type == typeof(Pickaxe),
            CompanionEquipmentSlot.WateringCan => type == typeof(WateringCan),
            CompanionEquipmentSlot.FishingRod => type == typeof(FishingRod),
            _ => false
        };
    }

    private static string GetEquipmentStateToken(SavedItemStack? saved)
    {
        return saved is null ? "EMPTY" : SavedItemStackIdentity.CreateToken(saved);
    }

    private static string GetEquipmentSlotNameKey(CompanionEquipmentSlot slot)
    {
        return slot switch
        {
            CompanionEquipmentSlot.Axe => "companion.equipment.slot.axe",
            CompanionEquipmentSlot.Pickaxe => "companion.equipment.slot.pickaxe",
            CompanionEquipmentSlot.WateringCan => "companion.equipment.slot.watering_can",
            CompanionEquipmentSlot.FishingRod => "companion.equipment.slot.fishing_rod",
            _ => "companion.equipment.slot.unknown"
        };
    }
}
