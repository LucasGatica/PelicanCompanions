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
    private void UpdateAmbientDialogue()
    {
        if (!this.config.EnableCommunication || this.config.DialogueCooldownSeconds <= 0 || this.IsBlockedGameState(blockForMenu: true))
            return;

        long now = DateTimeOffset.UtcNow.UtcTicks;
        long cooldownTicks = TimeSpan.FromSeconds(this.config.DialogueCooldownSeconds).Ticks;

        foreach (SquadMemberState member in this.members.Values.Where(p => p.Mode == CompanionMode.Following))
        {
            if (now - member.LastDialogueUtcTicks < cooldownTicks)
                continue;

            if (this.random.NextDouble() > 0.35)
                continue;

            NPC? npc = Game1.getCharacterFromName(member.NpcName, mustBeVillager: false, includeEventActors: false);
            Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
            if (npc is null || owner?.currentLocation is null || npc.currentLocation != owner.currentLocation)
                continue;

            if (this.Say(npc, "Idle", force: false))
                member.LastDialogueUtcTicks = now;
        }
    }

    private bool Say(NPC npc, string category, bool force)
    {
        if (!force && !this.config.EnableCommunication)
            return false;

        Farmer owner = this.members.TryGetValue(npc.Name, out SquadMemberState? member)
            ? this.GetOwnerFarmer(member.OwnerId) ?? Game1.player
            : Game1.player;
        string? key = this.PickDialogueKey(npc, category, owner);
        if (string.IsNullOrWhiteSpace(key))
            key = $"dialogue.{category}.generic";

        string line = this.Tr(key, new
        {
            npc = npc.displayName,
            player = owner.displayName,
            hearts = this.GetFriendshipHearts(npc, owner)
        });

        if (string.IsNullOrWhiteSpace(line) || line == key)
            return false;

        npc.showTextAboveHead(line);
        return true;
    }

    private string? PickDialogueKey(NPC npc, string category, Farmer owner)
    {
        List<CompanionDialogueLine> candidates = new();
        foreach (string profileKey in this.GetProfileKeys(npc))
        {
            if (this.npcProfiles.TryGetValue(profileKey, out NpcCompanionProfile? profile)
                && profile?.Dialogue is not null
                && profile.Dialogue.TryGetValue(category, out List<CompanionDialogueLine>? lines)
                && lines is not null)
            {
                candidates.AddRange(lines
                    .Where(line => line is not null)
                    .Where(line => this.ConditionMatches(npc, line.Condition, owner)));
            }
        }

        return candidates.Count == 0 ? null : candidates[this.random.Next(candidates.Count)].TextKey;
    }

    private IEnumerable<string> GetProfileKeys(NPC npc)
    {
        yield return "Generic";

        if (npc is Pet)
        {
            string name = npc.Name;
            if (name.Contains("Cat", StringComparison.OrdinalIgnoreCase))
                yield return "All_Cat";
            else if (name.Contains("Dog", StringComparison.OrdinalIgnoreCase))
                yield return "All_Dog";
            else if (name.Contains("Turtle", StringComparison.OrdinalIgnoreCase))
                yield return "All_Turtle";
        }

        yield return npc.Name;
    }

    private bool ConditionMatches(NPC npc, string? condition, Farmer owner)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        foreach (string rawToken in condition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool invert = rawToken.StartsWith('!');
            string token = invert ? rawToken[1..] : rawToken;
            bool result = token switch
            {
                "spouse" => owner.spouse == npc.Name,
                "night" => Game1.timeOfDay >= 1800,
                "farm" => owner.currentLocation?.Name == "Farm",
                "mine" => owner.currentLocation is StardewValley.Locations.MineShaft,
                "volcano" => owner.currentLocation is StardewValley.Locations.VolcanoDungeon,
                "pet" => npc is Pet,
                "villager" => npc.IsVillager,
                _ when token.StartsWith("hearts>=", StringComparison.OrdinalIgnoreCase)
                    => this.GetFriendshipHearts(npc, owner) >= this.ParseTrailingInt(token),
                _ when token.StartsWith("hearts<", StringComparison.OrdinalIgnoreCase)
                    => this.GetFriendshipHearts(npc, owner) < this.ParseTrailingInt(token),
                _ => false
            };

            if (invert)
                result = !result;

            if (!result)
                return false;
        }

        return true;
    }

    private void UpdateDisconnectTimeouts()
    {
        if (!Context.IsMainPlayer || this.IsBlockedGameState(blockForMenu: false))
            return;

        this.ProcessDeferredNpcRestores();

        if (this.config.ParkTimeoutMinutes <= 0)
            return;

        long now = DateTimeOffset.UtcNow.UtcTicks;
        long timeoutTicks = TimeSpan.FromMinutes(this.config.ParkTimeoutMinutes).Ticks;
        foreach (SquadMemberState member in this.members.Values.Where(p => p.Mode == CompanionMode.ParkedForDisconnect).ToList())
        {
            // Revalidate connectivity at the destructive commit point. The
            // owner may have rejoined between timeout scans and follower ticks.
            if (this.GetOwnerFarmer(member.OwnerId) is null
                && member.ParkedAtUtcTicks > 0
                && now - member.ParkedAtUtcTicks >= timeoutTicks)
            {
                this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
            }
        }
    }

    private void ProcessDeferredNpcRestores()
    {
        if (!Context.IsMainPlayer
            || this.deferredNpcRestores.Count == 0
            || this.IsBlockedGameState(blockForMenu: false))
        {
            return;
        }

        foreach ((string npcName, DeferredNpcRestoreState restore) in this.deferredNpcRestores.ToList())
        {
            if (this.members.ContainsKey(npcName))
            {
                this.Monitor.Log($"Discarded stale deferred schedule restore for active companion '{npcName}'.", LogLevel.Warn);
                this.deferredNpcRestores.Remove(npcName);
                this.MarkStateDirty();
                continue;
            }

            NPC? npc = this.GetNpcByName(npcName);
            if (npc is null)
                continue;

            try
            {
                npc.controller = null;
                ResetCompanionMovementSpeed(npc);
                npc.Halt();
                this.RestoreNpcSchedule(npc, restore);
                this.deferredNpcRestores.Remove(npcName);
                this.MarkStateDirty();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Deferred schedule restore failed for '{npcName}' and will be retried: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    private NPC? FindTargetNpc(ICursorPosition cursor)
    {
        GameLocation location = Game1.currentLocation;
        Vector2 cursorTile = cursor.GrabTile;
        Vector2 facingTile = this.GetFacingTile(Game1.player);
        Vector2 playerTile = Game1.player.Tile;

        return location.characters
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !p.IsInvisible)
            .Select(p => new
            {
                Npc = p,
                Distance = Math.Min(Vector2.Distance(p.Tile, cursorTile), Math.Min(Vector2.Distance(p.Tile, facingTile), Vector2.Distance(p.Tile, playerTile)))
            })
            .Where(p => p.Distance <= 2.25f)
            .OrderBy(p => p.Distance)
            .Select(p => p.Npc)
            .FirstOrDefault();
    }

    private Vector2 GetFacingTile(Farmer player)
    {
        Vector2 tile = player.Tile;
        return player.FacingDirection switch
        {
            0 => tile + new Vector2(0, -1),
            1 => tile + new Vector2(1, 0),
            2 => tile + new Vector2(0, 1),
            3 => tile + new Vector2(-1, 0),
            _ => tile
        };
    }

    private bool IsSupportedTarget(NPC npc, Farmer requester)
    {
        if (npc is Pet)
            return true;

        if (this.config.RecruitAllNpcs)
            return !npc.IsMonster && !npc.IsInvisible;

        return npc.CanSocialize || requester.friendshipData.ContainsKey(npc.Name) || this.npcProfiles.ContainsKey(npc.Name);
    }

    private bool MeetsFriendshipRequirement(NPC npc, Farmer requester)
    {
        if (npc is Pet || requester.spouse == npc.Name)
            return true;

        return this.GetFriendshipHearts(npc, requester) >= this.config.FriendshipRequirement;
    }

    private int GetFriendshipHearts(NPC npc)
    {
        return this.GetFriendshipHearts(npc, Game1.player);
    }

    private int GetFriendshipHearts(NPC npc, Farmer player)
    {
        return player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship)
            ? friendship.Points / 250
            : 0;
    }

    private bool CanOwnerMutate(SquadMemberState member, long ownerId, bool showWarning = true)
    {
        if (member.OwnerId == ownerId)
            return true;

        if (showWarning && ownerId == Game1.player.UniqueMultiplayerID)
            this.Warn("recruitment.not_owner", new { npc = member.DisplayName });
        return false;
    }

    private int GetOwnerSlot(SquadMemberState member)
    {
        return this.members.Values
            .Where(p => p.OwnerId == member.OwnerId)
            .OrderBy(p => p.NpcName, StringComparer.OrdinalIgnoreCase)
            .TakeWhile(p => p.NpcName != member.NpcName)
            .Count();
    }

    private SquadMemberState? GetAvailableMember(long ownerId)
    {
        return this.members.Values.FirstOrDefault(p => p.OwnerId == ownerId
            && p.Mode == CompanionMode.Following
            && !this.pendingTasks.ContainsKey(p.NpcName)
            && !this.activeRecallTargets.ContainsKey(p.NpcName));
    }

    private Farmer? GetOwnerFarmer(long ownerId)
    {
        if (Game1.player.UniqueMultiplayerID == ownerId)
            return Game1.player;

        return Game1.getOnlineFarmers().FirstOrDefault(p => p.UniqueMultiplayerID == ownerId);
    }

    private bool IsBlockedGameState(bool blockForMenu)
    {
        return !Context.IsWorldReady
            || Game1.currentLocation is null
            || Game1.eventUp
            || Game1.CurrentEvent is not null
            || Game1.fadeToBlack
            || Game1.currentMinigame is not null
            || Game1.isFestival()
            || (blockForMenu && Game1.activeClickableMenu is not null);
    }

    private bool AreTaskActionsSafe()
    {
        return !this.IsBlockedGameState(blockForMenu: true);
    }

    private void MaintainCompanionScheduleLocks(bool stopCurrentRoutes)
    {
        if (!Context.IsWorldReady)
            return;

        foreach (SquadMemberState member in this.members.Values)
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
                this.DisableNpcSchedule(npc, stopCurrentRoutes);
        }
    }

    private void DisableNpcSchedule(NPC npc, bool stopCurrentRoute)
    {
        npc.ignoreScheduleToday = true;
        if (!stopCurrentRoute)
            return;

        npc.ClearSchedule();
        npc.DirectionsToNewLocation = null;
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
    }

    private void RestoreNpcSchedule(NPC npc, SquadMemberState member)
    {
        npc.ignoreScheduleToday = false;
        npc.DirectionsToNewLocation = null;
        npc.ClearSchedule();

        // Put the NPC back at the position where this mod acquired control
        // before asking vanilla to resume today's schedule. Older saves don't
        // have this marker and safely retain the legacy behavior.
        bool originalPositionStillRelevant = member.OriginalDayIndex < 0 || member.OriginalDayIndex == Game1.Date.TotalDays;
        if (member.HasOriginalPosition
            && originalPositionStillRelevant
            && !string.IsNullOrWhiteSpace(member.OriginalLocationName))
        {
            GameLocation? originalLocation = Game1.getLocationFromName(member.OriginalLocationName);
            if (originalLocation is not null)
            {
                Vector2 originalTile = NormalizeTile(new Vector2(member.OriginalTileX, member.OriginalTileY));
                this.PlaceNpc(npc, originalLocation, originalTile);
            }
        }

        if (!Context.IsWorldReady || Game1.eventUp || Game1.CurrentEvent is not null || Game1.isFestival())
            return;

        npc.checkSchedule(Game1.timeOfDay);
    }

    private void RestoreNpcSchedule(NPC npc, DeferredNpcRestoreState restore)
    {
        this.RestoreNpcSchedule(
            npc,
            new SquadMemberState
            {
                NpcName = restore.NpcName,
                OriginalLocationName = restore.OriginalLocationName,
                OriginalTileX = restore.OriginalTileX,
                OriginalTileY = restore.OriginalTileY,
                HasOriginalPosition = restore.HasOriginalPosition,
                OriginalDayIndex = restore.OriginalDayIndex
            });
    }

    private void SetCompanionActivity(SquadMemberState member, string activityKey)
    {
        string normalized = string.IsNullOrWhiteSpace(activityKey)
            ? "companion.status.following"
            : activityKey;
        if (member.CurrentActivityKey == normalized)
            return;

        member.CurrentActivityKey = normalized;
        this.MarkStateDirty();
    }

    private void SetCompanionTarget(SquadMemberState member, CompanionTaskKind kind, Vector2 tile)
    {
        string key = kind switch
        {
            CompanionTaskKind.Lumbering => "companion.target.wood",
            CompanionTaskKind.Mining => "companion.target.mining",
            CompanionTaskKind.Watering => "companion.target.watering",
            CompanionTaskKind.Gathering => "companion.target.gathering",
            CompanionTaskKind.Harvesting => "companion.target.harvesting",
            CompanionTaskKind.Petting => "companion.target.petting",
            _ => ""
        };
        int x = (int)tile.X;
        int y = (int)tile.Y;
        if (member.CurrentTargetKey == key && member.CurrentTargetX == x && member.CurrentTargetY == y)
            return;

        member.CurrentTargetKey = key;
        member.CurrentTargetX = x;
        member.CurrentTargetY = y;
        this.MarkStateDirty();
    }

    private void ClearCompanionTarget(SquadMemberState member)
    {
        if (string.IsNullOrEmpty(member.CurrentTargetKey) && member.CurrentTargetX == -1 && member.CurrentTargetY == -1)
            return;

        member.CurrentTargetKey = "";
        member.CurrentTargetX = -1;
        member.CurrentTargetY = -1;
        this.MarkStateDirty();
    }

    private void SetTaskResult(SquadMemberState member, string resultKey)
    {
        member.LastTaskResultKey = resultKey;
        member.LastFailureReasonKey = "";
        this.MarkStateDirty();
    }

    private void SetTaskFailure(SquadMemberState member, string failureKey)
    {
        if (member.LastFailureReasonKey == failureKey)
            return;

        member.LastFailureReasonKey = failureKey;
        this.MarkStateDirty();
    }

    private string GetWorkTargetKey(string locationName, Vector2 tile)
    {
        Vector2 normalized = NormalizeTile(tile);
        return $"{locationName}|{(int)normalized.X}|{(int)normalized.Y}";
    }

    private bool TryReserveWorkTarget(string npcName, string locationName, Vector2 tile)
    {
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workTargetReservations.TryGetValue(key, out string? owner) && !string.Equals(owner, npcName, StringComparison.OrdinalIgnoreCase))
            return false;

        this.workTargetReservations[key] = npcName;
        this.InvalidateTargetPreviews();
        return true;
    }

    private void ReleaseWorkTarget(string locationName, Vector2 tile)
    {
        if (this.workTargetReservations.Remove(this.GetWorkTargetKey(locationName, tile)))
            this.InvalidateTargetPreviews();
    }

    private bool IsStandTileReserved(GameLocation location, Vector2 tile, string npcName)
    {
        string key = this.GetWorkTargetKey(location.NameOrUniqueName, tile);
        bool reservedForWork = this.workStandReservations.TryGetValue(key, out string? reservedBy)
            && !string.Equals(reservedBy, npcName, StringComparison.OrdinalIgnoreCase);
        return reservedForWork || this.followDestinationsThisUpdate.Contains(key);
    }

    private bool TryReserveStandTile(string npcName, string locationName, Vector2 tile)
    {
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workStandReservations.TryGetValue(key, out string? reservedBy)
            && !string.Equals(reservedBy, npcName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        this.workStandReservations[key] = npcName;
        return true;
    }

    private void ReleaseStandTile(string npcName, string locationName, Vector2 tile)
    {
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workStandReservations.TryGetValue(key, out string? reservedBy)
            && string.Equals(reservedBy, npcName, StringComparison.OrdinalIgnoreCase))
        {
            this.workStandReservations.Remove(key);
        }
    }

    private void ReleaseWorkTargetsForNpc(string npcName)
    {
        bool removedAny = false;
        foreach (KeyValuePair<string, string> reservation in this.workTargetReservations.ToList())
        {
            if (string.Equals(reservation.Value, npcName, StringComparison.OrdinalIgnoreCase))
            {
                this.workTargetReservations.Remove(reservation.Key);
                removedAny = true;
            }
        }

        foreach (KeyValuePair<string, string> reservation in this.workStandReservations.ToList())
        {
            if (string.Equals(reservation.Value, npcName, StringComparison.OrdinalIgnoreCase))
                this.workStandReservations.Remove(reservation.Key);
        }

        if (removedAny)
            this.InvalidateTargetPreviews();
    }

    private void RemovePendingTask(string npcName)
    {
        if (!this.pendingTasks.TryGetValue(npcName, out PendingCompanionTask? task))
            return;

        this.RemovePendingTask(task);
    }

    private void RemovePendingTask(PendingCompanionTask task, string? failureKey = null, bool returning = false)
    {
        this.pendingTasks.Remove(task.NpcName);
        this.ReleaseWorkTarget(task.LocationName, task.TargetTile);
        this.ReleaseStandTile(task.NpcName, task.LocationName, task.StandTile);
        if (this.members.TryGetValue(task.NpcName, out SquadMemberState? member))
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
            {
                try
                {
                    npc.controller = null;
                    ResetCompanionMovementSpeed(npc);
                    npc.Halt();
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed stopping companion '{member.NpcName}' while cleaning up a task: {ex.Message}", LogLevel.Warn);
                }
            }

            this.ClearCompanionTarget(member);
            if (!string.IsNullOrWhiteSpace(failureKey))
                this.SetTaskFailure(member, failureKey);

            this.SetCompanionActivity(member, returning ? "companion.status.returning" : "companion.status.following");
            if (returning)
            {
                if (npc?.currentLocation is not null)
                    this.ShowCompanionWorkSignal(npc, npc.currentLocation, npc.Tile, "return");
            }
        }
    }
}
