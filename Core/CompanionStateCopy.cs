namespace PelicanCompanions;

/// <summary>Creates detached copies of persisted and replicated state.</summary>
internal static class CompanionStateCopy
{
    public static SquadMemberState CloneMember(SquadMemberState source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SquadMemberState
        {
            NpcName = source.NpcName,
            DisplayName = source.DisplayName,
            OwnerId = source.OwnerId,
            Mode = source.Mode,
            OriginalLocationName = source.OriginalLocationName,
            OriginalTileX = source.OriginalTileX,
            OriginalTileY = source.OriginalTileY,
            HasOriginalPosition = source.HasOriginalPosition,
            OriginalDayIndex = source.OriginalDayIndex,
            OriginalScheduleCaptured = source.OriginalScheduleCaptured,
            OriginalScheduleKey = source.OriginalScheduleKey,
            OriginalPetBehavior = source.OriginalPetBehavior,
            OriginalSpousePatioActivity = source.OriginalSpousePatioActivity,
            OriginalMovementSpeedCaptured = source.OriginalMovementSpeedCaptured,
            OriginalMovementSpeed = source.OriginalMovementSpeed,
            OriginalAddedSpeed = source.OriginalAddedSpeed,
            WaitingLocationName = source.WaitingLocationName,
            WaitingTileX = source.WaitingTileX,
            WaitingTileY = source.WaitingTileY,
            ParkedAtUtcTicks = source.ParkedAtUtcTicks,
            LastDialogueUtcTicks = source.LastDialogueUtcTicks,
            RecentDialogueKeys = (source.RecentDialogueKeys ?? new List<string>()).ToList(),
            Profile = CloneProfile(source.Profile),
            Inventory = (source.Inventory ?? new List<SavedItemStack>()).Select(CloneItem).ToList(),
            SearchWood = source.SearchWood,
            SearchMining = source.SearchMining,
            SearchWatering = source.SearchWatering,
            ClearArea = source.ClearArea,
            CurrentWorkIsDirect = source.CurrentWorkIsDirect,
            PreferredWorkSpecialty = source.PreferredWorkSpecialty,
            WorkAreaActive = source.WorkAreaActive,
            WorkAreaOrderId = source.WorkAreaOrderId,
            WorkAreaLocationName = source.WorkAreaLocationName,
            WorkAreaRegionKind = source.WorkAreaRegionKind,
            WorkAreaCenterX = source.WorkAreaCenterX,
            WorkAreaCenterY = source.WorkAreaCenterY,
            WorkAreaRadius = source.WorkAreaRadius,
            WorkAreaMinX = source.WorkAreaMinX,
            WorkAreaMinY = source.WorkAreaMinY,
            WorkAreaSize = source.WorkAreaSize,
            WorkAreaSpecialty = source.WorkAreaSpecialty,
            CurrentActivityKey = source.CurrentActivityKey,
            LastTaskResultKey = source.LastTaskResultKey,
            LastFailureReasonKey = source.LastFailureReasonKey,
            CurrentTargetKey = source.CurrentTargetKey,
            CurrentTargetX = source.CurrentTargetX,
            CurrentTargetY = source.CurrentTargetY,
            PreviewTargetKey = source.PreviewTargetKey,
            PreviewReasonKey = source.PreviewReasonKey,
            PreviewTargetX = source.PreviewTargetX,
            PreviewTargetY = source.PreviewTargetY
        };
    }

    public static CompanionProfileState CloneProfile(CompanionProfileState source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new CompanionProfileState
        {
            NpcName = source.NpcName,
            Level = source.Level,
            Xp = source.Xp,
            UnspentSkillPoints = source.UnspentSkillPoints,
            BonusLevelTenPointGranted = source.BonusLevelTenPointGranted,
            UnlockedSkillIds = (source.UnlockedSkillIds ?? new List<string>()).ToList(),
            RecentLoot = (source.RecentLoot ?? new List<RecentCompanionLoot>()).Select(CloneLoot).ToList()
        };
    }

    public static SavedItemStack CloneItem(SavedItemStack source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SavedItemStack
        {
            QualifiedItemId = source.QualifiedItemId,
            Stack = source.Stack,
            Quality = source.Quality,
            ModData = new Dictionary<string, string>(
                source.ModData ?? new Dictionary<string, string>(),
                StringComparer.Ordinal),
            PreservedParentItemId = source.PreservedParentItemId,
            HasColor = source.HasColor,
            ColorR = source.ColorR,
            ColorG = source.ColorG,
            ColorB = source.ColorB,
            ColorA = source.ColorA,
            HasToolData = source.HasToolData,
            ToolUpgradeLevel = source.ToolUpgradeLevel,
            WateringCanWaterLeft = source.WateringCanWaterLeft
        };
    }

    public static NpcCosmeticState CloneCosmetic(NpcCosmeticState source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new NpcCosmeticState
        {
            NpcName = source.NpcName,
            EquippedHat = source.EquippedHat is null ? null : CloneItem(source.EquippedHat)
        };
    }

    public static RecentCompanionLoot CloneLoot(RecentCompanionLoot source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new RecentCompanionLoot
        {
            QualifiedItemId = source.QualifiedItemId,
            DisplayName = source.DisplayName,
            Stack = source.Stack,
            SourceKey = source.SourceKey,
            AddedAtUtcTicks = source.AddedAtUtcTicks
        };
    }

    public static DeferredNpcRestoreState CloneRestore(DeferredNpcRestoreState source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DeferredNpcRestoreState
        {
            NpcName = source.NpcName,
            OriginalLocationName = source.OriginalLocationName,
            OriginalTileX = source.OriginalTileX,
            OriginalTileY = source.OriginalTileY,
            HasOriginalPosition = source.HasOriginalPosition,
            OriginalDayIndex = source.OriginalDayIndex,
            OriginalScheduleCaptured = source.OriginalScheduleCaptured,
            OriginalScheduleKey = source.OriginalScheduleKey,
            OriginalPetBehavior = source.OriginalPetBehavior,
            OriginalSpousePatioActivity = source.OriginalSpousePatioActivity,
            OriginalMovementSpeedCaptured = source.OriginalMovementSpeedCaptured,
            OriginalMovementSpeed = source.OriginalMovementSpeed,
            OriginalAddedSpeed = source.OriginalAddedSpeed
        };
    }

    public static CompanionHostRules CloneHostRules(CompanionHostRules source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new CompanionHostRules
        {
            UseSquadInventory = source.UseSquadInventory,
            EnableCompanionProgression = source.EnableCompanionProgression,
            CompanionInventorySlots = source.CompanionInventorySlots,
            CompanionWorkRadius = source.CompanionWorkRadius,
            CompanionWorkReturnDistance = source.CompanionWorkReturnDistance,
            FriendshipRequirement = source.FriendshipRequirement,
            MaxSquadSize = source.MaxSquadSize,
            RecruitAllNpcs = source.RecruitAllNpcs,
            EnableGathering = source.EnableGathering,
            ProtectBeehouseFlowers = source.ProtectBeehouseFlowers,
            HarvestingMode = source.HarvestingMode,
            ForagingMode = source.ForagingMode,
            LumberingMode = source.LumberingMode,
            MiningMode = source.MiningMode,
            WateringMode = source.WateringMode,
            PettingMode = source.PettingMode
        };
    }
}
