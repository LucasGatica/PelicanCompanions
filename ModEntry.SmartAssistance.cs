using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private SmartWaterRefillMode GetEffectiveSmartWaterRefillMode()
    {
        return this.replicatedHostRules?.SmartWaterRefill ?? this.config.SmartWaterRefill;
    }

    private int GetEffectiveSmartWaterRefillSearchRadius()
    {
        return Math.Clamp(
            this.replicatedHostRules?.SmartWaterRefillSearchRadius
                ?? this.config.SmartWaterRefillSearchRadius,
            3,
            40);
    }

    private bool IsSmartToolSwapEnabled()
    {
        return this.replicatedHostRules?.EnableSmartToolSwap
            ?? this.config.EnableSmartToolSwap;
    }

    /// <summary>
    /// Equip a missing compatible tool from the owner's inventory. A usable
    /// equipped tool is never replaced automatically, which prevents tools
    /// bouncing between companions during consecutive planning passes.
    /// </summary>
    private bool TryEnsureSmartToolForTask(
        SquadMemberState member,
        CompanionTaskKind kind)
    {
        if (this.HasUsableCompanionToolForTask(member, kind))
            return true;
        if (!Context.IsMainPlayer || !this.IsSmartToolSwapEnabled())
            return false;

        if (!TryGetEquipmentSlotForTask(kind, out CompanionEquipmentSlot slot))
            return false;

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.UsingTool)
            return false;

        // An empty watering can should use the refill workflow. Only replace it
        // when smart refill is unavailable and the player carries a filled can.
        bool hasEmptyCan = kind == CompanionTaskKind.Watering
            && this.HasEmptyCompanionWateringCan(member);
        if (hasEmptyCan && this.HasAvailableSmartWaterRefill(member))
            return false;

        int bestIndex = -1;
        int bestRank = int.MinValue;
        string bestToken = "";
        for (int index = 0; index < owner.Items.Count; index++)
        {
            if (owner.Items[index] is not Tool tool
                || !IsToolForSlot(tool, slot)
                || tool.enchantments.Count > 0
                || tool.previousEnchantments.Count > 0
                || tool is FishingRod rod && rod.attachments.Any(item => item is not null)
                || !this.TryPrepareToolForEquipment(
                    tool,
                    slot,
                    member.OwnerId,
                    out SavedItemStack prepared))
            {
                continue;
            }

            int rank = tool.UpgradeLevel;
            if (tool is WateringCan can)
            {
                if (can.WaterLeft <= 0)
                    continue;
                rank += 1000 + can.WaterLeft;
            }

            if (rank <= bestRank)
                continue;

            bestRank = rank;
            bestIndex = index;
            bestToken = SavedItemStackIdentity.CreateToken(prepared);
        }

        if (bestIndex < 0)
            return false;

        CompanionOperationalProfileState profile =
            this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName);
        bool changed = this.SetCompanionEquipment(
            member,
            slot,
            bestIndex,
            member.OwnerId,
            bestToken,
            GetEquipmentStateToken(GetEquipmentSlot(profile.Equipment, slot)));
        return changed && this.HasUsableCompanionToolForTask(member, kind);
    }

    /// <summary>
    /// Check the same location, range, reservation, and path prerequisites used
    /// by the refill command without reserving anything. This lets a companion
    /// prefer a real refill trip but fall back to a filled player-owned can when
    /// the configured source is not currently usable.
    /// </summary>
    private bool HasAvailableSmartWaterRefill(SquadMemberState member)
    {
        if (this.GetEffectiveSmartWaterRefillMode() == SmartWaterRefillMode.Disabled
            || this.pendingTasks.ContainsKey(member.NpcName)
            || this.activeRecallTargets.ContainsKey(member.NpcName)
            || member.Mode != CompanionMode.Following
            || !this.TryGetEquippedTool(
                member,
                CompanionEquipmentSlot.WateringCan,
                out WateringCan wateringCan)
            || wateringCan.WaterLeft > 0)
        {
            return false;
        }

        NPC? npc = this.GetNpcByName(member.NpcName);
        GameLocation? location = npc?.currentLocation;
        if (npc is null
            || location is null
            || !CompanionSmartRefillPolicy.AllowsLocation(
                this.GetEffectiveSmartWaterRefillMode(),
                location is Farm,
                location.IsOutdoors))
        {
            return false;
        }

        return this.TryFindNearestRefillTarget(
            member,
            npc,
            location,
            NormalizeTile(npc.Tile),
            this.GetEffectiveSmartWaterRefillSearchRadius(),
            out _,
            out _);
    }

    private static bool TryGetEquipmentSlotForTask(
        CompanionTaskKind kind,
        out CompanionEquipmentSlot slot)
    {
        slot = kind switch
        {
            CompanionTaskKind.Lumbering => CompanionEquipmentSlot.Axe,
            CompanionTaskKind.Mining => CompanionEquipmentSlot.Pickaxe,
            CompanionTaskKind.Watering => CompanionEquipmentSlot.WateringCan,
            CompanionTaskKind.Fishing => CompanionEquipmentSlot.FishingRod,
            _ => (CompanionEquipmentSlot)(-1)
        };
        return Enum.IsDefined(slot);
    }

    /// <summary>Queue a host-authoritative trip to a canonical refill tile.</summary>
    private bool TryQueueSmartWaterRefill(
        SquadMemberState member,
        GameLocation location,
        Vector2? resumeWateringTarget = null,
        bool manual = false)
    {
        if (!Context.IsMainPlayer
            || this.pendingTasks.ContainsKey(member.NpcName)
            || this.activeRecallTargets.ContainsKey(member.NpcName)
            || member.Mode != CompanionMode.Following)
        {
            return false;
        }

        if (!this.TryGetEquippedTool(
                member,
                CompanionEquipmentSlot.WateringCan,
                out WateringCan wateringCan)
            || wateringCan.WaterLeft > 0)
        {
            return false;
        }

        SmartWaterRefillMode mode = this.GetEffectiveSmartWaterRefillMode();
        bool isFarm = location is Farm;
        bool isOutdoors = location.IsOutdoors;
        if (!CompanionSmartRefillPolicy.AllowsLocation(mode, isFarm, isOutdoors))
            return false;

        NPC? npc = this.GetNpcByName(member.NpcName);
        if (npc?.currentLocation != location)
            return false;

        int radius = this.GetEffectiveSmartWaterRefillSearchRadius();
        Vector2 npcTile = NormalizeTile(npc.Tile);
        if (!this.TryFindNearestRefillTarget(
                member,
                npc,
                location,
                npcTile,
                radius,
                out Vector2 waterTile,
                out Vector2 standTile))
        {
            return false;
        }

        if (!this.TryReserveWorkTarget(
                member.NpcName,
                location.NameOrUniqueName,
                waterTile))
        {
            return false;
        }

        if (!this.TryReserveStandTile(
                member.NpcName,
                location.NameOrUniqueName,
                standTile))
        {
            this.ReleaseWorkTarget(
                member.NpcName,
                location.NameOrUniqueName,
                waterTile);
            return false;
        }

        PendingCompanionTask task = new()
        {
            Kind = CompanionTaskKind.RefillingWater,
            NpcName = member.NpcName,
            LocationName = location.NameOrUniqueName,
            TargetTile = waterTile,
            Manual = manual,
            UsesWorkDirective = this.HasActiveWorkDirective(member),
            IgnoresTaskMode = true,
            WorkRadius = radius,
            ReturnDistance = Math.Max(radius, this.GetCompanionReturnDistance(member)),
            StartedTick = Game1.ticks,
            LastProcessedTick = Game1.ticks,
            StandTile = standTile,
            HasResumeWateringTarget = resumeWateringTarget.HasValue,
            ResumeWateringTargetTile =
                NormalizeTile(resumeWateringTarget ?? Vector2.Zero)
        };

        this.pendingTasks[member.NpcName] = task;
        this.SetCompanionActivity(member, "companion.status.refilling_water");
        this.SetCompanionTarget(member, CompanionTaskKind.RefillingWater, waterTile);
        this.ShowCompanionWorkSignal(npc, location, waterTile, "water");
        return true;
    }

    private bool TryFindNearestRefillTarget(
        SquadMemberState member,
        NPC npc,
        GameLocation location,
        Vector2 center,
        int radius,
        out Vector2 waterTile,
        out Vector2 standTile)
    {
        waterTile = Vector2.Zero;
        standTile = Vector2.Zero;
        if (location.Map is null || location.Map.Layers.Count == 0)
            return false;

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        int centerX = (int)center.X;
        int centerY = (int)center.Y;
        IEnumerable<Vector2> candidates =
            from x in Enumerable.Range(
                Math.Max(0, centerX - radius),
                Math.Max(0, Math.Min(width - 1, centerX + radius)
                    - Math.Max(0, centerX - radius) + 1))
            from y in Enumerable.Range(
                Math.Max(0, centerY - radius),
                Math.Max(0, Math.Min(height - 1, centerY + radius)
                    - Math.Max(0, centerY - radius) + 1))
            let tile = new Vector2(x, y)
            where Vector2.Distance(center, tile) <= radius
            orderby Vector2.DistanceSquared(center, tile), y, x
            select tile;

        foreach (Vector2 candidate in candidates)
        {
            bool refillable;
            try
            {
                refillable = location.CanRefillWateringCanOnTile(
                    (int)candidate.X,
                    (int)candidate.Y);
            }
            catch
            {
                continue;
            }

            if (!refillable
                || this.IsTargetReserved(location, candidate)
                || !this.TryFindSafeAdjacentTile(
                    location,
                    candidate,
                    npc,
                    member,
                    radius,
                    out Vector2 candidateStand,
                    ownerAnchorOverride: center))
            {
                continue;
            }

            waterTile = candidate;
            standTile = candidateStand;
            return true;
        }

        return false;
    }

    private void ProcessPendingWaterRefillTask(PendingCompanionTask task)
    {
        if (!this.members.TryGetValue(task.NpcName, out SquadMemberState? member)
            || member.Mode != CompanionMode.Following)
        {
            this.RemovePendingTask(task);
            return;
        }

        if (task.InactiveTicks > InstantTaskTimeoutTicks * 3)
        {
            this.RemovePendingTask(
                task,
                "companion.task_failure.refill_unavailable",
                returning: true);
            if (task.Manual)
                this.WarnForPlayer(member.OwnerId, "tasks.refill_unavailable");
            return;
        }

        GameLocation? location = Game1.getLocationFromName(task.LocationName);
        NPC? npc = this.GetNpcByName(task.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (location is null
            || npc?.currentLocation != location
            || owner is null
            || !CompanionSmartRefillPolicy.AllowsLocation(
                this.GetEffectiveSmartWaterRefillMode(),
                location is Farm,
                location.IsOutdoors))
        {
            this.RemovePendingTask(
                task,
                "companion.task_failure.refill_unavailable",
                returning: true);
            return;
        }

        Vector2 waterTile = NormalizeTile(task.TargetTile);
        bool sourceValid;
        try
        {
            sourceValid = location.CanRefillWateringCanOnTile(
                (int)waterTile.X,
                (int)waterTile.Y);
        }
        catch
        {
            sourceValid = false;
        }

        if (!sourceValid)
        {
            this.RemovePendingTask(
                task,
                "companion.task_failure.refill_unavailable",
                returning: true);
            return;
        }

        int navigationRange = Math.Max(
            task.WorkRadius,
            (int)Math.Ceiling(Vector2.Distance(owner.Tile, npc.Tile))
                + task.WorkRadius + 2);
        if (!this.TryResolveTaskStandTile(
                location,
                waterTile,
                npc,
                member,
                task,
                navigationRange,
                out Vector2 standTile))
        {
            this.RemovePendingTask(
                task,
                "companion.task_failure.no_safe_tile",
                returning: true);
            return;
        }

        if (!this.IsNpcAtTaskTile(npc, standTile))
        {
            this.RouteNpcToTaskTile(
                npc,
                location,
                standTile,
                task,
                force: false);
            return;
        }

        this.StopCompanionMovement(npc);
        this.FaceTile(npc, waterTile);
        if (!this.TryRefillCompanionWateringCan(member, out string failureKey))
        {
            this.RemovePendingTask(task, failureKey, returning: true);
            return;
        }

        this.ShowCompanionWorkSignal(npc, location, waterTile, "water");
        this.SetTaskResult(member, "companion.task_result.refilled_water");
        npc.doEmote(20);
        location.localSound("slosh", npc.Tile);

        bool resume = task.HasResumeWateringTarget;
        Vector2 resumeTile = task.ResumeWateringTargetTile;
        bool manual = task.Manual;
        this.RemovePendingTask(task);
        if (resume
            && this.IsValidWateringTarget(location, resumeTile)
            && this.TryWaterTile(location, resumeTile, member, manual))
        {
            return;
        }

        if (this.HasActiveWorkDirective(member))
            this.priorityTaskPlanningMembers.Add(member.NpcName);
    }

    private bool TryRefillCompanionWateringCan(
        SquadMemberState member,
        out string failureKey)
    {
        failureKey = "companion.task_failure.need_watering_can";
        CompanionOperationalProfileState profile =
            this.GetOrCreateOperationalProfile(member.OwnerId, member.NpcName);
        SavedItemStack? current =
            GetEquipmentSlot(profile.Equipment, CompanionEquipmentSlot.WateringCan);
        if (current is null
            || this.TryCreateItem(current) is not WateringCan wateringCan)
        {
            return false;
        }

        if (!CompanionEquipmentPolicy.IsValidWateringCanState(
                wateringCan.UpgradeLevel,
                wateringCan.WaterLeft))
        {
            throw new InvalidDataException(
                $"Companion '{member.NpcName}' has incoherent watering-can state.");
        }

        if (wateringCan.WaterLeft >= wateringCan.waterCanMax)
        {
            failureKey = "";
            return true;
        }

        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null)
        {
            failureKey = "companion.task_failure.owner_unavailable";
            return false;
        }

        SavedItemStack previous = CompanionStateCopy.CloneItem(current);
        EquipmentJournalSnapshot journalBefore = CaptureEquipmentJournal(owner);
        wateringCan.WaterLeft = wateringCan.waterCanMax;
        SavedItemStack updated = this.ToSavedItem(wateringCan)
            ?? throw new InvalidOperationException(
                "The equipped watering can couldn't be serialized after refill.");

        try
        {
            SetEquipmentSlot(
                profile.Equipment,
                CompanionEquipmentSlot.WateringCan,
                updated);
            this.WriteOwnerEquipmentJournal(owner);
        }
        catch
        {
            SetEquipmentSlot(
                profile.Equipment,
                CompanionEquipmentSlot.WateringCan,
                previous);
            RestoreEquipmentJournal(owner, journalBefore);
            throw;
        }

        this.MarkStateDirty();
        failureKey = "";
        return true;
    }
}
