using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int CompanionLifeTicksPerSecond = 60;
    private const int CompanionLifeUpdateIntervalTicks = 30;
    private const int CompanionLifeMinimumStationaryTicks = 60;
    private const int CompanionLifeWorldReactionDelayTicks = 90;
    private const int CompanionLifeOwnerReactionGapTicks = 5 * CompanionLifeTicksPerSecond;
    private const int CompanionLifeInitialInteractionDelayTicks = 5 * CompanionLifeTicksPerSecond;
    private const float CompanionInteractionMaximumDistance = 3f;

    private readonly Dictionary<string, CompanionLifeObservation> companionLifeObservations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> nextCompanionIdleAnimationTicks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, int> nextCompanionInteractionTicks = new();
    private readonly Dictionary<long, int> lastCompanionWorldReactionTicks = new();
    private readonly Dictionary<string, int> lastCompanionInteractionTicks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastCompanionPairInteractionTicks =
        new(StringComparer.OrdinalIgnoreCase);
    private int nextCompanionLifeUpdateTick;

    /// <summary>
    /// Update cosmetic idle behavior, contextual reactions, and fair pair
    /// interactions. All decisions are host-authoritative; callers may invoke
    /// this every tick because the method applies its own bounded cadence.
    /// </summary>
    private void UpdateCompanionLife()
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || this.saveWritesBlocked
            || !HasCompanionLifeTickElapsed(Game1.ticks, this.nextCompanionLifeUpdateTick))
        {
            return;
        }

        this.nextCompanionLifeUpdateTick =
            unchecked(Game1.ticks + CompanionLifeUpdateIntervalTicks);
        this.PruneCompanionLifeState();

        List<CompanionLifeCandidate> candidates = new();
        foreach (SquadMemberState member in this.members.Values
            .OrderBy(member => member.OwnerId)
            .ThenBy(member => member.NpcName, StringComparer.OrdinalIgnoreCase))
        {
            NPC? npc = this.GetNpcByName(member.NpcName);
            Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
            if (npc?.currentLocation is null
                || owner?.currentLocation is null
                || this.IsOwnerSimulationBlocked(
                    member.OwnerId,
                    blockForMenu: true))
                continue;

            CompanionLifeObservation observation =
                this.GetOrUpdateCompanionLifeObservation(member, npc, owner);
            if (this.IsCompanionLifeEligible(member, npc, owner, observation))
            {
                candidates.Add(new CompanionLifeCandidate(member, npc, owner, observation));
            }
            else
            {
                // Work controllers can stand still for a long time. Reset the
                // idle clock while operationally busy so the first cosmetic
                // action doesn't fire immediately after work ends.
                observation.StationarySinceTick = Game1.ticks;
                this.nextCompanionIdleAnimationTicks.Remove(member.NpcName);
            }
        }

        if (candidates.Count == 0)
            return;

        HashSet<string> usedNpcNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<long> usedOwnerIds = new();
        this.PresentPendingCompanionWorldReactions(
            candidates,
            usedNpcNames,
            usedOwnerIds);
        this.TryRunCompanionInteractions(
            candidates,
            usedNpcNames,
            usedOwnerIds);
        this.TryRunCompanionIdleAnimations(
            candidates,
            usedNpcNames,
            usedOwnerIds);
    }

    private CompanionLifeObservation GetOrUpdateCompanionLifeObservation(
        SquadMemberState member,
        NPC npc,
        Farmer owner)
    {
        string locationName = npc.currentLocation?.NameOrUniqueName ?? "";
        string weather = GetCompanionLifeWeather(npc.currentLocation);
        Vector2 position = npc.Position;
        int now = Game1.ticks;

        if (!this.companionLifeObservations.TryGetValue(
                member.NpcName,
                out CompanionLifeObservation? observation)
            || observation.OwnerId != member.OwnerId)
        {
            observation = new CompanionLifeObservation
            {
                OwnerId = member.OwnerId,
                LocationName = locationName,
                Weather = weather,
                LastPosition = position,
                StationarySinceTick = now
            };
            this.companionLifeObservations[member.NpcName] = observation;
            return observation;
        }

        bool ownerSharesLocation = npc.currentLocation == owner.currentLocation;
        if (!string.Equals(
                observation.LocationName,
                locationName,
                StringComparison.Ordinal))
        {
            observation.LocationName = locationName;
            observation.Weather = weather;
            observation.LastPosition = position;
            observation.StationarySinceTick = now;
            observation.PendingWeatherReaction = false;
            observation.PendingLocationReaction = ownerSharesLocation;
            observation.ReactionReadyTick =
                unchecked(now + CompanionLifeWorldReactionDelayTicks);
            return observation;
        }

        if (!string.Equals(observation.Weather, weather, StringComparison.Ordinal))
        {
            observation.Weather = weather;
            observation.PendingWeatherReaction = ownerSharesLocation;
            observation.ReactionReadyTick =
                unchecked(now + CompanionLifeWorldReactionDelayTicks);
        }

        if (Vector2.DistanceSquared(position, observation.LastPosition) > 0.01f)
        {
            observation.LastPosition = position;
            observation.StationarySinceTick = now;
        }

        return observation;
    }

    private bool IsCompanionLifeEligible(
        SquadMemberState member,
        NPC npc,
        Farmer owner,
        CompanionLifeObservation observation)
    {
        return member.Mode is CompanionMode.Following or CompanionMode.Waiting
            && npc.currentLocation is not null
            && npc.currentLocation == owner.currentLocation
            && !npc.IsInvisible
            && npc.controller is null
            && !this.pendingTasks.ContainsKey(member.NpcName)
            && !this.activeRecallTargets.ContainsKey(member.NpcName)
            && !this.companionMovementControllers.ContainsKey(member.NpcName)
            && !this.companionWorkAnimations.ContainsKey(member.NpcName)
            && CompanionDialogueScheduler.HasElapsed(
                Game1.ticks,
                observation.StationarySinceTick,
                CompanionLifeMinimumStationaryTicks);
    }

    private void PresentPendingCompanionWorldReactions(
        IReadOnlyList<CompanionLifeCandidate> candidates,
        ISet<string> usedNpcNames,
        ISet<long> usedOwnerIds)
    {
        foreach (IGrouping<long, CompanionLifeCandidate> ownerGroup in candidates
            .Where(candidate => candidate.Observation.PendingLocationReaction
                || candidate.Observation.PendingWeatherReaction)
            .GroupBy(candidate => candidate.Member.OwnerId))
        {
            long ownerId = ownerGroup.Key;
            int reactionGapTicks = Math.Max(
                CompanionLifeOwnerReactionGapTicks,
                Math.Max(0, this.config.DialogueCooldownSeconds)
                    * CompanionLifeTicksPerSecond);
            if (this.lastCompanionWorldReactionTicks.TryGetValue(
                    ownerId,
                    out int lastReactionTick)
                && !CompanionDialogueScheduler.HasElapsed(
                    Game1.ticks,
                    lastReactionTick,
                    reactionGapTicks))
            {
                continue;
            }

            CompanionLifeCandidate? selected = ownerGroup
                .Where(candidate => HasCompanionLifeTickElapsed(
                    Game1.ticks,
                    candidate.Observation.ReactionReadyTick))
                .OrderBy(candidate => candidate.Observation.ReactionReadyTick)
                .ThenBy(candidate => candidate.Member.NpcName, StringComparer.OrdinalIgnoreCase)
                .Cast<CompanionLifeCandidate?>()
                .FirstOrDefault();
            if (selected is not CompanionLifeCandidate reaction)
                continue;

            CompanionLifeObservation observation = reaction.Observation;
            bool locationReaction = observation.PendingLocationReaction;
            foreach (CompanionLifeCandidate candidate in ownerGroup)
            {
                if (locationReaction)
                    candidate.Observation.PendingLocationReaction = false;
                else
                    candidate.Observation.PendingWeatherReaction = false;
            }

            string preferredCategory = locationReaction
                ? "LocationArrival"
                : "WeatherReaction";
            string category = this.GetCompanionLifeDialogueCategory(
                reaction.Npc,
                preferredCategory);
            bool queued = this.Say(
                reaction.Npc,
                category,
                force: false,
                ownerIdOverride: reaction.Member.OwnerId);

            if (queued)
            {
                CompanionIdleAnimationKind animation = locationReaction
                    ? CompanionIdleAnimationKind.LookAround
                    : GetWeatherReactionAnimation(observation.Weather);
                this.PlayCompanionLifeAnimation(reaction.Npc, animation);
            }
            this.lastCompanionWorldReactionTicks[ownerId] = Game1.ticks;
            usedNpcNames.Add(reaction.Member.NpcName);
            usedOwnerIds.Add(ownerId);
        }
    }

    private void TryRunCompanionInteractions(
        IReadOnlyList<CompanionLifeCandidate> candidates,
        ISet<string> usedNpcNames,
        ISet<long> usedOwnerIds)
    {
        if (!this.config.EnableCompanionInteractions)
            return;

        int cooldownTicks = checked(
            CompanionIdleAnimationPolicy.NormalizeInteractionCooldownSeconds(
                this.config.CompanionInteractionCooldownSeconds)
            * CompanionLifeTicksPerSecond);
        foreach (IGrouping<long, CompanionLifeCandidate> ownerGroup in candidates
            .Where(candidate => !usedNpcNames.Contains(candidate.Member.NpcName)
                && !usedOwnerIds.Contains(candidate.Member.OwnerId))
            .GroupBy(candidate => candidate.Member.OwnerId))
        {
            long ownerId = ownerGroup.Key;
            if (!this.nextCompanionInteractionTicks.TryGetValue(
                    ownerId,
                    out int nextInteractionTick))
            {
                this.nextCompanionInteractionTicks[ownerId] =
                    unchecked(Game1.ticks + CompanionLifeInitialInteractionDelayTicks);
                continue;
            }
            if (!HasCompanionLifeTickElapsed(Game1.ticks, nextInteractionTick))
                continue;

            CompanionLifeCandidate[] group = ownerGroup.ToArray();
            List<CompanionLifePair> pairs = new();
            for (int firstIndex = 0; firstIndex < group.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < group.Length; secondIndex++)
                {
                    CompanionLifeCandidate first = group[firstIndex];
                    CompanionLifeCandidate second = group[secondIndex];
                    if (first.Npc.currentLocation != second.Npc.currentLocation
                        || Vector2.Distance(first.Npc.Tile, second.Npc.Tile)
                            > CompanionInteractionMaximumDistance)
                    {
                        continue;
                    }

                    string pairKey = GetCompanionLifePairKey(
                        ownerId,
                        first.Member.NpcName,
                        second.Member.NpcName);
                    int pairLastTick = this.lastCompanionPairInteractionTicks
                        .GetValueOrDefault(pairKey, int.MinValue);
                    int firstLastTick = this.lastCompanionInteractionTicks
                        .GetValueOrDefault(first.Member.NpcName, int.MinValue);
                    int secondLastTick = this.lastCompanionInteractionTicks
                        .GetValueOrDefault(second.Member.NpcName, int.MinValue);
                    pairs.Add(new CompanionLifePair(
                        first,
                        second,
                        pairKey,
                        pairLastTick,
                        Math.Max(firstLastTick, secondLastTick)));
                }
            }

            CompanionLifePair? selected = pairs
                .OrderBy(pair => pair.MostRecentNpcInteractionTick)
                .ThenBy(pair => pair.PairInteractionTick)
                .ThenBy(pair => pair.PairKey, StringComparer.Ordinal)
                .Cast<CompanionLifePair?>()
                .FirstOrDefault();
            if (selected is not CompanionLifePair pair)
            {
                this.nextCompanionInteractionTicks[ownerId] =
                    unchecked(Game1.ticks + CompanionLifeTicksPerSecond);
                continue;
            }

            CompanionLifeCandidate speaker = this.random.Next(2) == 0
                ? pair.First
                : pair.Second;
            CompanionLifeCandidate responder = speaker.Member.NpcName.Equals(
                pair.First.Member.NpcName,
                StringComparison.OrdinalIgnoreCase)
                ? pair.Second
                : pair.First;
            string speakerCategory = this.GetCompanionLifeDialogueCategory(
                speaker.Npc,
                "CompanionInteraction");
            string responderCategory = this.GetCompanionLifeDialogueCategory(
                responder.Npc,
                "CompanionInteractionReply");
            bool queued = this.Say(
                speaker.Npc,
                speakerCategory,
                force: false,
                ownerIdOverride: ownerId);
            if (queued)
            {
                int speakerFacing = GetFacingDirectionToward(
                    speaker.Npc,
                    responder.Npc);
                int responderFacing = GetFacingDirectionToward(
                    responder.Npc,
                    speaker.Npc);
                this.PlayCompanionLifeAnimation(
                    speaker.Npc,
                    CompanionIdleAnimationKind.Happy,
                    speakerFacing);
                this.PlayCompanionLifeAnimation(
                    responder.Npc,
                    CompanionIdleAnimationKind.LookAround,
                    responderFacing);
                this.Say(
                    responder.Npc,
                    responderCategory,
                    force: false,
                    ownerIdOverride: ownerId);
            }

            this.lastCompanionInteractionTicks[pair.First.Member.NpcName] = Game1.ticks;
            this.lastCompanionInteractionTicks[pair.Second.Member.NpcName] = Game1.ticks;
            this.lastCompanionPairInteractionTicks[pair.PairKey] = Game1.ticks;
            this.nextCompanionInteractionTicks[ownerId] =
                unchecked(Game1.ticks + cooldownTicks);
            usedNpcNames.Add(pair.First.Member.NpcName);
            usedNpcNames.Add(pair.Second.Member.NpcName);
            usedOwnerIds.Add(ownerId);
        }
    }

    private void TryRunCompanionIdleAnimations(
        IReadOnlyList<CompanionLifeCandidate> candidates,
        ISet<string> usedNpcNames,
        ISet<long> usedOwnerIds)
    {
        if (!this.config.EnableIdleAnimations)
            return;

        int intervalTicks = checked(
            CompanionIdleAnimationPolicy.NormalizeIntervalSeconds(
                this.config.IdleAnimationIntervalSeconds)
            * CompanionLifeTicksPerSecond);
        foreach (CompanionLifeCandidate candidate in candidates)
        {
            string npcName = candidate.Member.NpcName;
            if (usedNpcNames.Contains(npcName)
                || usedOwnerIds.Contains(candidate.Member.OwnerId))
                continue;

            if (!this.nextCompanionIdleAnimationTicks.TryGetValue(
                    npcName,
                    out int nextAnimationTick))
            {
                this.nextCompanionIdleAnimationTicks[npcName] =
                    unchecked(Game1.ticks + GetCompanionLifeJitter(intervalTicks));
                continue;
            }
            if (!HasCompanionLifeTickElapsed(Game1.ticks, nextAnimationTick))
                continue;

            IReadOnlyList<CompanionIdleAnimationKind> animations =
                this.GetCompanionIdleAnimations(candidate.Npc);
            CompanionIdleAnimationKind selected =
                animations[this.random.Next(animations.Count)];
            this.PlayCompanionLifeAnimation(candidate.Npc, selected);
            this.nextCompanionIdleAnimationTicks[npcName] =
                unchecked(Game1.ticks + GetCompanionLifeJitter(intervalTicks));
            usedOwnerIds.Add(candidate.Member.OwnerId);
        }
    }

    private IReadOnlyList<CompanionIdleAnimationKind> GetCompanionIdleAnimations(
        NPC npc)
    {
        foreach (string profileKey in this.GetProfileKeys(npc))
        {
            if (!this.npcProfiles.TryGetValue(
                    profileKey,
                    out NpcCompanionProfile? profile)
                || profile?.IdleAnimations is null)
            {
                continue;
            }

            CompanionIdleAnimationKind[] parsed = profile.IdleAnimations
                .Select(CompanionIdleAnimationPolicy.Parse)
                .Where(animation => animation.HasValue)
                .Select(animation => animation!.Value)
                .Distinct()
                .ToArray();
            if (parsed.Length > 0)
                return parsed;
        }

        return new[]
        {
            CompanionIdleAnimationKind.LookAround,
            CompanionIdleAnimationKind.Happy,
            CompanionIdleAnimationKind.Jump
        };
    }

    private void PlayCompanionLifeAnimation(
        NPC npc,
        CompanionIdleAnimationKind animation,
        int facingDirection = -1)
    {
        if (npc.currentLocation is null
            || npc is StardewValley.Characters.Pet
                && !this.config.EnablePetExpressions)
            return;

        if (facingDirection is < 0 or > 3
            && animation == CompanionIdleAnimationKind.LookAround)
        {
            int offset = this.random.Next(1, 4);
            facingDirection = (npc.FacingDirection + offset) % 4;
        }

        CompanionExpressionMessage message = new()
        {
            NpcName = npc.Name,
            LocationName = npc.currentLocation.NameOrUniqueName,
            FacingDirection = facingDirection,
            EmoteId = animation == CompanionIdleAnimationKind.Happy ? 20 : -1,
            JumpHeight = animation == CompanionIdleAnimationKind.Jump ? 2.5f : 0f,
            ShakeMilliseconds = animation == CompanionIdleAnimationKind.Shake ? 180 : 0
        };

        this.ApplyCompanionExpression(message);
        this.BroadcastCompanionExpression(message);
    }

    private string GetCompanionLifeDialogueCategory(
        NPC npc,
        string preferredCategory)
    {
        return this.HasConfiguredDialogueCategory(npc, preferredCategory)
            ? preferredCategory
            : "Idle";
    }

    private void PruneCompanionLifeState()
    {
        HashSet<string> activeNpcNames = this.members.Keys.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (string npcName in this.companionLifeObservations.Keys
            .Where(npcName => !activeNpcNames.Contains(npcName))
            .ToArray())
        {
            this.companionLifeObservations.Remove(npcName);
            this.nextCompanionIdleAnimationTicks.Remove(npcName);
            this.lastCompanionInteractionTicks.Remove(npcName);
        }

        HashSet<long> activeOwnerIds = this.members.Values
            .Select(member => member.OwnerId)
            .ToHashSet();
        foreach (long ownerId in this.nextCompanionInteractionTicks.Keys
            .Where(ownerId => !activeOwnerIds.Contains(ownerId))
            .ToArray())
        {
            this.nextCompanionInteractionTicks.Remove(ownerId);
        }
        foreach (long ownerId in this.lastCompanionWorldReactionTicks.Keys
            .Where(ownerId => !activeOwnerIds.Contains(ownerId))
            .ToArray())
            this.lastCompanionWorldReactionTicks.Remove(ownerId);

        foreach (string pairKey in this.lastCompanionPairInteractionTicks.Keys
            .Where(pairKey => !IsCompanionLifePairActive(
                pairKey,
                activeNpcNames,
                activeOwnerIds))
            .ToArray())
        {
            this.lastCompanionPairInteractionTicks.Remove(pairKey);
        }
    }

    private int GetCompanionLifeJitter(int intervalTicks)
    {
        int variation = Math.Max(
            CompanionLifeUpdateIntervalTicks,
            intervalTicks / 4);
        int minimum = Math.Max(
            CompanionLifeUpdateIntervalTicks,
            intervalTicks - variation);
        int maximum = Math.Max(minimum + 1, intervalTicks + variation + 1);
        return this.random.Next(minimum, maximum);
    }

    private void ResetCompanionLifeState()
    {
        this.companionLifeObservations.Clear();
        this.nextCompanionIdleAnimationTicks.Clear();
        this.nextCompanionInteractionTicks.Clear();
        this.lastCompanionWorldReactionTicks.Clear();
        this.lastCompanionInteractionTicks.Clear();
        this.lastCompanionPairInteractionTicks.Clear();
        this.nextCompanionLifeUpdateTick = 0;
    }

    private static CompanionIdleAnimationKind GetWeatherReactionAnimation(
        string weather)
    {
        return weather is "rain" or "storm" or "snow" or "green_rain"
            ? CompanionIdleAnimationKind.Shake
            : CompanionIdleAnimationKind.Happy;
    }

    private static string GetCompanionLifeWeather(GameLocation? location)
    {
        if (location is null)
            return "";

        try
        {
            return location.IsGreenRainingHere()
                ? "green_rain"
                : location.IsLightningHere()
                    ? "storm"
                    : location.IsSnowingHere()
                        ? "snow"
                        : location.IsRainingHere()
                            ? "rain"
                            : "sun";
        }
        catch
        {
            return "sun";
        }
    }

    private static int GetFacingDirectionToward(NPC source, NPC target)
    {
        Vector2 delta = target.Tile - source.Tile;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
            return delta.X >= 0f ? 1 : 3;
        return delta.Y >= 0f ? 2 : 0;
    }

    private static string GetCompanionLifePairKey(
        long ownerId,
        string firstNpcName,
        string secondNpcName)
    {
        string first = (firstNpcName ?? "").Trim().ToUpperInvariant();
        string second = (secondNpcName ?? "").Trim().ToUpperInvariant();
        if (string.Compare(first, second, StringComparison.Ordinal) > 0)
            (first, second) = (second, first);
        return $"{ownerId}|{first}|{second}";
    }

    private static bool IsCompanionLifePairActive(
        string pairKey,
        ISet<string> activeNpcNames,
        ISet<long> activeOwnerIds)
    {
        string[] parts = pairKey.Split('|', 3);
        return parts.Length == 3
            && long.TryParse(parts[0], out long ownerId)
            && activeOwnerIds.Contains(ownerId)
            && activeNpcNames.Contains(parts[1])
            && activeNpcNames.Contains(parts[2]);
    }

    private static bool HasCompanionLifeTickElapsed(int nowTick, int deadlineTick)
    {
        return unchecked((int)((uint)nowTick - (uint)deadlineTick)) >= 0;
    }

    private sealed class CompanionLifeObservation
    {
        public long OwnerId { get; init; }
        public string LocationName { get; set; } = "";
        public string Weather { get; set; } = "";
        public Vector2 LastPosition { get; set; }
        public int StationarySinceTick { get; set; }
        public bool PendingLocationReaction { get; set; }
        public bool PendingWeatherReaction { get; set; }
        public int ReactionReadyTick { get; set; }
    }

    private readonly record struct CompanionLifeCandidate(
        SquadMemberState Member,
        NPC Npc,
        Farmer Owner,
        CompanionLifeObservation Observation);

    private readonly record struct CompanionLifePair(
        CompanionLifeCandidate First,
        CompanionLifeCandidate Second,
        string PairKey,
        int PairInteractionTick,
        int MostRecentNpcInteractionTick);
}
