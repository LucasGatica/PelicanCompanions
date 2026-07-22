namespace PelicanCompanions;

/// <summary>A tool slot owned by one player/NPC pairing.</summary>
internal enum CompanionEquipmentSlot
{
    Axe = 0,
    Pickaxe = 1,
    WateringCan = 2,
    FishingRod = 3
}

/// <summary>An hourly assignment in the farm routine editor.</summary>
internal enum CompanionRoutineActivity
{
    Follow = 0,
    Wait = 1,
    VanillaRoutine = 2,
    Water = 3,
    Lumber = 4,
    Mine = 5,
    Clear = 6,
    Deposit = 7
}

/// <summary>What an NPC does when scheduled work has no remaining target.</summary>
internal enum CompanionRoutineCompletionBehavior
{
    Follow = 0,
    Wait = 1,
    VanillaRoutine = 2
}

internal sealed class CompanionEquipmentState
{
    public SavedItemStack? Axe { get; set; }
    public SavedItemStack? Pickaxe { get; set; }
    public SavedItemStack? WateringCan { get; set; }
    public SavedItemStack? FishingRod { get; set; }
}

internal sealed class CompanionRoutineHourState
{
    public int Hour { get; set; }
    public CompanionRoutineActivity Activity { get; set; } = CompanionRoutineActivity.Follow;
}

/// <summary>A remembered world area which an hourly work assignment can reactivate.</summary>
internal sealed class CompanionRoutineAreaPreset
{
    public CompanionWorkSpecialty Specialty { get; set; } = CompanionWorkSpecialty.ClearArea;
    public string LocationName { get; set; } = "";
    public CompanionWorkRegionKind RegionKind { get; set; } = CompanionWorkRegionKind.Circle;
    public int CenterX { get; set; } = -1;
    public int CenterY { get; set; } = -1;
    public int Radius { get; set; } = 8;
    public int MinX { get; set; } = -1;
    public int MinY { get; set; } = -1;
    public int Size { get; set; }
}

internal sealed class CompanionRoutineState
{
    public bool Enabled { get; set; }
    public bool RepeatDaily { get; set; } = true;
    /// <summary>The only day on which a non-repeating routine may run.</summary>
    public int ScheduledDayIndex { get; set; } = -1;
    public long Revision { get; set; }
    public CompanionRoutineCompletionBehavior CompletionBehavior { get; set; } = CompanionRoutineCompletionBehavior.Follow;
    public List<CompanionRoutineHourState> Hours { get; set; } = new();
    public List<CompanionRoutineAreaPreset> AreaPresets { get; set; } = new();
    public CompanionRoutineExecutionState Execution { get; set; } = new();
}

/// <summary>Persisted idempotency state for applying the current routine block.</summary>
/// <remarks>
/// Tasks and path controllers remain transient, but this key prevents a
/// completed work block from being restarted every ten in-game minutes or
/// after a save/reload in the same hour.
/// </remarks>
internal sealed class CompanionRoutineExecutionState
{
    public int AppliedDayIndex { get; set; } = -1;
    public int AppliedBlockHour { get; set; } = -1;
    public long AppliedRevision { get; set; } = -1;
    public int CompletedDayIndex { get; set; } = -1;
    public int CompletedBlockHour { get; set; } = -1;
}

/// <summary>A stable reference to a player chest, including an identity token so moved chests can be rediscovered.</summary>
internal sealed class CompanionChestDestinationState
{
    public string LocationName { get; set; } = "";
    public int TileX { get; set; } = -1;
    public int TileY { get; set; } = -1;
    public string ChestId { get; set; } = "";
}

/// <summary>
/// Owner-scoped NPC data. Tools, routines, and logistics never transfer to a
/// different farmer who later recruits the same NPC.
/// </summary>
internal sealed class CompanionOperationalProfileState
{
    public string NpcName { get; set; } = "";
    public long OwnerId { get; set; }
    public CompanionEquipmentState Equipment { get; set; } = new();
    public CompanionRoutineState Routine { get; set; } = new();
    public CompanionChestDestinationState? ChestDestination { get; set; }
}

/// <summary>Owner-wide logistics defaults used when an NPC has no individual chest override.</summary>
internal sealed class CompanionOwnerLogisticsState
{
    public long OwnerId { get; set; }
    public CompanionChestDestinationState? DefaultChestDestination { get; set; }
}

/// <summary>
/// Vanilla-save-side equipment checkpoint. Keeping the same tool ownership in
/// farmer modData makes a player-inventory swap recoverable even if writing the
/// mod's separate save payload fails.
/// </summary>
internal sealed class CompanionEquipmentJournalState
{
    public int Version { get; set; } = 1;
    public long OwnerId { get; set; }
    public List<CompanionEquipmentJournalEntry> Entries { get; set; } = new();
}

