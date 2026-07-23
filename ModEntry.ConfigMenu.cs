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
    private void RegisterGenericModConfigMenu()
    {
        IGenericModConfigMenuApiCompat? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApiCompat>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(
            this.ModManifest,
            reset: () => this.config = new ModConfig(),
            save: () =>
            {
                this.config.Validate();
                if (Context.IsMainPlayer && !this.saveWritesBlocked)
                {
                    this.RebalanceMemberInventoriesForCapacity();
                    this.EnforceConfiguredWorkAreaRadius();
                    this.MarkStateDirty();
                }
                this.Helper.WriteConfig(this.config);
            },
            titleScreenOnly: false);

        this.AddParagraph(gmcm, "config.overview");

        this.AddSection(gmcm, "config.section.controls");
        this.AddParagraph(gmcm, "config.section.controls.description");
        this.AddKeybindOption(gmcm, "quickActionWheelKey", () => this.config.QuickActionWheelKey, value => this.config.QuickActionWheelKey = value);
        this.AddKeybindOption(gmcm, "controllerQuickActionWheelKey", () => this.config.ControllerQuickActionWheelKey, value => this.config.ControllerQuickActionWheelKey = value);
        this.AddKeybindOption(gmcm, "recruitKey", () => this.config.RecruitKey, value => this.config.RecruitKey = value);
        this.AddKeybindOption(gmcm, "manualTaskKey", () => this.config.ManualTaskKey, value => this.config.ManualTaskKey = value);
        this.AddKeybindOption(gmcm, "openSquadInventoryKey", () => this.config.OpenSquadInventoryKey, value => this.config.OpenSquadInventoryKey = value);
        this.AddKeybindOption(gmcm, "tasksToggleKey", () => this.config.TasksToggleKey, value => this.config.TasksToggleKey = value);
        this.AddKeybindOption(gmcm, "openCompanionPanelKey", () => this.config.OpenCompanionPanelKey, value => this.config.OpenCompanionPanelKey = value);
        this.AddKeybindOption(gmcm, "recallAllCompanionsKey", () => this.config.RecallAllCompanionsKey, value => this.config.RecallAllCompanionsKey = value);

        this.AddSection(gmcm, "config.section.interface");
        this.AddParagraph(gmcm, "config.section.interface.description");
        this.AddBoolOption(gmcm, "showCompanionQuickHud", () => this.config.ShowCompanionQuickHud, value => this.config.ShowCompanionQuickHud = value);
        this.AddEnumOption(gmcm, "companionQuickHudMode", () => this.config.CompanionQuickHudMode, value => this.config.CompanionQuickHudMode = value);
        this.AddEnumOption(gmcm, "companionQuickHudSide", () => this.config.CompanionQuickHudSide, value => this.config.CompanionQuickHudSide = value);
        this.AddBoundedIntOption(gmcm, "companionQuickHudMaxRows", () => this.config.CompanionQuickHudMaxRows, value => this.config.CompanionQuickHudMaxRows = value, 1, 12);
        this.AddBoolOption(gmcm, "showCompanionLevelUpHud", () => this.config.ShowCompanionLevelUpHud, value => this.config.ShowCompanionLevelUpHud = value);

        this.AddSection(gmcm, "config.section.movement");
        this.AddEnumOption(gmcm, "companionFormationMode", () => this.config.CompanionFormationMode, value => this.config.CompanionFormationMode = value);
        this.AddBoolOption(gmcm, "showCompanionMovementDebug", () => this.config.ShowCompanionMovementDebug, value => this.config.ShowCompanionMovementDebug = value);

        this.AddSection(gmcm, "config.section.recruitment");
        this.AddBoundedIntOption(gmcm, "friendshipRequirement", () => this.config.FriendshipRequirement, value => this.config.FriendshipRequirement = value, 0, 14);
        this.AddIntOption(gmcm, "friendshipPointsPerHour", () => this.config.FriendshipPointsPerHour, value => this.config.FriendshipPointsPerHour = value);
        this.AddBoundedIntOption(gmcm, "maxSquadSize", () => this.config.MaxSquadSize, value => this.config.MaxSquadSize = value, 1, 12);
        this.AddBoolOption(gmcm, "recruitAllNpcs", () => this.config.RecruitAllNpcs, value => this.config.RecruitAllNpcs = value);

        this.AddSection(gmcm, "config.section.inventory");
        this.AddBoolOption(gmcm, "useSquadInventory", () => this.config.UseSquadInventory, value => this.config.UseSquadInventory = value);
        this.AddBoolOption(gmcm, "useVanillaDialogueUi", () => this.config.UseVanillaDialogueUi, value => this.config.UseVanillaDialogueUi = value);
        this.AddBoolOption(gmcm, "enableCompanionProgression", () => this.config.EnableCompanionProgression, value => this.config.EnableCompanionProgression = value);
        this.AddBoundedIntOption(gmcm, "companionInventorySlots", () => this.config.CompanionInventorySlots, value => this.config.CompanionInventorySlots = value, 1, 10);
        this.AddBoundedIntOption(gmcm, "companionWorkRadius", () => this.config.CompanionWorkRadius, value => this.config.CompanionWorkRadius = value, 3, 20);
        this.AddBoundedIntOption(gmcm, "companionWorkReturnDistance", () => this.config.CompanionWorkReturnDistance, value => this.config.CompanionWorkReturnDistance = value, 3, 40);

        this.AddSection(gmcm, "config.section.dialogue");
        this.AddBoolOption(gmcm, "enableCommunication", () => this.config.EnableCommunication, value => this.config.EnableCommunication = value);
        this.AddIntOption(gmcm, "dialogueCooldownSeconds", () => this.config.DialogueCooldownSeconds, value => this.config.DialogueCooldownSeconds = value);
        this.AddIntOption(gmcm, "communicationGroupCooldownSeconds", () => this.config.CommunicationGroupCooldownSeconds, value => this.config.CommunicationGroupCooldownSeconds = value);
        this.AddBoolOption(gmcm, "enablePetExpressions", () => this.config.EnablePetExpressions, value => this.config.EnablePetExpressions = value);
        this.AddBoolOption(gmcm, "enableIdleAnimations", () => this.config.EnableIdleAnimations, value => this.config.EnableIdleAnimations = value);
        this.AddBoundedIntOption(gmcm, "idleAnimationIntervalSeconds", () => this.config.IdleAnimationIntervalSeconds, value => this.config.IdleAnimationIntervalSeconds = value, 5, 300);
        this.AddBoolOption(gmcm, "enableCompanionInteractions", () => this.config.EnableCompanionInteractions, value => this.config.EnableCompanionInteractions = value);
        this.AddBoundedIntOption(gmcm, "companionInteractionCooldownSeconds", () => this.config.CompanionInteractionCooldownSeconds, value => this.config.CompanionInteractionCooldownSeconds = value, 15, 1800);

        this.AddSection(gmcm, "config.section.tasks");
        this.AddEnumOption(gmcm, "smartWaterRefill", () => this.config.SmartWaterRefill, value => this.config.SmartWaterRefill = value);
        this.AddBoundedIntOption(gmcm, "smartWaterRefillSearchRadius", () => this.config.SmartWaterRefillSearchRadius, value => this.config.SmartWaterRefillSearchRadius = value, 3, 40);
        this.AddBoolOption(gmcm, "enableSmartToolSwap", () => this.config.EnableSmartToolSwap, value => this.config.EnableSmartToolSwap = value);
        this.AddEnumOption(gmcm, "smartDeposit", () => this.config.SmartDeposit, value => this.config.SmartDeposit = value);
        this.AddBoolOption(gmcm, "enableGathering", () => this.config.EnableGathering, value => this.config.EnableGathering = value);
        this.AddEnumOption(gmcm, "harvestingMode", () => this.config.HarvestingMode, value => this.config.HarvestingMode = value);
        this.AddIntOption(gmcm, "protectBeehouseFlowers", () => this.config.ProtectBeehouseFlowers, value => this.config.ProtectBeehouseFlowers = value);
        this.AddEnumOption(gmcm, "foragingMode", () => this.config.ForagingMode, value => this.config.ForagingMode = value);
        this.AddEnumOption(gmcm, "lumberingMode", () => this.config.LumberingMode, value => this.config.LumberingMode = value);
        this.AddEnumOption(gmcm, "miningMode", () => this.config.MiningMode, value => this.config.MiningMode = value);
        this.AddEnumOption(gmcm, "wateringMode", () => this.config.WateringMode, value => this.config.WateringMode = value);
        this.AddEnumOption(gmcm, "pettingMode", () => this.config.PettingMode, value => this.config.PettingMode = value);

        this.AddSection(gmcm, "config.section.multiplayer");
        this.AddBoolOption(gmcm, "warpHomeOnDisconnect", () => this.config.WarpHomeOnDisconnect, value => this.config.WarpHomeOnDisconnect = value);
        this.AddIntOption(gmcm, "parkTimeoutMinutes", () => this.config.ParkTimeoutMinutes, value => this.config.ParkTimeoutMinutes = value);
    }

    private void AddSection(IGenericModConfigMenuApiCompat gmcm, string key)
    {
        gmcm.AddSectionTitle(this.ModManifest, () => this.Tr(key), tooltip: null);
    }

    private void AddParagraph(IGenericModConfigMenuApiCompat gmcm, string key)
    {
        gmcm.AddParagraph(this.ModManifest, () => this.Tr(key));
    }

    private void AddBoolOption(IGenericModConfigMenuApiCompat gmcm, string key, Func<bool> get, Action<bool> set)
    {
        gmcm.AddBoolOption(this.ModManifest, get, set, () => this.Tr($"config.{key}.name"), () => this.Tr($"config.{key}.description"), key);
    }

    private void AddBoundedIntOption(IGenericModConfigMenuApiCompat gmcm, string key, Func<int> get, Action<int> set, int min, int max)
    {
        string[] values = Enumerable.Range(min, max - min + 1)
            .Select(value => value.ToString())
            .ToArray();

        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (int.TryParse(value, out int parsed))
                {
                    set(Math.Clamp(parsed, min, max));
                    this.config.Validate();
                }
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            values,
            value => value,
            fieldId: key);
    }

    private void AddIntOption(IGenericModConfigMenuApiCompat gmcm, string key, Func<int> get, Action<int> set)
    {
        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (int.TryParse(value, out int parsed))
                {
                    set(parsed);
                    this.config.Validate();
                }
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            allowedValues: null,
            formatAllowedValue: null,
            fieldId: key);
    }

    private void AddKeybindOption(IGenericModConfigMenuApiCompat gmcm, string key, Func<KeybindList> get, Action<KeybindList> set)
    {
        gmcm.AddKeybindList(
            this.ModManifest,
            get,
            set,
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            fieldId: key);
    }

    private void AddEnumOption<TEnum>(IGenericModConfigMenuApiCompat gmcm, string key, Func<TEnum> get, Action<TEnum> set)
        where TEnum : struct, Enum
    {
        string[] values = Enum.GetNames<TEnum>();
        gmcm.AddTextOption(
            this.ModManifest,
            () => get().ToString(),
            value =>
            {
                if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
                    set(parsed);
            },
            () => this.Tr($"config.{key}.name"),
            () => this.Tr($"config.{key}.description"),
            values,
            value => this.Tr($"config.enum.{value}"),
            key);
    }

    private int ParseTrailingInt(string token)
    {
        string digits = new(token.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int parsed) ? parsed : 0;
    }

    private void Info(string key, object? tokens = null)
    {
        this.ShowFeedback(this.Tr(key, tokens), isError: false);
    }

    private void Warn(string key, object? tokens = null)
    {
        this.ShowFeedback(this.Tr(key, tokens), isError: true);
    }

    private void InfoForPlayer(long playerId, string key, object? tokens = null)
    {
        this.ShowFeedbackForPlayer(playerId, this.Tr(key, tokens), isError: false);
    }

    private void WarnForPlayer(long playerId, string key, object? tokens = null)
    {
        this.ShowFeedbackForPlayer(playerId, this.Tr(key, tokens), isError: true);
    }

    private void ShowFeedbackForPlayer(long playerId, string text, bool isError)
    {
        long? previousTarget = this.commandFeedbackTargetPlayerId;
        this.commandFeedbackTargetPlayerId = playerId;
        try
        {
            this.ShowFeedback(text, isError);
        }
        finally
        {
            this.commandFeedbackTargetPlayerId = previousTarget;
        }
    }

    private void ShowFeedback(string text, bool isError)
    {
        if (this.commandFeedbackTargetPlayerId is long targetPlayerId
            && Context.IsMainPlayer
            && targetPlayerId != Game1.player.UniqueMultiplayerID)
        {
            try
            {
                this.Helper.Multiplayer.SendMessage(
                    new CompanionCommandFeedbackMessage
                    {
                        Text = text,
                        IsError = isError,
                        Action = this.commandFeedbackAction ?? "",
                        CommandId = this.commandFeedbackCommandId ?? ""
                    },
                    MessageCommandFeedback,
                    modIDs: new[] { this.ModManifest.UniqueID },
                    playerIDs: new[] { targetPlayerId });
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not send companion command feedback to player {targetPlayerId}: {ex.Message}", LogLevel.Warn);
            }
            return;
        }

        Game1.addHUDMessage(isError
            ? new HUDMessage(text, HUDMessage.error_type)
            : new HUDMessage(text));
    }

    private bool ShouldShowFeedbackFor(long playerId)
    {
        return playerId == Game1.player.UniqueMultiplayerID
            || this.commandFeedbackTargetPlayerId == playerId;
    }

    private string Tr(string key, object? tokens = null)
    {
        return tokens is null
            ? this.Helper.Translation.Get(key)
            : this.Helper.Translation.Get(key, tokens);
    }
}

/// <summary>
/// Local compatibility surface for the subset of GMCM used by this mod. Keeping
/// this contract local avoids a runtime dependency on SMAPI's newer convenience
/// interface while still exposing GMCM's native keybind-list widget.
/// </summary>
public interface IGenericModConfigMenuApiCompat
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

    void AddParagraph(IManifest mod, Func<string> text);

    void AddBoolOption(
        IManifest mod,
        Func<bool> getValue,
        Action<bool> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string? fieldId = null);

    void AddTextOption(
        IManifest mod,
        Func<string> getValue,
        Action<string> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string[]? allowedValues = null,
        Func<string, string>? formatAllowedValue = null,
        string? fieldId = null);

    void AddKeybindList(
        IManifest mod,
        Func<KeybindList> getValue,
        Action<KeybindList> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string? fieldId = null);
}
