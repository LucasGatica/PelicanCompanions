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
        if ((!this.config.EnableCommunication && !this.config.EnablePetExpressions)
            || this.config.DialogueCooldownSeconds <= 0
            || this.IsBlockedGameState(blockForMenu: true))
            return;

        int intervalTicks = Math.Max(DialogueTicksPerSecond, this.config.DialogueCooldownSeconds * DialogueTicksPerSecond);
        foreach (IGrouping<long, SquadMemberState> group in this.members.Values
            .Where(member => member.Mode == CompanionMode.Following)
            .GroupBy(member => member.OwnerId))
        {
            long ownerId = group.Key;
            if (!this.dialogueScheduler.CanScheduleAmbient(ownerId, Game1.ticks, intervalTicks))
                continue;

            this.dialogueScheduler.MarkAmbientAttempt(ownerId, Game1.ticks);
            Farmer? owner = this.GetOwnerFarmer(ownerId);
            if (owner?.currentLocation is null)
                continue;

            List<NPC> eligible = group
                .Where(member => !this.pendingTasks.ContainsKey(member.NpcName)
                    && !this.activeRecallTargets.ContainsKey(member.NpcName))
                .Select(member => this.GetNpcByName(member.NpcName))
                .Where(npc => npc is not null
                    && npc.currentLocation == owner.currentLocation
                    && (npc is Pet ? this.config.EnablePetExpressions : this.config.EnableCommunication))
                .Cast<NPC>()
                .ToList();
            if (eligible.Count == 0)
                continue;

            string? lastSpeaker = this.dialogueScheduler.GetLastSpeaker(ownerId);
            List<NPC> freshSpeakers = eligible
                .Where(npc => !string.Equals(npc.Name, lastSpeaker, StringComparison.OrdinalIgnoreCase))
                .ToList();
            List<NPC> pool = freshSpeakers.Count > 0 ? freshSpeakers : eligible;
            NPC selected = pool[this.random.Next(pool.Count)];
            this.Say(selected, "Idle", force: false, ownerIdOverride: ownerId);
        }
    }

    private bool Say(
        NPC npc,
        string category,
        bool force,
        long? ownerIdOverride = null,
        CompanionDialogueContext? context = null)
    {
        context ??= new CompanionDialogueContext { TaskKind = GetTaskKindForDialogueCategory(category) };
        return this.QueueCompanionCommunication(npc, category, force, ownerIdOverride, context);
    }

    private CompanionDialogueLine? PickDialogueLine(
        NPC npc,
        string category,
        Farmer owner,
        CompanionDialogueContext context,
        IReadOnlyCollection<string> recentKeys)
    {
        List<(CompanionDialogueLine Line, int SourcePriority)>? eligible = null;
        int fallbackSourcePriority = 2;
        foreach (string profileKey in this.GetProfileKeys(npc))
        {
            if (this.npcProfiles.TryGetValue(profileKey, out NpcCompanionProfile? profile)
                && profile?.Dialogue is not null
                && profile.Dialogue.TryGetValue(category, out List<CompanionDialogueLine>? lines)
                && lines is not null)
            {
                CompanionDialogueLine[] matching = lines
                    .Where(line => line is not null)
                    .Where(line => this.ConditionMatches(npc, line.Condition, owner, context))
                    .Where(line => this.dialogueScheduler.CanPresentIdentity(
                        owner.UniqueMultiplayerID,
                        CompanionDialogueSelectionPolicy.GetIdentity(line),
                        Game1.ticks,
                        Math.Clamp(line.MinIntervalSeconds, 0, 3600) * DialogueTicksPerSecond))
                    .ToArray();
                if (matching.Length == 0)
                    continue;

                if (eligible is null)
                {
                    eligible = matching
                        .Select(line => (line, SourcePriority: 0))
                        .ToList();
                    continue;
                }

                // Lower-priority profiles can't replace authored NPC lines,
                // but explicitly marked contextual overlays may enrich an
                // unconditional exact line (for example season or friendship).
                // Normal fallback lines remain available only when every
                // matching primary line is recent, so repetition can be avoided.
                eligible.AddRange(matching
                    .Select(line => (line, SourcePriority: line.Overlay ? 1 : fallbackSourcePriority)));
                fallbackSourcePriority++;
            }
        }

        if (eligible is null || eligible.Count == 0)
            return null;

        List<(CompanionDialogueLine Line, int SourcePriority)> fresh = eligible
            .Where(candidate => !recentKeys.Contains(
                CompanionDialogueSelectionPolicy.GetIdentity(candidate.Line),
                StringComparer.OrdinalIgnoreCase))
            .ToList();
        List<(CompanionDialogueLine Line, int SourcePriority)> pool = fresh.Count > 0
            ? fresh
            : eligible;
        if (fresh.Any(candidate => candidate.SourcePriority == 0))
            pool = pool.Where(candidate => candidate.SourcePriority <= 1).ToList();

        if (pool.Any(candidate => candidate.SourcePriority <= 1))
        {
            // The primary profile and its explicit overlays form one authored
            // tier: a contextual overlay may enrich an unconditional base line.
            pool = pool.Where(candidate => candidate.SourcePriority <= 1).ToList();
            int bestSpecificity = pool.Max(candidate => GetDialogueConditionSpecificity(candidate.Line));
            pool = pool
                .Where(candidate => GetDialogueConditionSpecificity(candidate.Line) == bestSpecificity)
                .ToList();
            int bestSourcePriority = pool.Min(candidate => candidate.SourcePriority);
            pool = pool
                .Where(candidate => candidate.SourcePriority == bestSourcePriority)
                .ToList();
        }
        else
        {
            // Normal fallback profiles are a strict hierarchy. A more generic
            // profile must not leapfrog an earlier source just because its line
            // has more condition clauses.
            int bestSourcePriority = pool.Min(candidate => candidate.SourcePriority);
            pool = pool
                .Where(candidate => candidate.SourcePriority == bestSourcePriority)
                .ToList();
            int bestSpecificity = pool.Max(candidate => GetDialogueConditionSpecificity(candidate.Line));
            pool = pool
                .Where(candidate => GetDialogueConditionSpecificity(candidate.Line) == bestSpecificity)
                .ToList();
        }

        return CompanionDialogueSelectionPolicy.Select(
            pool.Select(candidate => candidate.Line).ToArray(),
            recentKeys,
            this.random.Next());
    }

    private bool HasConfiguredDialogueCategory(NPC npc, string category)
    {
        foreach (string profileKey in this.GetProfileKeys(npc))
        {
            if (this.npcProfiles.TryGetValue(profileKey, out NpcCompanionProfile? profile)
                && profile?.Dialogue is not null
                && profile.Dialogue.ContainsKey(category))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetDialogueConditionSpecificity(CompanionDialogueLine line)
    {
        return string.IsNullOrWhiteSpace(line.Condition)
            ? 0
            : line.Condition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private IEnumerable<string> GetProfileKeys(NPC npc)
    {
        HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);
        if (yielded.Add(npc.Name))
            yield return npc.Name;

        if (npc is Pet pet)
        {
            string petType = pet.petType.Value ?? "";
            string normalizedType = new(petType.Where(char.IsLetterOrDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(normalizedType) && yielded.Add($"All_{normalizedType}"))
                yield return $"All_{normalizedType}";
            if (yielded.Add("All_Pet"))
                yield return "All_Pet";
        }
        else
        {
            if (npc.IsVillager && yielded.Add("All_Villager"))
                yield return "All_Villager";
        }

        if (yielded.Add("Generic"))
            yield return "Generic";
    }

    private bool ConditionMatches(
        NPC npc,
        string? condition,
        Farmer owner,
        CompanionDialogueContext context)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        foreach (string rawToken in condition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool invert = rawToken.StartsWith('!');
            string token = invert ? rawToken[1..] : rawToken;
            string normalized = token.ToLowerInvariant();
            bool result = normalized switch
            {
                "spouse" => context.IsSpouse,
                "morning" => context.DayPeriod == "morning",
                "afternoon" => context.DayPeriod == "afternoon",
                "evening" => context.DayPeriod == "evening",
                "night" => context.TimeOfDay >= 1800,
                "farm" => owner.currentLocation?.Name == "Farm",
                "mine" => owner.currentLocation is StardewValley.Locations.MineShaft,
                "volcano" => owner.currentLocation is StardewValley.Locations.VolcanoDungeon,
                "pet" => npc is Pet,
                "villager" => npc.IsVillager,
                "outdoors" => context.IsOutdoors,
                "indoors" => !context.IsOutdoors,
                "sun" or "rain" or "storm" or "snow" or "green_rain" => context.Weather == normalized,
                "spring" or "summer" or "fall" or "winter" => context.Season.Equals(normalized, StringComparison.OrdinalIgnoreCase),
                "success" => !string.IsNullOrWhiteSpace(context.ResultKey),
                "failure" => !string.IsNullOrWhiteSpace(context.FailureKey),
                "item_found" => !string.IsNullOrWhiteSpace(context.ItemId) || !string.IsNullOrWhiteSpace(context.ItemName),
                "manual" => context.IsManual,
                _ when normalized.StartsWith("hearts>=", StringComparison.Ordinal)
                    => this.GetFriendshipHearts(npc, owner) >= this.ParseTrailingInt(token),
                _ when normalized.StartsWith("hearts<", StringComparison.Ordinal)
                    => this.GetFriendshipHearts(npc, owner) < this.ParseTrailingInt(token),
                _ when normalized.StartsWith("time>=", StringComparison.Ordinal)
                    => context.TimeOfDay >= this.ParseTrailingInt(token),
                _ when normalized.StartsWith("time<", StringComparison.Ordinal)
                    => context.TimeOfDay < this.ParseTrailingInt(token),
                _ when normalized.StartsWith("season=", StringComparison.Ordinal)
                    => context.Season.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("weather=", StringComparison.Ordinal)
                    => context.Weather.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("period=", StringComparison.Ordinal)
                    => context.DayPeriod.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("location=", StringComparison.Ordinal)
                    => context.LocationName.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("context=", StringComparison.Ordinal)
                    => context.LocationContext.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("task=", StringComparison.Ordinal)
                    => context.TaskKind?.ToString().Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase) == true,
                _ when normalized.StartsWith("result=", StringComparison.Ordinal)
                    => context.ResultKey.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("failure=", StringComparison.Ordinal)
                    => context.FailureKey.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
                _ when normalized.StartsWith("item=", StringComparison.Ordinal)
                    => context.ItemId.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase)
                        || context.ItemName.Equals(token[(token.IndexOf('=') + 1)..], StringComparison.OrdinalIgnoreCase),
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
                try
                {
                    this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Timed disconnect dismissal failed for '{member.NpcName}' and was isolated: {ex}", LogLevel.Error);
                }
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
                if (!restore.OriginalMovementSpeedCaptured)
                {
                    restore.OriginalMovementSpeedCaptured = true;
                    restore.OriginalMovementSpeed = npc.speed;
                    restore.OriginalAddedSpeed = npc.addedSpeed;
                    this.MarkStateDirty();
                }

                this.StopCompanionMovement(npc);
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

        // These NPC subclasses run an additional movement state machine after
        // NPC.update (and some install their own controllers every ten minutes).
        // Treating them as ordinary villagers causes double movement and order
        // takeover even when RecruitAllNpcs is enabled.
        if (HasIndependentNpcMovementSystem(npc))
            return false;

        if (this.config.RecruitAllNpcs)
            return !npc.IsMonster && !npc.IsInvisible;

        return npc.CanSocialize || requester.friendshipData.ContainsKey(npc.Name) || this.npcProfiles.ContainsKey(npc.Name);
    }

    private static bool HasIndependentNpcMovementSystem(NPC npc)
    {
        return npc is Child or Horse or Junimo or JunimoHarvester or Raccoon or TrashBear;
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

        if (showWarning && this.ShouldShowFeedbackFor(ownerId))
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

    private IEnumerable<SquadMemberState> GetAvailableMembers(long ownerId)
    {
        return this.members.Values.Where(p => p.OwnerId == ownerId
            && p.Mode == CompanionMode.Following
            && !this.HasActiveWorkArea(p)
            && !this.pendingTasks.ContainsKey(p.NpcName)
            && !this.activeRecallTargets.ContainsKey(p.NpcName));
    }

    private SquadMemberState? GetAvailableMember(
        long ownerId,
        Func<SquadMemberState, bool>? predicate = null)
    {
        IEnumerable<SquadMemberState> available = this.GetAvailableMembers(ownerId);
        return predicate is null ? available.FirstOrDefault() : available.FirstOrDefault(predicate);
    }

    private bool IsMemberNearOwnerInLocation(
        SquadMemberState member,
        Farmer owner,
        GameLocation location)
    {
        NPC? npc = this.GetNpcByName(member.NpcName);
        return npc?.currentLocation == location
            && owner.currentLocation == location
            && IsWithinCompanionDistance(owner.Tile, npc.Tile);
    }

    private Farmer? GetOwnerFarmer(long ownerId)
    {
        if (Game1.player.UniqueMultiplayerID == ownerId)
            return Game1.player;

        return Game1.getOnlineFarmers().FirstOrDefault(p => p.UniqueMultiplayerID == ownerId);
    }

    private bool IsBlockedGameState(bool blockForMenu)
    {
        return this.IsGlobalSimulationBlocked()
            || Game1.currentLocation is null
            || Game1.eventUp
            || Game1.CurrentEvent is not null
            || Game1.fadeToBlack
            || Game1.currentMinigame is not null
            || (blockForMenu && Game1.activeClickableMenu is not null);
    }

    private bool IsGlobalSimulationBlocked()
    {
        return !Context.IsWorldReady
            || Game1.showingEndOfNightStuff
            || this.pendingDailyCompanionRefresh
            || Game1.isFestival();
    }

    private bool IsOwnerSimulationBlocked(long ownerId, bool blockForMenu)
    {
        if (this.IsGlobalSimulationBlocked())
            return true;

        // Each local split-screen records its own UI/event state before the
        // secondary screen exits the authoritative update loop. Remote
        // farmhands aren't represented here, so the host's local state must not
        // pause them.
        if (this.localOwnerSimulationBlocks.TryGetValue(ownerId, out LocalSimulationBlockState localState)
            && unchecked((uint)(Game1.ticks - localState.Tick)) <= 120)
        {
            return blockForMenu ? localState.WithMenu : localState.WithoutMenu;
        }

        if (Context.IsMultiplayer && ownerId != Game1.player.UniqueMultiplayerID)
            return false;

        return this.IsBlockedGameState(blockForMenu);
    }

    private void CaptureLocalOwnerSimulationBlockState()
    {
        this.localOwnerSimulationBlocks[Game1.player.UniqueMultiplayerID] = new LocalSimulationBlockState(
            this.IsBlockedGameState(blockForMenu: false),
            this.IsBlockedGameState(blockForMenu: true),
            Game1.ticks);
    }

    private bool AreTaskActionsSafe(long? ownerId = null)
    {
        return ownerId.HasValue
            ? !this.IsOwnerSimulationBlocked(ownerId.Value, blockForMenu: true)
            : !this.IsBlockedGameState(blockForMenu: true);
    }

    private void MaintainCompanionScheduleLocks(bool stopCurrentRoutes)
    {
        if (!Context.IsWorldReady)
            return;

        foreach (SquadMemberState member in this.members.Values.ToList())
        {
            if (member.Mode == CompanionMode.OriginalRoutine)
                continue;

            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is null)
                continue;

            if (HasIndependentNpcMovementSystem(npc))
            {
                try
                {
                    this.DismissMember(member.NpcName, silent: true, ownerOverride: member.OwnerId);
                    this.Monitor.Log($"Companion '{member.NpcName}' was dismissed because its NPC type has an independent movement system.", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Could not safely dismiss unsupported companion '{member.NpcName}': {ex}", LogLevel.Error);
                }
                continue;
            }

            try
            {
                this.DisableNpcSchedule(npc, stopCurrentRoutes);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed maintaining companion control for '{member.NpcName}': {ex.Message}", LogLevel.Warn);
            }
        }
    }

    private void DisableNpcSchedule(NPC npc, bool stopCurrentRoute)
    {
        if (this.members.TryGetValue(npc.Name, out SquadMemberState? controlledMember))
            this.EnsureOriginalNpcMovementSpeedCaptured(controlledMember, npc);

        bool controllerBelongsToMod = this.IsOwnedCompanionController(npc);
        bool hasExternalController = npc.controller is not null && !controllerBelongsToMod;
        bool hasVanillaStateToClear = npc.Schedule is { Count: > 0 }
            || npc.queuedSchedulePaths.Count > 0
            || npc.currentScheduleDelay >= 0f
            || npc.DirectionsToNewLocation is not null
            || npc.temporaryController is not null
            || npc.IsWalkingInSquare
            || npc.IsReturningToEndPoint()
            || npc.doingEndOfRouteAnimation.Value
            || npc.goingToDoEndOfRouteAnimation.Value
            || !string.IsNullOrWhiteSpace(npc.endOfRouteBehaviorName.Value)
            || npc.shouldPlayRobinHammerAnimation.Value
            || npc.shouldPlaySpousePatioAnimation.Value
            || npc.isSleeping.Value
            || npc.layingDown;

        // The expensive reflection/animation/bed cleanup is an acquisition
        // operation, not per-tick maintenance. Once control is cleanly held,
        // the hot path only needs to preserve ignoreScheduleToday. If another
        // system reintroduces vanilla state, fall through and reacquire it.
        if (!stopCurrentRoute
            && this.controlledNpcLeases.Contains(npc)
            && !hasExternalController
            && !hasVanillaStateToClear)
        {
            npc.ignoreScheduleToday = true;
            return;
        }

        StardewValley.Pathfinding.PathFindController? externalController = controllerBelongsToMod
            ? null
            : npc.controller;
        bool wasSleeping = npc.isSleeping.Value || npc.layingDown;
        int spriteWidthBeforeCleanup = npc.Sprite?.SpriteWidth ?? 16;
        int spriteHeightBeforeCleanup = npc.Sprite?.SpriteHeight ?? 32;

        npc.ignoreScheduleToday = true;
        npc.ClearSchedule();
        npc.queuedSchedulePaths.Clear();
        npc.currentScheduleDelay = -1f;
        npc.DirectionsToNewLocation = null;
        npc.temporaryController = null;

        var startedBehaviorField = this.Helper.Reflection.GetField<string?>(npc, "_startedEndOfRouteBehavior", required: false);
        var finishingBehaviorField = this.Helper.Reflection.GetField<string?>(npc, "_finishingEndOfRouteBehavior", required: false);
        var currentlyAnimatingField = this.Helper.Reflection.GetField<bool>(npc, "currentlyDoingEndOfRouteAnimation", required: false);
        bool routeAnimationStarted = npc.doingEndOfRouteAnimation.Value
            || currentlyAnimatingField?.GetValue() == true
            || !string.IsNullOrWhiteSpace(startedBehaviorField?.GetValue())
            || !string.IsNullOrWhiteSpace(finishingBehaviorField?.GetValue());
        bool hadRouteBehavior = npc.IsWalkingInSquare
            || npc.IsReturningToEndPoint()
            || routeAnimationStarted
            || npc.goingToDoEndOfRouteAnimation.Value
            || !string.IsNullOrWhiteSpace(npc.endOfRouteBehaviorName.Value)
            || npc.shouldPlayRobinHammerAnimation.Value
            || npc.shouldPlaySpousePatioAnimation.Value
            || wasSleeping;
        if (hadRouteBehavior)
        {
            if (routeAnimationStarted)
            {
                // finishEndOfRouteAnimation also clears spouse dialogue. Preserve
                // that unrelated daily state while still letting custom route
                // behaviors clean up temporary sprites and offsets.
                bool shouldSayMarriageDialogue = npc.shouldSayMarriageDialogue.Value;
                List<MarriageDialogueReference> marriageDialogue = npc.currentMarriageDialogue.ToList();
                try
                {
                    npc.EndActivityRouteEndBehavior();
                }
                finally
                {
                    npc.currentMarriageDialogue.Clear();
                    foreach (MarriageDialogueReference dialogue in marriageDialogue)
                        npc.currentMarriageDialogue.Add(dialogue);
                    npc.shouldSayMarriageDialogue.Value = shouldSayMarriageDialogue;
                }
            }

            npc.IsWalkingInSquare = false;
            if (npc.IsReturningToEndPoint())
            {
                // returnToEndPoint has no public cancel API. Making the current
                // bounds its endpoint lets the public method close the state
                // without changing position.
                npc.lastCrossroad = npc.GetBoundingBox();
                npc.returnToEndPoint();
            }

            npc.doingEndOfRouteAnimation.Value = false;
            npc.goingToDoEndOfRouteAnimation.Value = false;
            npc.endOfRouteBehaviorName.Value = null;
            npc.endOfRouteMessage.Value = null;
            npc.nextEndOfRouteMessage = null;
            AnimatedSprite? sprite = npc.Sprite;
            sprite?.StopAnimation();
            startedBehaviorField?.SetValue(null);
            finishingBehaviorField?.SetValue(null);
            this.Helper.Reflection.GetField<string?>(npc, "loadedEndOfRouteBehavior", required: false)?.SetValue(null);
            currentlyAnimatingField?.SetValue(false);
            npc.drawOffset = Vector2.Zero;
            npc.appliedRouteAnimationOffset = Vector2.Zero;
            StardewValley.GameData.Characters.CharacterData? data = npc.GetData();
            if (sprite is not null)
            {
                // Pets don't have CharacterData, and their sprites are normally
                // 32x32 rather than the 16x32 NPC default. Keep the dimensions
                // that were active before sleep/special-state cleanup. For a
                // real route animation, finishEndOfRouteAnimation has already
                // restored the correct custom fallback dimensions.
                int fallbackWidth = routeAnimationStarted ? sprite.SpriteWidth : spriteWidthBeforeCleanup;
                int fallbackHeight = routeAnimationStarted ? sprite.SpriteHeight : spriteHeightBeforeCleanup;
                sprite.SpriteWidth = data?.Size.X ?? fallbackWidth;
                sprite.SpriteHeight = data?.Size.Y ?? fallbackHeight;
                sprite.UpdateSourceRect();
            }
        }

        npc.shouldPlayRobinHammerAnimation.Value = false;
        npc.shouldPlaySpousePatioAnimation.Value = false;
        npc.isSleeping.Value = false;
        if (npc.layingDown)
        {
            npc.layingDown = false;
            if (npc is not Pet)
                npc.HideShadow = false;
        }

        npc.movementPause = 0;
        this.Helper.Reflection.GetField<bool>(npc, "freezeMotion", required: false)?.SetValue(false);

        this.ReleaseCompanionBedReservation(npc, externalController);
        if (npc is Pet pet && pet.isSleepingOnFarmerBed.Value)
        {
            pet.isSleepingOnFarmerBed.Value = false;
            pet.UpdateSleepingOnBed();
        }
        if (npc is Pet controlledPet)
            controlledPet.CurrentBehavior = "Walk";

        bool replacedByExternalController = npc.controller is not null && !controllerBelongsToMod;
        if (stopCurrentRoute || replacedByExternalController)
        {
            this.StopCompanionMovement(npc);
            if (replacedByExternalController)
            {
                this.lastFollowPathTicks.Remove(npc.Name);
                if (this.pendingTasks.TryGetValue(npc.Name, out PendingCompanionTask? interruptedTask))
                {
                    interruptedTask.LastPathTick = 0;
                    interruptedTask.NoProgressTicks = 0;
                }
            }
        }

        this.controlledNpcLeases.Add(npc);
    }

    private void ReleaseCompanionBedReservation(
        NPC npc,
        StardewValley.Pathfinding.PathFindController? externalController)
    {
        if (npc.currentLocation is not StardewValley.Locations.FarmHouse farmHouse)
            return;

        if (npc is Pet pet)
        {
            if (!pet.isSleepingOnFarmerBed.Value)
                return;

            foreach (StardewValley.Objects.BedFurniture bed in farmHouse.furniture
                .OfType<StardewValley.Objects.BedFurniture>()
                .Where(bed => bed.GetBoundingBox().Intersects(pet.GetBoundingBox())))
            {
                // Match BedFurniture's own update scope. Passing only farmers
                // currently inside this farmhouse can make NetMutex discard a
                // valid remote owner's lock before we decide whether to release it.
                bed.mutex.Update(Game1.getOnlineFarmers());
                if (bed.mutex.IsLockHeld() && !IsBedNeededByOtherNpc(farmHouse, bed, npc))
                    bed.mutex.ReleaseLock();
            }

            return;
        }

        if (!npc.isMarried())
            return;

        Point bedSpot = farmHouse.getSpouseBedSpot(npc.Name);
        Rectangle bedSpotBounds = new(bedSpot.X * 64, bedSpot.Y * 64, 64, 64);
        bool headingToBed = externalController?.endPoint == bedSpot;
        bool occupyingBed = bedSpotBounds.Intersects(npc.GetBoundingBox());
        if (!headingToBed && !occupyingBed)
            return;

        foreach (StardewValley.Objects.BedFurniture bed in farmHouse.furniture
            .OfType<StardewValley.Objects.BedFurniture>()
            .Where(bed => bed.GetBoundingBox().Intersects(bedSpotBounds)))
        {
            bed.mutex.Update(Game1.getOnlineFarmers());
            if (bed.mutex.IsLockHeld() && !IsBedNeededByOtherNpc(farmHouse, bed, npc))
                bed.mutex.ReleaseLock();
        }
    }

    private static bool IsBedNeededByOtherNpc(
        StardewValley.Locations.FarmHouse farmHouse,
        StardewValley.Objects.BedFurniture bed,
        NPC ignoredNpc)
    {
        Rectangle bedBounds = bed.GetBoundingBox();
        foreach (NPC other in farmHouse.characters)
        {
            if (ReferenceEquals(other, ignoredNpc))
                continue;

            bool sleepingInBed = bedBounds.Intersects(other.GetBoundingBox())
                && (other.isSleeping.Value
                    || other.layingDown
                    || (other is Pet otherPet && otherPet.isSleepingOnFarmerBed.Value));
            if (sleepingInBed)
                return true;

            if (other.controller is not null)
            {
                Point endPoint = other.controller.endPoint;
                Rectangle endPointBounds = new(endPoint.X * 64, endPoint.Y * 64, 64, 64);
                if (bedBounds.Intersects(endPointBounds))
                    return true;
            }
        }

        return false;
    }

    private void NeutralizeVanillaBedtimeController(NPC npc)
    {
        if (npc.currentLocation is not StardewValley.Locations.FarmHouse farmHouse
            || !npc.isMarried())
        {
            return;
        }

        Point bedSpot = farmHouse.getSpouseBedSpot(npc.Name);
        if (!this.IsOwnedCompanionController(npc)
            && npc.controller?.endPoint == bedSpot)
        {
            this.DisableNpcSchedule(npc, stopCurrentRoute: true);
        }
    }

    private void RestoreNpcSchedule(NPC npc, SquadMemberState member)
    {
        try
        {
            this.RestoreNpcScheduleCore(npc, member);
        }
        finally
        {
            RestoreOriginalNpcMovementSpeed(npc, member);
            this.controlledNpcLeases.Remove(npc);
        }
    }

    private void RestoreNpcScheduleCore(NPC npc, SquadMemberState member)
    {
        string? liveScheduleKey = npc.ScheduleKey;
        bool hadLiveSchedule = npc.Schedule is not null;
        this.StopCompanionMovement(npc);
        this.DisableNpcSchedule(npc, stopCurrentRoute: true);
        npc.ignoreScheduleToday = false;

        if (this.IsOwnerSimulationBlocked(member.OwnerId, blockForMenu: false))
        {
            if (!this.TryRestoreOriginalOrVanillaHome(npc, member))
                throw new InvalidOperationException($"No safe fallback restoration tile was available for '{npc.Name}'.");
            return;
        }

        if (npc is Pet pet)
        {
            this.BeginAllowingVanillaMovement(pet);
            try
            {
                if (Game1.timeOfDay >= 2000)
                    pet.warpToFarmHouse(GetVanillaPetOwner(pet));
                else if (this.TryRestoreOriginalNpcPosition(pet, member))
                    pet.CurrentBehavior = string.IsNullOrWhiteSpace(member.OriginalPetBehavior) ? "Walk" : member.OriginalPetBehavior;
                else
                {
                    pet.setAtFarmPosition();
                    pet.CurrentBehavior = "Walk";
                }
            }
            finally
            {
                this.EndAllowingVanillaMovement(pet);
            }

            pet.ignoreScheduleToday = false;
            return;
        }

        if (this.TryRestoreOriginalSpousePatioActivity(npc, member))
        {
            npc.lastAttemptedSchedule = Game1.timeOfDay;
            npc.ignoreScheduleToday = false;
            return;
        }

        npc.queuedSchedulePaths.Clear();
        npc.lastAttemptedSchedule = -1;
        npc.currentScheduleDelay = -1f;
        GameLocation? locationBeforeScheduleLoad = npc.currentLocation;
        Vector2 positionBeforeScheduleLoad = npc.Position;
        bool scheduleLoaded = this.TryLoadRestorationSchedule(npc, member, liveScheduleKey);
        bool scheduleLoadRepositioned = npc.currentLocation != locationBeforeScheduleLoad
            || Vector2.DistanceSquared(npc.Position, positionBeforeScheduleLoad) > 1f;
        bool TryFallbackFromCurrentState()
        {
            return this.TryCompleteScheduleFallback(
                npc,
                member,
                locationBeforeScheduleLoad,
                positionBeforeScheduleLoad / 64f);
        }
        // A schedule containing a time-zero command can warp synchronously while
        // parsing. Arrival was suppressed above; remove any controller side
        // effect before applying the actual current stop.
        this.StopCompanionMovement(npc);
        if (!scheduleLoaded || npc.Schedule is null)
        {
            if (!this.TryRestoreOriginalOrVanillaHome(npc, member))
                throw new InvalidOperationException($"No safe vanilla home tile was available for '{npc.Name}'.");
            npc.ignoreScheduleToday = false;
            return;
        }

        if (npc.Schedule.Count == 0)
        {
            // A valid schedule may consist only of a time-zero placement. Its
            // parser already moved the NPC to the intended location and there
            // are no later route entries to resume.
            npc.lastAttemptedSchedule = Game1.timeOfDay;
            npc.ignoreScheduleToday = false;
            return;
        }

        KeyValuePair<int, StardewValley.Pathfinding.SchedulePathDescription>? currentStopEntry = npc.Schedule
            .Where(entry => entry.Key <= Game1.timeOfDay)
            .OrderByDescending(entry => entry.Key)
            .Select(entry => (KeyValuePair<int, StardewValley.Pathfinding.SchedulePathDescription>?)entry)
            .FirstOrDefault();
        StardewValley.Pathfinding.SchedulePathDescription? currentStop = currentStopEntry?.Value;
        if (currentStopEntry is { } activeEntry && IsScheduleRouteStillInTransit(activeEntry))
        {
            if (!TryFallbackFromCurrentState())
                throw new InvalidOperationException($"No safe in-transit restoration tile was available for '{npc.Name}'.");
            return;
        }

        if (currentStop is not null && IsMarriedHomeScheduleStop(npc, currentStop))
        {
            bool scheduledLateArrival = currentStopEntry is { } homeEntry
                && Utility.ConvertTimeToMinutes(homeEntry.Key) + GetScheduleRouteTravelMinutes(homeEntry.Value)
                    >= Utility.ConvertTimeToMinutes(2130);
            bool shouldSleep = scheduledLateArrival || Game1.timeOfDay >= 2200;
            if (!this.TryRestoreMarriedNpcHome(npc, shouldSleep))
            {
                if (!TryFallbackFromCurrentState())
                    throw new InvalidOperationException($"The spouse home destination could not be restored for '{npc.Name}'.");
                return;
            }

            npc.lastAttemptedSchedule = Game1.timeOfDay;
            npc.ignoreScheduleToday = false;
            return;
        }

        if (currentStop is not null)
        {
            if (string.IsNullOrWhiteSpace(currentStop.targetLocationName)
                || Game1.getLocationFromName(currentStop.targetLocationName) is not GameLocation scheduledLocation)
            {
                if (!TryFallbackFromCurrentState())
                    throw new InvalidOperationException($"Schedule location '{currentStop.targetLocationName}' is unavailable for '{npc.Name}'.");
                return;
            }

            Vector2 scheduledTile = new(currentStop.targetTile.X, currentStop.targetTile.Y);
            if (!this.PlaceNpc(
                npc,
                scheduledLocation,
                scheduledTile,
                maintainCompanionControl: false,
                suppressVanillaArrival: true))
            {
                if (!TryFallbackFromCurrentState())
                    throw new InvalidOperationException($"No safe schedule restoration tile was available for '{npc.Name}'.");
                return;
            }

            if (currentStop.facingDirection is >= 0 and <= 3)
                npc.faceDirection(currentStop.facingDirection);

            // Future ten-minute checks should only enqueue routes after the
            // current point in the day, not replay the morning route chain.
            npc.lastAttemptedSchedule = Game1.timeOfDay;
            npc.ignoreScheduleToday = false;
            if (!string.IsNullOrWhiteSpace(currentStop.endOfRouteBehavior))
                this.StartRestoredRouteEndBehavior(npc, currentStop);

            return;
        }

        if (hadLiveSchedule || scheduleLoadRepositioned)
        {
            npc.lastAttemptedSchedule = Game1.timeOfDay;
            npc.ignoreScheduleToday = false;
            return;
        }

        if (!this.TryRestoreOriginalOrVanillaHome(npc, member))
            throw new InvalidOperationException($"No safe fallback restoration tile was available for '{npc.Name}'.");
        npc.ignoreScheduleToday = false;
        npc.checkSchedule(Game1.timeOfDay);
    }

    private bool TryLoadRestorationSchedule(NPC npc, SquadMemberState member, string? liveScheduleKey)
    {
        bool sameDay = member.OriginalDayIndex < 0
            || member.OriginalDayIndex == Game1.Date.TotalDays;
        this.BeginSuppressingVanillaArrival(npc);
        try
        {
            if (sameDay && member.OriginalScheduleCaptured)
            {
                if (string.IsNullOrWhiteSpace(member.OriginalScheduleKey))
                {
                    npc.ClearSchedule();
                    return false;
                }

                return npc.TryLoadSchedule(member.OriginalScheduleKey);
            }

            if (!string.IsNullOrWhiteSpace(liveScheduleKey))
                return npc.TryLoadSchedule(liveScheduleKey);

            if (!string.IsNullOrWhiteSpace(npc.islandScheduleName.Value))
                return npc.TryLoadSchedule(npc.islandScheduleName.Value);

            return npc.TryLoadSchedule();
        }
        finally
        {
            this.EndSuppressingVanillaArrival(npc);
        }
    }

    private static bool IsMarriedHomeScheduleStop(
        NPC npc,
        StardewValley.Pathfinding.SchedulePathDescription stop)
    {
        // Marriage schedules use the west BusStop exit as a logical farmhouse
        // endpoint. PathFindController normally rewrites that warp to the
        // spouse's actual home while walking the route.
        return npc.isMarried()
            && string.Equals(stop.targetLocationName, "BusStop", StringComparison.OrdinalIgnoreCase)
            && stop.targetTile.X <= 9
            && stop.targetTile.Y == 23
            && stop.facingDirection == 3;
    }

    private static bool IsScheduleRouteStillInTransit(
        KeyValuePair<int, StardewValley.Pathfinding.SchedulePathDescription> entry)
    {
        int travelMinutes = GetScheduleRouteTravelMinutes(entry.Value);
        if (travelMinutes <= 0)
            return false;

        int departureMinutes = Utility.ConvertTimeToMinutes(entry.Key);
        int currentMinutes = Utility.ConvertTimeToMinutes(Game1.timeOfDay);
        return currentMinutes < departureMinutes + travelMinutes;
    }

    private static int GetScheduleRouteTravelMinutes(
        StardewValley.Pathfinding.SchedulePathDescription stop)
    {
        Stack<Point>? route = stop.route;
        if (route is null || route.Count < 2)
            return 0;

        int walkingPixels = 0;
        Point? previous = null;
        foreach (Point point in route)
        {
            if (previous.HasValue
                && Math.Abs(previous.Value.X - point.X) + Math.Abs(previous.Value.Y - point.Y) == 1)
            {
                walkingPixels += 64;
            }
            previous = point;
        }

        int movementFrames = walkingPixels / DefaultCompanionMovementSpeed;
        int framesPerTenMinutes = Math.Max(1, Game1.realMilliSecondsPerGameTenMinutes / 1000 * 60);
        return (int)Math.Round((float)movementFrames / framesPerTenMinutes) * 10;
    }

    private void StartRestoredRouteEndBehavior(
        NPC npc,
        StardewValley.Pathfinding.SchedulePathDescription stop)
    {
        this.BeginAllowingVanillaMovement(npc);
        try
        {
            npc.StartActivityRouteEndBehavior(
                stop.endOfRouteBehavior,
                stop.endOfRouteMessage ?? "");
        }
        finally
        {
            this.EndAllowingVanillaMovement(npc);
        }
    }

    private bool TryRestoreMarriedNpcHome(NPC npc, bool shouldSleep)
    {
        if (shouldSleep)
            return this.TryRestoreMarriedNpcToBed(npc);

        if (npc.getHome() is not StardewValley.Locations.FarmHouse farmHouse)
            return false;

        Point kitchenSpot = farmHouse.getKitchenStandingSpot();
        if (!this.PlaceNpc(
            npc,
            farmHouse,
            new Vector2(kitchenSpot.X, kitchenSpot.Y),
            maintainCompanionControl: false,
            suppressVanillaArrival: true))
        {
            return false;
        }

        npc.faceDirection(0);
        return true;
    }

    private bool TryRestoreMarriedNpcToBed(NPC npc)
    {
        if (npc.getHome() is not StardewValley.Locations.FarmHouse farmHouse)
            return false;

        Point bedSpot = farmHouse.getSpouseBedSpot(npc.Name);
        if (!this.PlaceNpc(
            npc,
            farmHouse,
            new Vector2(bedSpot.X, bedSpot.Y),
            maintainCompanionControl: false,
            suppressVanillaArrival: true,
            allowOccupiedExactTile: true))
        {
            return false;
        }

        npc.faceDirection(0);
        if (farmHouse.GetSpouseBed() is null)
            return true;

        this.BeginAllowingVanillaMovement(npc);
        try
        {
            StardewValley.Locations.FarmHouse.spouseSleepEndFunction(npc, farmHouse);
        }
        finally
        {
            this.EndAllowingVanillaMovement(npc);
        }

        return true;
    }

    private static Farmer GetVanillaPetOwner(Pet pet)
    {
        List<Farmer> farmers = Game1.getAllFarmers().ToList();
        return farmers.FirstOrDefault(farmer => string.Equals(
                farmer.homeLocation.Value,
                pet.homeLocationName.Value,
                StringComparison.OrdinalIgnoreCase))
            ?? farmers.FirstOrDefault(farmer => string.Equals(
                farmer.getPetName(),
                pet.Name,
                StringComparison.OrdinalIgnoreCase))
            ?? Game1.MasterPlayer;
    }

    private bool TryRestoreOriginalNpcPosition(NPC npc, SquadMemberState member)
    {
        bool originalPositionStillRelevant = member.OriginalDayIndex < 0
            || member.OriginalDayIndex == Game1.Date.TotalDays;
        if (!member.HasOriginalPosition
            || !originalPositionStillRelevant
            || string.IsNullOrWhiteSpace(member.OriginalLocationName)
            || Game1.getLocationFromName(member.OriginalLocationName) is not GameLocation originalLocation)
        {
            return false;
        }

        Vector2 originalTile = NormalizeTile(new Vector2(member.OriginalTileX, member.OriginalTileY));
        return this.PlaceNpc(
            npc,
            originalLocation,
            originalTile,
            maintainCompanionControl: false,
            suppressVanillaArrival: true);
    }

    private bool TryRestoreOriginalSpousePatioActivity(NPC npc, SquadMemberState member)
    {
        bool sameDay = member.OriginalDayIndex < 0
            || member.OriginalDayIndex == Game1.Date.TotalDays;
        if (!sameDay
            || !member.OriginalSpousePatioActivity
            || !npc.isMarried()
            || Game1.timeOfDay >= 2130
            || !this.TryRestoreOriginalNpcPosition(npc, member))
        {
            return false;
        }

        Vector2 originalTile = NormalizeTile(new Vector2(member.OriginalTileX, member.OriginalTileY));
        if (!string.Equals(npc.currentLocation?.NameOrUniqueName, member.OriginalLocationName, StringComparison.Ordinal)
            || NormalizeTile(npc.Tile) != originalTile)
        {
            return false;
        }

        // Halt() clears the network flag but not necessarily the local mirror.
        // Reset the mirror first so NPC.update rebuilds the patio sprite instead
        // of interpreting the restored flag as an instruction to stop it.
        this.Helper.Reflection
            .GetField<bool>(npc, "isPlayingSpousePatioAnimation", required: false)
            ?.SetValue(false);
        npc.shouldPlaySpousePatioAnimation.Value = true;
        npc.shouldPlaySpousePatioAnimation.CancelInterpolation();
        return true;
    }

    private bool TryRestoreOriginalOrVanillaHome(NPC npc, SquadMemberState member)
    {
        // Restoring only the saved bed tile leaves a spouse awake and the bed
        // unreserved, because DisableNpcSchedule deliberately cleared both
        // states. Re-run the vanilla sleep end behavior once bedtime has begun.
        if (npc.isMarried()
            && Game1.timeOfDay >= 2130
            && this.TryRestoreMarriedNpcToBed(npc))
        {
            return true;
        }

        return this.TryRestoreOriginalNpcPosition(npc, member)
            || this.TryRestoreVanillaHome(npc);
    }

    private bool TryCompleteScheduleFallback(
        NPC npc,
        SquadMemberState member,
        GameLocation? preferredLocation = null,
        Vector2? preferredTile = null)
    {
        bool restoredPreferred = preferredLocation is not null
            && preferredTile.HasValue
            && this.PlaceNpc(
                npc,
                preferredLocation,
                preferredTile.Value,
                maintainCompanionControl: false,
                suppressVanillaArrival: true);
        if (!restoredPreferred && !this.TryRestoreOriginalOrVanillaHome(npc, member))
            return false;

        // A safe fallback is terminal for this dismissal. Repeatedly stopping
        // and re-warping a released NPC every second is worse than skipping the
        // unavailable current stop. The remaining routes were precomputed from
        // that missing stop, so pause them until vanilla rebuilds next morning.
        npc.ClearSchedule();
        npc.ignoreScheduleToday = true;
        return true;
    }

    private bool TryRestoreVanillaHome(NPC npc)
    {
        if (npc.isMarried() && npc.getHome() is StardewValley.Locations.FarmHouse farmHouse)
        {
            if (Game1.timeOfDay >= 2130)
                return this.TryRestoreMarriedNpcToBed(npc);

            Point kitchenSpot = farmHouse.getKitchenStandingSpot();
            return this.PlaceNpc(
                npc,
                farmHouse,
                new Vector2(kitchenSpot.X, kitchenSpot.Y),
                maintainCompanionControl: false,
                suppressVanillaArrival: true);
        }

        if (string.IsNullOrWhiteSpace(npc.DefaultMap)
            || Game1.getLocationFromName(npc.DefaultMap) is not GameLocation homeLocation)
        {
            return false;
        }

        return this.PlaceNpc(
            npc,
            homeLocation,
            npc.DefaultPosition / 64f,
            maintainCompanionControl: false,
            suppressVanillaArrival: true);
    }

    private bool IsOwnedCompanionController(NPC npc)
    {
        if (!this.companionMovementControllers.TryGetValue(npc.Name, out CompanionMovementControllerState state))
            return false;

        if (ReferenceEquals(npc.controller, state.Controller))
            return true;

        this.companionMovementControllers.Remove(npc.Name);
        return false;
    }

    private bool HasCompanionController(
        NPC npc,
        CompanionMovementIntent intent,
        GameLocation location,
        Vector2 targetTile)
    {
        return this.TryGetCompanionControllerTarget(npc, intent, location, out Vector2 activeTarget)
            && activeTarget == NormalizeTile(targetTile);
    }

    private bool TryGetCompanionControllerTarget(
        NPC npc,
        CompanionMovementIntent intent,
        GameLocation location,
        out Vector2 targetTile)
    {
        targetTile = default;
        if (!this.companionMovementControllers.TryGetValue(npc.Name, out CompanionMovementControllerState state)
            || !ReferenceEquals(npc.controller, state.Controller))
        {
            this.companionMovementControllers.Remove(npc.Name);
            return false;
        }

        if (state.Intent != intent
            || !string.Equals(state.LocationName, location.NameOrUniqueName, StringComparison.Ordinal))
        {
            return false;
        }

        targetTile = NormalizeTile(state.TargetTile);
        return true;
    }

    private bool TryStartCompanionPath(
        NPC npc,
        GameLocation location,
        Vector2 targetTile,
        CompanionMovementIntent intent)
    {
        targetTile = NormalizeTile(targetTile);
        this.DisableNpcSchedule(npc, stopCurrentRoute: false);
        this.StopCompanionMovement(npc);

        try
        {
            CompanionPathFindController controller = new(
                npc,
                location,
                new Point((int)targetTile.X, (int)targetTile.Y),
                -1,
                allowOffscreenCompletion: intent == CompanionMovementIntent.RoutineTask);
            if (controller.pathToEndPoint is null)
                return false;

            npc.controller = controller;
            this.companionMovementControllers[npc.Name] = new CompanionMovementControllerState(
                controller,
                intent,
                location.NameOrUniqueName,
                targetTile);
            return true;
        }
        catch
        {
            this.companionMovementControllers.Remove(npc.Name);
            npc.controller = null;
            throw;
        }
    }

    private void StopCompanionMovement(NPC npc)
    {
        if (this.members.TryGetValue(npc.Name, out SquadMemberState? controlledMember))
            this.EnsureOriginalNpcMovementSpeedCaptured(controlledMember, npc);

        this.companionMovementControllers.Remove(npc.Name);
        npc.controller = null;
        ResetCompanionMovementSpeed(npc);
        npc.Halt();
        npc.setTrajectory(0, 0);
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
                OriginalDayIndex = restore.OriginalDayIndex,
                OriginalScheduleCaptured = restore.OriginalScheduleCaptured,
                OriginalScheduleKey = restore.OriginalScheduleKey,
                OriginalPetBehavior = restore.OriginalPetBehavior,
                OriginalSpousePatioActivity = restore.OriginalSpousePatioActivity,
                OriginalMovementSpeedCaptured = restore.OriginalMovementSpeedCaptured,
                OriginalMovementSpeed = restore.OriginalMovementSpeed,
                OriginalAddedSpeed = restore.OriginalAddedSpeed
            });
    }

    private static void RestoreOriginalNpcMovementSpeed(NPC npc, SquadMemberState member)
    {
        if (!member.OriginalMovementSpeedCaptured)
            return;

        npc.speed = member.OriginalMovementSpeed;
        npc.addedSpeed = member.OriginalAddedSpeed;
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
            CompanionTaskKind.MovingToWait => "companion.target.marked_spot",
            CompanionTaskKind.Lumbering => "companion.target.wood",
            CompanionTaskKind.Mining => "companion.target.mining",
            CompanionTaskKind.Watering => "companion.target.watering",
            CompanionTaskKind.Gathering => "companion.target.gathering",
            CompanionTaskKind.Harvesting => "companion.target.harvesting",
            CompanionTaskKind.Petting => "companion.target.petting",
            CompanionTaskKind.Fishing => "companion.target.fishing",
            CompanionTaskKind.RefillingWater => "companion.target.water_source",
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
        if (this.sharedWorkTargetReservations.ContainsKey(key))
            return false;

        if (this.workTargetReservations.TryGetValue(key, out string? owner) && !string.Equals(owner, npcName, StringComparison.OrdinalIgnoreCase))
            return false;

        this.workTargetReservations[key] = npcName;
        this.InvalidateTargetPreviews();
        return true;
    }

    private bool TryReserveSharedWorkTarget(string npcName, string locationName, Vector2 tile, string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return false;

        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workTargetReservations.ContainsKey(key))
            return false;

        if (!this.sharedWorkTargetReservations.TryGetValue(key, out SharedWorkTargetReservation? reservation))
        {
            reservation = new SharedWorkTargetReservation { GroupId = groupId };
            this.sharedWorkTargetReservations[key] = reservation;
        }
        else if (!string.Equals(reservation.GroupId, groupId, StringComparison.Ordinal))
            return false;

        reservation.NpcNames.Add(npcName);
        this.InvalidateTargetPreviews();
        return true;
    }

    private void ReleaseWorkTarget(string npcName, string locationName, Vector2 tile)
    {
        string key = this.GetWorkTargetKey(locationName, tile);
        if (this.workTargetReservations.TryGetValue(key, out string? reservedBy)
            && string.Equals(reservedBy, npcName, StringComparison.OrdinalIgnoreCase)
            && this.workTargetReservations.Remove(key))
        {
            this.InvalidateTargetPreviews();
        }

        if (this.sharedWorkTargetReservations.TryGetValue(key, out SharedWorkTargetReservation? sharedReservation)
            && sharedReservation.NpcNames.Remove(npcName))
        {
            if (sharedReservation.NpcNames.Count == 0)
                this.sharedWorkTargetReservations.Remove(key);
            this.InvalidateTargetPreviews();
        }
    }

    private bool IsStandTileReserved(GameLocation location, Vector2 tile, string npcName)
    {
        string key = this.GetWorkTargetKey(location.NameOrUniqueName, tile);
        bool reservedForWork = this.workStandReservations.TryGetValue(key, out string? reservedBy)
            && !string.Equals(reservedBy, npcName, StringComparison.OrdinalIgnoreCase);
        bool reservedForFollow = this.followDestinationsThisUpdate.TryGetValue(key, out string? followOwner)
            && !string.Equals(followOwner, npcName, StringComparison.OrdinalIgnoreCase);
        return reservedForWork || reservedForFollow;
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

        foreach (KeyValuePair<string, SharedWorkTargetReservation> reservation in this.sharedWorkTargetReservations.ToList())
        {
            if (!reservation.Value.NpcNames.Remove(npcName))
                continue;

            if (reservation.Value.NpcNames.Count == 0)
                this.sharedWorkTargetReservations.Remove(reservation.Key);
            removedAny = true;
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

    private void CompleteSharedTargetPeers(PendingCompanionTask completedTask)
    {
        if (string.IsNullOrWhiteSpace(completedTask.SharedTargetGroupId))
            return;

        string resultKey = this.members.TryGetValue(completedTask.NpcName, out SquadMemberState? completedMember)
            ? completedMember.LastTaskResultKey
            : "";

        foreach (PendingCompanionTask peer in this.pendingTasks.Values
            .Where(candidate => !ReferenceEquals(candidate, completedTask)
                && string.Equals(candidate.SharedTargetGroupId, completedTask.SharedTargetGroupId, StringComparison.Ordinal)
                && string.Equals(candidate.LocationName, completedTask.LocationName, StringComparison.Ordinal)
                && NormalizeTile(candidate.TargetTile) == NormalizeTile(completedTask.TargetTile))
            .ToList())
        {
            if (this.members.TryGetValue(peer.NpcName, out SquadMemberState? peerMember))
            {
                if (!string.IsNullOrWhiteSpace(resultKey))
                    this.SetTaskResult(peerMember, resultKey);
                else
                    this.SetTaskFailure(peerMember, "");
            }

            this.RemovePendingTask(peer);
        }
    }

    private void RemovePendingTask(PendingCompanionTask task, string? failureKey = null, bool returning = false)
    {
        if (!this.pendingTasks.TryGetValue(task.NpcName, out PendingCompanionTask? currentTask)
            || !ReferenceEquals(currentTask, task))
        {
            return;
        }

        this.pendingTasks.Remove(task.NpcName);
        if (!task.Manual
            && task.Kind is CompanionTaskKind.Lumbering or CompanionTaskKind.Mining or CompanionTaskKind.Watering
            && (task.UsesWorkDirective || task.UsesConfiguredAutonomy || task.UsesFixedWorkArea)
            && failureKey is "companion.task_failure.task_timeout"
                or "companion.task_failure.no_safe_tile"
                or "companion.task_failure.path_recovery"
                or "companion.task_failure.unexpected_error")
        {
            this.workTargetRetryAfterTicks[this.GetWorkTargetKey(task.LocationName, task.TargetTile)] = Game1.ticks + FailedWorkTargetBackoffTicks;
        }

        this.ReleaseWorkTarget(task.NpcName, task.LocationName, task.TargetTile);
        this.ReleaseStandTile(task.NpcName, task.LocationName, task.StandTile);
        if (this.members.TryGetValue(task.NpcName, out SquadMemberState? member))
        {
            if (string.IsNullOrWhiteSpace(failureKey)
                && task.Kind is not CompanionTaskKind.MovingToWait
                    and not CompanionTaskKind.RefillingWater)
            {
                this.TrySmartDepositAfterTask(member);
            }

            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is not null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(failureKey)
                        && task.Kind != CompanionTaskKind.MovingToWait
                        && failureKey is not "companion.task_failure.recalled"
                            and not "companion.task_failure.tasks_disabled"
                            and not "companion.task_failure.directive_disabled"
                            and not "companion.task_failure.location_changed"
                            and not "companion.task_failure.owner_unavailable")
                    {
                        this.ShowCompanionWorkFailureAnimation(npc, task.Kind, task.TargetTile);
                    }

                    this.StopCompanionMovement(npc);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed stopping companion '{member.NpcName}' while cleaning up a task: {ex.Message}", LogLevel.Warn);
                }
            }

            this.ClearCompanionTarget(member);
            if (!string.IsNullOrWhiteSpace(failureKey))
                this.SetTaskFailure(member, failureKey);

            string nextActivity = member.Mode switch
            {
                CompanionMode.Waiting => "companion.status.waiting",
                CompanionMode.ParkedForDisconnect => "companion.status.parked",
                CompanionMode.OriginalRoutine => "companion.status.original_routine",
                _ when this.IsCompanionChestDepositPending(member) =>
                    "companion.status.depositing",
                _ when task.UsesFixedWorkArea && this.HasActiveWorkArea(member) =>
                    failureKey == "companion.task_failure.tasks_disabled"
                        ? "companion.status.work_area_paused"
                        : string.IsNullOrWhiteSpace(failureKey)
                            ? "companion.status.work_area"
                            : "companion.status.work_area_blocked",
                _ when returning => "companion.status.returning",
                _ => "companion.status.following"
            };
            this.SetCompanionActivity(member, nextActivity);
            bool shouldResumeFixedArea = task.UsesFixedWorkArea && this.HasActiveWorkArea(member);
            if ((!returning || shouldResumeFixedArea)
                && member.Mode == CompanionMode.Following
                && (this.HasActiveWorkDirective(member) || task.UsesConfiguredAutonomy))
            {
                // Don't combine world mutation, target planning, and a fresh
                // pathfinder construction in the task-completion frame.
                int deferredScanTick = Game1.ticks + 5;
                this.nextTaskScanTick = this.nextTaskScanTick <= Game1.ticks
                    ? deferredScanTick
                    : Math.Min(this.nextTaskScanTick, deferredScanTick);
            }

            if (returning && !shouldResumeFixedArea)
            {
                if (npc?.currentLocation is not null)
                    this.ShowCompanionWorkSignal(npc, npc.currentLocation, npc.Tile, "return");
            }
        }
    }
}
