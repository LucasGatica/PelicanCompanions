using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Pets;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int DialogueTicksPerSecond = 60;
    private const int AmbientDialogueTtlTicks = 8 * DialogueTicksPerSecond;
    private const int TaskDialogueTtlTicks = 10 * DialogueTicksPerSecond;
    private const int CommandDialogueTtlTicks = 12 * DialogueTicksPerSecond;

    private readonly CompanionDialogueScheduler dialogueScheduler = new();
    private readonly Dictionary<string, string> lastObservedCommunicationFailures = new(StringComparer.OrdinalIgnoreCase);

    private bool QueueCompanionCommunication(
        NPC npc,
        string category,
        bool force,
        long? ownerIdOverride = null,
        CompanionDialogueContext? suppliedContext = null)
    {
        bool petExpression = CompanionDialoguePolicy.GetExpressionKind(npc is Pet)
            == CompanionExpressionKind.PetExpression;
        if (petExpression ? !this.config.EnablePetExpressions : !force && !this.config.EnableCommunication)
            return false;

        long ownerId = ownerIdOverride
            ?? suppliedContext?.OwnerId
            ?? (this.members.TryGetValue(npc.Name, out SquadMemberState? member)
                ? member.OwnerId
                : Game1.player.UniqueMultiplayerID);
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        if (owner?.currentLocation is null || npc.currentLocation != owner.currentLocation)
            return false;

        CompanionDialogueContext context = this.BuildDialogueContext(npc, owner, suppliedContext);
        CompanionDialoguePriority priority = GetDialoguePriority(category, force, context);
        int baseTtlTicks = priority switch
        {
            CompanionDialoguePriority.Ambient => AmbientDialogueTtlTicks,
            CompanionDialoguePriority.Task => TaskDialogueTtlTicks,
            _ => CommandDialogueTtlTicks
        };
        int ttlTicks = checked(baseTtlTicks
            + this.config.CommunicationGroupCooldownSeconds * DialogueTicksPerSecond);
        string dedupeKey = string.Join(
            "|",
            npc.Name,
            category,
            context.TaskKind?.ToString() ?? "",
            context.FailureKey,
            context.ItemId);

        CompanionDialogueRequest request = new(
            ownerId,
            npc.Name,
            category,
            priority,
            context,
            force,
            Game1.ticks,
            ttlTicks,
            dedupeKey);
        if (force && category.Equals("Dismiss", StringComparison.OrdinalIgnoreCase))
        {
            int sharedGapTicks = this.GetCommunicationSharedGapTicks();
            return this.dialogueScheduler.CanPresent(ownerId, Game1.ticks, sharedGapTicks)
                && this.TryPresentCompanionCommunication(request);
        }

        return this.dialogueScheduler.Enqueue(request);
    }

    /// <summary>Adapter used at task commit points so dialogue reacts to completed work, not merely queued work.</summary>
    private void QueueTaskSuccess(
        NPC npc,
        SquadMemberState member,
        string category,
        CompanionTaskKind taskKind,
        string resultKey,
        bool manual,
        Item? item = null)
    {
        this.Say(
            npc,
            category,
            force: false,
            ownerIdOverride: member.OwnerId,
            context: new CompanionDialogueContext
            {
                TaskKind = taskKind,
                ItemName = item?.DisplayName ?? "",
                ItemId = item?.QualifiedItemId ?? "",
                ResultKey = resultKey,
                IsManual = manual
            });
    }

    private void UpdateCompanionCommunication()
    {
        if (!Context.IsMainPlayer || this.IsBlockedGameState(blockForMenu: true))
            return;

        this.ObserveCompanionTaskFailures();
        int sharedGapTicks = this.GetCommunicationSharedGapTicks();
        foreach (long ownerId in this.dialogueScheduler.GetOwnersWithPending())
        {
            // Invalid or stale entries are discarded without consuming the group
            // gap. Cap retries so malformed content can never monopolize a tick.
            for (int attempts = 0; attempts < 4; attempts++)
            {
                if (!this.dialogueScheduler.TryDequeue(ownerId, Game1.ticks, sharedGapTicks, out CompanionDialogueRequest request))
                    break;

                if (this.TryPresentCompanionCommunication(request))
                    break;
            }
        }
    }

    private bool TryPresentCompanionCommunication(CompanionDialogueRequest request)
    {
        NPC? npc = this.GetNpcByName(request.NpcName);
        Farmer? owner = this.GetOwnerFarmer(request.OwnerId);
        if (npc?.currentLocation is null
            || owner?.currentLocation is null
            || npc.currentLocation != owner.currentLocation
            || (!string.IsNullOrWhiteSpace(request.Context.LocationName)
                && !string.Equals(
                    npc.currentLocation.NameOrUniqueName,
                    request.Context.LocationName,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        if (npc is Pet pet)
        {
            if (!this.config.EnablePetExpressions)
                return false;

            CompanionExpressionMessage expression = this.BuildPetExpression(pet, request);
            this.ApplyCompanionExpression(expression);
            this.BroadcastCompanionExpression(expression);
            string petIdentity = $"pet:{request.Category}:{expression.EmoteId}:{expression.SoundCue}";
            this.dialogueScheduler.MarkPresented(request.OwnerId, npc.Name, petIdentity, Game1.ticks);
            return true;
        }

        if (!request.Force && !this.config.EnableCommunication)
            return false;

        List<string> recent = this.dialogueScheduler
            .GetRecentIdentities(request.OwnerId, npc.Name)
            .ToList();
        if (this.members.TryGetValue(npc.Name, out SquadMemberState? member))
        {
            foreach (string persistedKey in member.RecentDialogueKeys.AsEnumerable().Reverse())
            {
                if (!recent.Contains(persistedKey, StringComparer.OrdinalIgnoreCase))
                    recent.Add(persistedKey);
            }
        }

        CompanionDialogueLine? selected = this.PickDialogueLine(
            npc,
            request.Category,
            owner,
            request.Context,
            recent);
        // A configured category with no eligible line is an intentional
        // condition miss. Don't bypass its conditions through the legacy
        // dialogue.<category>.generic compatibility fallback.
        if (selected is null && this.HasConfiguredDialogueCategory(npc, request.Category))
            return false;

        string textKey = selected?.TextKey ?? $"dialogue.{request.Category}.generic";
        string line = this.Tr(textKey, this.BuildDialogueTokens(npc, owner, request.Context));
        if (string.IsNullOrWhiteSpace(line) || string.Equals(line, textKey, StringComparison.Ordinal))
            return false;

        CompanionExpressionMessage message = new()
        {
            NpcName = npc.Name,
            LocationName = npc.currentLocation.NameOrUniqueName,
            Text = line,
            TextKey = textKey,
            Context = request.Context
        };
        this.ApplyCompanionExpression(message);
        this.BroadcastCompanionExpression(message);

        string dialogueIdentity = selected is null
            ? textKey
            : CompanionDialogueSelectionPolicy.GetIdentity(selected);
        int minimumIdentityGapTicks = Math.Clamp(selected?.MinIntervalSeconds ?? 0, 0, 3600)
            * DialogueTicksPerSecond;
        this.dialogueScheduler.MarkPresented(
            request.OwnerId,
            npc.Name,
            dialogueIdentity,
            Game1.ticks,
            minimumIdentityGapTicks);
        if (member is not null)
        {
            member.RecentDialogueKeys.RemoveAll(value => string.Equals(value, dialogueIdentity, StringComparison.OrdinalIgnoreCase));
            member.RecentDialogueKeys.Add(dialogueIdentity);
            if (member.RecentDialogueKeys.Count > CompanionDialogueScheduler.SpeakerHistoryLimit)
            {
                member.RecentDialogueKeys.RemoveRange(
                    0,
                    member.RecentDialogueKeys.Count - CompanionDialogueScheduler.SpeakerHistoryLimit);
            }
            this.MarkStateDirty();
        }
        return true;
    }

    private CompanionDialogueContext BuildDialogueContext(
        NPC npc,
        Farmer owner,
        CompanionDialogueContext? supplied)
    {
        GameLocation location = owner.currentLocation;
        string weather = location.IsGreenRainingHere()
            ? "green_rain"
            : location.IsLightningHere()
                ? "storm"
                : location.IsSnowingHere()
                    ? "snow"
                    : location.IsRainingHere()
                        ? "rain"
                        : "sun";
        int time = Game1.timeOfDay;
        string period = time < 1200
            ? "morning"
            : time < 1800
                ? "afternoon"
                : time < 2200
                    ? "evening"
                    : "night";

        return new CompanionDialogueContext
        {
            OwnerId = owner.UniqueMultiplayerID,
            TaskKind = supplied?.TaskKind,
            ItemName = supplied?.ItemName ?? "",
            ItemId = supplied?.ItemId ?? "",
            ResultKey = supplied?.ResultKey ?? "",
            FailureKey = supplied?.FailureKey ?? "",
            Level = supplied?.Level,
            Hearts = this.GetFriendshipHearts(npc, owner),
            TimeOfDay = time,
            Season = location.GetSeasonKey(),
            Weather = weather,
            DayPeriod = period,
            LocationName = location.NameOrUniqueName,
            LocationContext = location.GetLocationContextId(),
            IsSpouse = string.Equals(owner.spouse, npc.Name, StringComparison.OrdinalIgnoreCase),
            IsOutdoors = location.IsOutdoors,
            IsManual = supplied?.IsManual ?? false
        };
    }

    private object BuildDialogueTokens(NPC npc, Farmer owner, CompanionDialogueContext context)
    {
        string itemName = context.ItemName;
        if (!string.IsNullOrWhiteSpace(context.ItemId))
        {
            try
            {
                itemName = ItemRegistry.Create(context.ItemId, allowNull: true)?.DisplayName ?? itemName;
            }
            catch
            {
                // Keep the host-provided display name for unresolved custom items.
            }
        }

        return new
        {
            npc = npc.displayName,
            player = owner.displayName,
            hearts = context.Hearts,
            season = context.Season,
            weather = context.Weather,
            period = context.DayPeriod,
            time = context.TimeOfDay,
            location = context.LocationName,
            task = context.TaskKind?.ToString() ?? "",
            item = itemName,
            itemId = context.ItemId,
            result = string.IsNullOrWhiteSpace(context.ResultKey) ? "" : this.Tr(context.ResultKey),
            failure = string.IsNullOrWhiteSpace(context.FailureKey) ? "" : this.Tr(context.FailureKey),
            level = context.Level ?? 0
        };
    }

    private CompanionExpressionMessage BuildPetExpression(Pet pet, CompanionDialogueRequest request)
    {
        bool failure = !string.IsNullOrWhiteSpace(request.Context.FailureKey)
            || request.Category.Contains("Failure", StringComparison.OrdinalIgnoreCase)
            || request.Category.Contains("Refusal", StringComparison.OrdinalIgnoreCase)
            || request.Category.Equals("Dismiss", StringComparison.OrdinalIgnoreCase);
        bool success = !string.IsNullOrWhiteSpace(request.Context.ResultKey)
            || request.Category.Equals("Recruit", StringComparison.OrdinalIgnoreCase)
            || request.Category.Contains("Complete", StringComparison.OrdinalIgnoreCase);
        PetData? data = pet.GetPetData();
        string sound = success
            ? data?.ContentSound ?? data?.BarkSound ?? ""
            : data?.BarkSound ?? data?.ContentSound ?? "";

        return new CompanionExpressionMessage
        {
            NpcName = pet.Name,
            LocationName = pet.currentLocation?.NameOrUniqueName ?? "",
            Text = "",
            EmoteId = failure ? 16 : success ? 20 : 8,
            SoundCue = sound,
            JumpHeight = failure ? 0f : success ? 2.5f : 1.5f,
            ShakeMilliseconds = failure ? 140 : 0
        };
    }

    private void ApplyCompanionExpression(CompanionExpressionMessage message)
    {
        if (message is null
            || string.IsNullOrWhiteSpace(message.NpcName)
            || string.IsNullOrWhiteSpace(message.LocationName))
        {
            return;
        }

        NPC? npc = this.GetNpcByName(message.NpcName);
        if (npc?.currentLocation is null
            || !string.Equals(npc.currentLocation.NameOrUniqueName, message.LocationName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            string text = message.Text;
            if (!string.IsNullOrWhiteSpace(message.TextKey) && message.Context is CompanionDialogueContext dialogueContext)
            {
                Farmer? owner = dialogueContext.OwnerId.HasValue
                    ? this.GetOwnerFarmer(dialogueContext.OwnerId.Value)
                    : null;
                if (owner is not null)
                {
                    Translation translated = this.Helper.Translation.Get(
                        message.TextKey,
                        this.BuildDialogueTokens(npc, owner, dialogueContext));
                    if (translated.HasValue())
                        text = translated;
                }
            }

            if (CompanionDialoguePolicy.CanSpeak(npc is Pet) && !string.IsNullOrWhiteSpace(text))
                npc.showTextAboveHead(text);
            if (message.EmoteId >= 0)
                npc.doEmote(message.EmoteId);
            if (message.JumpHeight > 0f)
                npc.jumpWithoutSound(Math.Clamp(message.JumpHeight, 0.5f, 6f));
            if (message.ShakeMilliseconds > 0)
                npc.shake(Math.Clamp(message.ShakeMilliseconds, 1, 1000));
            if (!string.IsNullOrWhiteSpace(message.SoundCue))
                npc.currentLocation.localSound(message.SoundCue, npc.Tile);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not present companion expression for '{message.NpcName}': {ex.Message}", LogLevel.Trace);
        }
    }

    private void BroadcastCompanionExpression(CompanionExpressionMessage message)
    {
        if (!Context.IsMainPlayer || Game1.getOnlineFarmers().Count() <= 1)
            return;

        try
        {
            this.Helper.Multiplayer.SendMessage(
                message,
                MessageCompanionExpression,
                modIDs: new[] { this.ModManifest.UniqueID });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not synchronize companion expression: {ex.Message}", LogLevel.Trace);
        }
    }

    private void ObserveCompanionTaskFailures()
    {
        foreach (SquadMemberState member in this.members.Values)
        {
            string failure = member.LastFailureReasonKey ?? "";
            if (!this.lastObservedCommunicationFailures.TryGetValue(member.NpcName, out string? previous))
            {
                this.lastObservedCommunicationFailures[member.NpcName] = failure;
                continue;
            }
            if (string.Equals(previous, failure, StringComparison.Ordinal))
                continue;

            this.lastObservedCommunicationFailures[member.NpcName] = failure;
            if (string.IsNullOrWhiteSpace(failure) || !ShouldReactToTaskFailure(failure))
                continue;

            NPC? npc = this.GetNpcByName(member.NpcName);
            if (npc is null)
                continue;

            this.QueueCompanionCommunication(
                npc,
                "TaskFailure",
                force: false,
                ownerIdOverride: member.OwnerId,
                suppliedContext: new CompanionDialogueContext { FailureKey = failure });
        }

        foreach (string staleNpcName in this.lastObservedCommunicationFailures.Keys
            .Where(name => !this.members.ContainsKey(name))
            .ToList())
        {
            this.lastObservedCommunicationFailures.Remove(staleNpcName);
        }
    }

    private static bool ShouldReactToTaskFailure(string failureKey)
    {
        return failureKey is not "companion.task_failure.path_recovery"
            and not "companion.task_failure.target_reserved"
            and not "companion.task_failure.npc_missing"
            and not "companion.task_failure.recalled"
            and not "companion.task_failure.directive_disabled"
            and not "companion.task_failure.tasks_disabled";
    }

    private static bool IsValidCompanionDialogueContext(CompanionDialogueContext? context)
    {
        return context is null
            || ((!context.TaskKind.HasValue || Enum.IsDefined(context.TaskKind.Value))
                && context.ItemName.Length <= 256
                && context.ItemId.Length <= 128
                && context.ResultKey.Length <= 256
                && context.FailureKey.Length <= 256
                && context.Season.Length <= 32
                && context.Weather.Length <= 32
                && context.DayPeriod.Length <= 32
                && context.LocationName.Length <= 256
                && context.LocationContext.Length <= 128);
    }

    private static CompanionDialoguePriority GetDialoguePriority(
        string category,
        bool force,
        CompanionDialogueContext context)
    {
        if (context.Level.HasValue
            || category.Contains("Complete", StringComparison.OrdinalIgnoreCase)
            || category.Equals("LootFound", StringComparison.OrdinalIgnoreCase))
            return CompanionDialoguePriority.Milestone;
        if (force
            || category.Equals("LootFound", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(context.FailureKey)
            || !string.IsNullOrWhiteSpace(context.ResultKey))
            return CompanionDialoguePriority.Command;
        if (category.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return CompanionDialoguePriority.Ambient;
        return CompanionDialoguePriority.Task;
    }

    private static CompanionTaskKind? GetTaskKindForDialogueCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "lumbering" => CompanionTaskKind.Lumbering,
            "mining" => CompanionTaskKind.Mining,
            "watering" => CompanionTaskKind.Watering,
            "foraging" => CompanionTaskKind.Gathering,
            "harvesting" => CompanionTaskKind.Harvesting,
            "petting" => CompanionTaskKind.Petting,
            "fishing_waiting" => CompanionTaskKind.Fishing,
            "fishing_caught" => CompanionTaskKind.Fishing,
            _ => null
        };
    }

    private int GetCommunicationSharedGapTicks()
    {
        return Math.Max(
            DialogueTicksPerSecond,
            this.config.CommunicationGroupCooldownSeconds * DialogueTicksPerSecond);
    }
}
