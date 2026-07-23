using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    /// <summary>Build the display-only, snapshot-safe row consumed by the team dashboard.</summary>
    private CompanionTeamMemberView GetCompanionTeamMemberView(SquadMemberState member)
    {
        ArgumentNullException.ThrowIfNull(member);

        NPC? npc = this.GetNpcByName(member.NpcName);
        string location = npc?.currentLocation?.NameOrUniqueName
            ?? member.WaitingLocationName
            ?? (!string.IsNullOrWhiteSpace(member.WorkAreaLocationName)
                ? member.WorkAreaLocationName
                : member.OriginalLocationName)
            ?? this.Tr("companion.map.unavailable");

        string activity = this.GetCompanionStatusText(member);
        if (!string.IsNullOrWhiteSpace(member.CurrentTargetKey))
        {
            activity = $"{activity} · {this.Tr(member.CurrentTargetKey)}";
        }

        CompanionEquipmentSlot? relevantSlot = GetTeamDashboardEquipmentSlot(member);
        Item? relevantTool = relevantSlot is CompanionEquipmentSlot slot
            ? this.GetCompanionEquipmentItem(member, slot)
            : null;
        string tool = relevantTool?.DisplayName
            ?? (relevantSlot.HasValue
                ? this.Tr("companion.equipment.empty")
                : "—");

        Item? wateringEquipment = this.GetCompanionEquipmentItem(
            member,
            CompanionEquipmentSlot.WateringCan);
        string water = wateringEquipment is WateringCan wateringCan
            ? this.Tr("companion.equipment.water", new
            {
                current = wateringCan.WaterLeft,
                capacity = CompanionEquipmentPolicy.GetWateringCanCapacity(wateringCan.UpgradeLevel)
            })
            : "—";

        int capacity = this.GetCompanionInventoryCapacity();
        int count = member.Inventory.Count;
        bool inventoryFull = count >= capacity;
        bool hasCurrentFailure = !string.IsNullOrWhiteSpace(member.LastFailureReasonKey)
            && member.LastFailureReasonKey is not
                "companion.task_failure.recalled"
                and not "companion.task_failure.inventory_full_world_drop";
        string blockReason = this.IsBlockedGameState(blockForMenu: false)
            ? this.Tr("companion.status.blocked")
            : hasCurrentFailure
                ? this.Tr(member.LastFailureReasonKey)
                : "";

        return new CompanionTeamMemberView(
            location,
            activity,
            tool,
            water,
            count,
            capacity,
            inventoryFull,
            blockReason);
    }

    /// <summary>
    /// Pause every owned companion through the same waiting transition used by
    /// the individual panel action. Directives, areas, routines, and cargo
    /// filters remain configured for a later explicit resume.
    /// </summary>
    private bool StopAllCompanionWork(long ownerId)
    {
        if (!Context.IsMainPlayer)
            return false;

        List<SquadMemberState> owned = this.GetTeamDashboardMembers(ownerId);
        int stopped = 0;
        foreach (SquadMemberState member in owned)
        {
            if (member.Mode == CompanionMode.Waiting
                && member.RoutinePausedByPlayer)
                continue;

            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc?.currentLocation is null)
                continue;

            this.SetWaiting(member.NpcName, ownerId, showMessage: false);
            if (member.Mode == CompanionMode.Waiting)
                stopped++;
        }

        if (this.ShouldShowFeedbackFor(ownerId))
        {
            if (stopped > 0)
            {
                this.InfoForPlayer(
                    ownerId,
                    stopped == 1
                        ? "companion.team.stop_all_complete_one"
                        : "companion.team.stop_all_complete",
                    new { count = stopped });
            }
            else
            {
                this.InfoForPlayer(ownerId, "companion.team.stop_all_unchanged");
            }
        }

        return stopped > 0;
    }

    /// <summary>
    /// Deposit all eligible cargo using each member's effective chest and
    /// persisted per-owner/NPC filters.
    /// </summary>
    private bool DepositAllCompanionCargo(long ownerId)
    {
        if (!Context.IsMainPlayer)
            return false;

        List<SquadMemberState> owned = this.GetTeamDashboardMembers(ownerId);
        int attempted = 0;
        int completed = 0;
        int scheduled = 0;
        foreach (SquadMemberState member in owned)
        {
            // HasCompanionDepositCargo and the deposit routine both apply
            // ShouldDepositCompanionItem, so wood/mineral/food filters are
            // preserved for the bulk command exactly as they are for routines.
            if (!this.HasCompanionDepositCargo(member))
                continue;

            attempted++;
            if (this.TryDepositCompanionInventoryToAssignedChest(member, showFeedback: false))
                completed++;
            else if (this.IsCompanionChestDepositPending(member))
                scheduled++;
        }

        if (this.ShouldShowFeedbackFor(ownerId))
        {
            if (attempted == 0)
            {
                this.InfoForPlayer(ownerId, "companion.team.deposit_all_empty");
            }
            else if (completed == attempted)
            {
                this.InfoForPlayer(
                    ownerId,
                    completed == 1
                        ? "companion.team.deposit_all_complete_one"
                        : "companion.team.deposit_all_complete",
                    new { count = completed });
            }
            else if (completed + scheduled == attempted)
            {
                this.InfoForPlayer(
                    ownerId,
                    scheduled == 1
                        ? "companion.team.deposit_all_started_one"
                        : "companion.team.deposit_all_started",
                    new { count = scheduled });
            }
            else
            {
                this.WarnForPlayer(
                    ownerId,
                    "companion.team.deposit_all_partial",
                    new
                    {
                        complete = completed,
                        total = attempted
                    });
            }
        }

        return attempted > 0 && completed + scheduled == attempted;
    }

    /// <summary>Resume each owned companion's own saved routine without copying or overwriting it.</summary>
    private bool FollowAllCompanionRoutines(long ownerId)
    {
        if (!Context.IsMainPlayer)
            return false;

        List<SquadMemberState> owned = this.GetTeamDashboardMembers(ownerId);
        int resumed = 0;
        foreach (SquadMemberState member in owned)
        {
            if (this.TryFollowCompanionRoutine(member, ownerId, showMessage: false))
                resumed++;
        }

        if (this.ShouldShowFeedbackFor(ownerId))
        {
            if (resumed > 0)
            {
                this.InfoForPlayer(
                    ownerId,
                    resumed == 1
                        ? "companion.team.follow_all_complete_one"
                        : "companion.team.follow_all_complete",
                    new { count = resumed });
            }
            else
            {
                this.WarnForPlayer(ownerId, "companion.team.follow_all_unavailable");
            }
        }

        return resumed > 0;
    }

    /// <summary>Panel wrapper: run locally on the host or send one owner-scoped request.</summary>
    private bool RequestStopAllCompanionWork()
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("StopAllCompanionWork");
            return true;
        }

        return this.StopAllCompanionWork(ownerId);
    }

    /// <summary>Panel wrapper: run locally on the host or send one owner-scoped request.</summary>
    private bool RequestDepositAllCompanionCargo()
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("DepositAllCompanionCargo");
            return true;
        }

        return this.DepositAllCompanionCargo(ownerId);
    }

    /// <summary>Panel wrapper: run locally on the host or send one owner-scoped request.</summary>
    private bool RequestFollowAllCompanionRoutines()
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("FollowAllCompanionRoutines");
            return true;
        }

        return this.FollowAllCompanionRoutines(ownerId);
    }

    private List<SquadMemberState> GetTeamDashboardMembers(long ownerId)
    {
        return this.members.Values
            .Where(member => member.OwnerId == ownerId)
            .OrderBy(member => member.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CompanionEquipmentSlot? GetTeamDashboardEquipmentSlot(SquadMemberState member)
    {
        if (member.CurrentActivityKey is "companion.status.moving_to_fish"
            or "companion.status.fishing"
            || member.CurrentTargetKey == "companion.target.fishing")
        {
            return CompanionEquipmentSlot.FishingRod;
        }

        return member.CurrentTargetKey switch
        {
            "companion.target.wood" => CompanionEquipmentSlot.Axe,
            "companion.target.mining" => CompanionEquipmentSlot.Pickaxe,
            "companion.target.watering" => CompanionEquipmentSlot.WateringCan,
            "companion.target.water_source" => CompanionEquipmentSlot.WateringCan,
            _ => member.PreferredWorkSpecialty switch
            {
                CompanionWorkSpecialty.Wood => CompanionEquipmentSlot.Axe,
                CompanionWorkSpecialty.Mining => CompanionEquipmentSlot.Pickaxe,
                CompanionWorkSpecialty.Watering => CompanionEquipmentSlot.WateringCan,
                _ => null
            }
        };
    }
}