internal sealed class CompanionEquipmentJournalEntry
{
    public string NpcName { get; set; } = "";
    public CompanionEquipmentSlot Slot { get; set; }
    public SavedItemStack? Item { get; set; }
}

/// <summary>
/// Vanilla-save-side snapshot of every active companion cargo. It is captured
/// immediately before saving so chest/world/player mutations and their mod-side
/// cargo source are recovered from the same vanilla transaction.
/// </summary>
internal sealed class CompanionCargoJournalState
{
    public int Version { get; set; } = 2;
    public long Revision { get; set; }
    public List<CompanionCargoJournalEntry> Entries { get; set; } = new();
    public List<SavedItemStack> SquadInventory { get; set; } = new();
    public List<SavedItemStack> LegacyOverflowItems { get; set; } = new();
}

internal sealed class CompanionCargoJournalEntry
{
    public long OwnerId { get; set; }
    public string NpcName { get; set; } = "";
    public List<SavedItemStack> Inventory { get; set; } = new();
}

internal readonly record struct CompanionOperationalProfileKey(long OwnerId, string NpcName);

internal static class CompanionOperationsStateCopy
{
    public static CompanionEquipmentState CloneEquipment(CompanionEquipmentState? source)
    {
        source ??= new CompanionEquipmentState();
        return new CompanionEquipmentState
        {
            Axe = CloneOptionalItem(source.Axe),
            Pickaxe = CloneOptionalItem(source.Pickaxe),
            WateringCan = CloneOptionalItem(source.WateringCan),
            FishingRod = CloneOptionalItem(source.FishingRod)
        };
    }

    public static CompanionRoutineState CloneRoutine(CompanionRoutineState? source)
    {
        source ??= new CompanionRoutineState();
        return new CompanionRoutineState
        {
            Enabled = source.Enabled,
            RepeatDaily = source.RepeatDaily,
            ScheduledDayIndex = source.ScheduledDayIndex,
            Revision = source.Revision,
            CompletionBehavior = source.CompletionBehavior,
            Hours = (source.Hours ?? new List<CompanionRoutineHourState>())
                .Where(hour => hour is not null)
                .Select(hour => new CompanionRoutineHourState { Hour = hour.Hour, Activity = hour.Activity })
                .ToList(),
            AreaPresets = (source.AreaPresets ?? new List<CompanionRoutineAreaPreset>())
                .Where(area => area is not null)
                .Select(CloneArea)
                .ToList(),
            Execution = CloneRoutineExecution(source.Execution)
        };
    }

    public static CompanionRoutineExecutionState CloneRoutineExecution(CompanionRoutineExecutionState? source)
    {
        source ??= new CompanionRoutineExecutionState();
        return new CompanionRoutineExecutionState
        {
            AppliedDayIndex = source.AppliedDayIndex,
            AppliedBlockHour = source.AppliedBlockHour,
            AppliedRevision = source.AppliedRevision,
            CompletedDayIndex = source.CompletedDayIndex,
            CompletedBlockHour = source.CompletedBlockHour
        };
    }

    public static CompanionOperationalProfileState CloneOperationalProfile(CompanionOperationalProfileState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CompanionOperationalProfileState
        {
            NpcName = source.NpcName,
            OwnerId = source.OwnerId,
            Equipment = CloneEquipment(source.Equipment),
            Routine = CloneRoutine(source.Routine),
            ChestDestination = CloneChest(source.ChestDestination)
        };
    }

    public static CompanionOwnerLogisticsState CloneOwnerLogistics(CompanionOwnerLogisticsState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CompanionOwnerLogisticsState
        {
            OwnerId = source.OwnerId,
            DefaultChestDestination = CloneChest(source.DefaultChestDestination)
        };
    }

    public static CompanionChestDestinationState? CloneChest(CompanionChestDestinationState? source)
    {
        return source is null
            ? null
            : new CompanionChestDestinationState
            {
                LocationName = source.LocationName,
                TileX = source.TileX,
                TileY = source.TileY,
                ChestId = source.ChestId
            };
    }

    private static CompanionRoutineAreaPreset CloneArea(CompanionRoutineAreaPreset source)
    {
        return new CompanionRoutineAreaPreset
        {
            Specialty = source.Specialty,
            LocationName = source.LocationName,
            RegionKind = source.RegionKind,
            CenterX = source.CenterX,
            CenterY = source.CenterY,
            Radius = source.Radius,
            MinX = source.MinX,
            MinY = source.MinY,
            Size = source.Size
        };
    }

    private static SavedItemStack? CloneOptionalItem(SavedItemStack? source)
    {
        return source is null ? null : CompanionStateCopy.CloneItem(source);
    }
}
