using System.Text.Json;
using StardewValley;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int CargoJournalVersion = 2;
    private const int MaximumCargoJournalEntries = 256;
    private const int MaximumCargoJournalItemsPerMember = 256;
    private const int MaximumCargoJournalItemsPerStore = 4096;
    private const int MaximumCargoJournalLength = 16_000_000;
    private const string CargoJournalModDataKey = "Lucas.PelicanCompanions/CargoJournal";

    private void WriteCargoJournal()
    {
        CompanionCargoJournalState journal = new()
        {
            Version = CargoJournalVersion,
            Revision = this.stateRevision,
            Entries = this.members.Values
                .OrderBy(member => member.NpcName, StringComparer.OrdinalIgnoreCase)
                .Select(member => new CompanionCargoJournalEntry
                {
                    OwnerId = member.OwnerId,
                    NpcName = member.NpcName,
                    Inventory = (member.Inventory ?? new List<SavedItemStack>())
                        .Select(CompanionStateCopy.CloneItem)
                        .ToList()
                })
                .ToList(),
            SquadInventory = this.squadInventory
                .Select(item => this.ToSavedItem(item)
                    ?? throw new InvalidDataException(
                        $"Squad item '{item.QualifiedItemId}' couldn't be checkpointed."))
                .ToList(),
            LegacyOverflowItems = this.legacyOverflowItems
                .Select(CompanionStateCopy.CloneItem)
                .ToList()
        };
        if (journal.Entries.Count > MaximumCargoJournalEntries
            || journal.Entries.Any(entry => entry.Inventory.Count > MaximumCargoJournalItemsPerMember)
            || journal.SquadInventory.Count > MaximumCargoJournalItemsPerStore
            || journal.LegacyOverflowItems.Count > MaximumCargoJournalItemsPerStore)
        {
            throw new InvalidDataException("The companion cargo checkpoint is unreasonably large.");
        }

        string json = JsonSerializer.Serialize(journal);
        if (json.Length > MaximumCargoJournalLength)
            throw new InvalidDataException("The companion cargo checkpoint is unreasonably large.");
        Game1.MasterPlayer.modData[CargoJournalModDataKey] = json;
    }

    private static void InvalidateCargoJournal()
    {
        // A missing checkpoint defers to the regular payload. It is safer than
        // leaving an older, otherwise-valid snapshot which could be mistaken
        // for the vanilla transaction currently being saved.
        Game1.MasterPlayer.modData.Remove(CargoJournalModDataKey);
    }

    private void ApplyCargoJournal(SavedModState data)
    {
        if (!Game1.MasterPlayer.modData.TryGetValue(CargoJournalModDataKey, out string? json)
            || string.IsNullOrWhiteSpace(json))
        {
            return;
        }
        if (json.Length > MaximumCargoJournalLength)
            throw new InvalidDataException("The companion cargo checkpoint is unreasonably large.");

        CompanionCargoJournalState journal = JsonSerializer.Deserialize<CompanionCargoJournalState>(json)
            ?? throw new InvalidDataException("The companion cargo checkpoint is empty or malformed.");
        if (journal.Version is not 1 and not CargoJournalVersion
            || journal.Revision < 0
            || journal.Entries is null
            || journal.Entries.Count > MaximumCargoJournalEntries
            || journal.SquadInventory is null
            || journal.LegacyOverflowItems is null
            || journal.SquadInventory.Count > MaximumCargoJournalItemsPerStore
            || journal.LegacyOverflowItems.Count > MaximumCargoJournalItemsPerStore)
        {
            throw new InvalidDataException("The companion cargo checkpoint has an unsupported version, revision, or size.");
        }

        // The checkpoint is the cargo authority whenever it is valid because
        // it commits in the same vanilla transaction as chests, farmer
        // inventories, and world state. This also covers the inverse failure:
        // if a mod payload advanced but the later vanilla commit did not, the
        // older checkpoint must restore the matching pre-save cargo.

        Dictionary<string, CompanionCargoJournalEntry> prepared =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (CompanionCargoJournalEntry? entry in journal.Entries)
        {
            if (entry is null
                || entry.OwnerId <= 0
                || string.IsNullOrWhiteSpace(entry.NpcName)
                || entry.NpcName.Length > MaximumPersistedLocationNameLength
                || entry.Inventory is null
                || entry.Inventory.Count > MaximumCargoJournalItemsPerMember
                || !prepared.TryAdd(entry.NpcName, entry))
            {
                throw new InvalidDataException("The companion cargo checkpoint contains an invalid or duplicate entry.");
            }

            List<SavedItemStack> normalized = new(entry.Inventory.Count);
            foreach (SavedItemStack? incoming in entry.Inventory)
            {
                SavedItemStack item = ValidateSnapshotItem(incoming);
                if (item.HasToolData
                    || item.QualifiedItemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The companion cargo checkpoint contains a tool outside equipment.");
                }
                normalized.Add(item);
            }
            entry.Inventory = normalized;
        }

        if (journal.Version >= 2)
        {
            data.SquadInventory = journal.SquadInventory
                .Select(ValidateSnapshotItem)
                .ToList();
            data.LegacyOverflowItems = journal.LegacyOverflowItems
                .Select(ValidateSnapshotItem)
                .ToList();
        }
        else
        {
            data.SquadInventory ??= new List<SavedItemStack>();
            data.LegacyOverflowItems ??= new List<SavedItemStack>();
        }

        HashSet<string> consumedEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (SquadMemberState? member in data.Members ?? Enumerable.Empty<SquadMemberState>())
        {
            if (member is null || string.IsNullOrWhiteSpace(member.NpcName))
                continue;
            if (!prepared.TryGetValue(member.NpcName, out CompanionCargoJournalEntry? entry)
                || entry.OwnerId != member.OwnerId)
            {
                // The journal is a complete active-cargo snapshot. Clearing a
                // stale payload member's cargo prevents a dismissed/reassigned
                // source stack from being resurrected beside its vanilla-side
                // destination if the mod payload write failed.
                member.Inventory = new List<SavedItemStack>();
                continue;
            }

            member.Inventory = entry.Inventory
                .Select(CompanionStateCopy.CloneItem)
                .ToList();
            consumedEntries.Add(entry.NpcName);
        }

        // A newer vanilla transaction may contain cargo for a just-recruited
        // member which isn't present in an older mod payload. Preserve those
        // stacks in recovery instead of dropping an otherwise valid journal
        // entry merely because its gameplay roster couldn't be reconstructed.
        foreach (CompanionCargoJournalEntry entry in prepared.Values
            .Where(entry => !consumedEntries.Contains(entry.NpcName)))
        {
            data.LegacyOverflowItems.AddRange(entry.Inventory
                .Select(CompanionStateCopy.CloneItem));
        }
    }
}
