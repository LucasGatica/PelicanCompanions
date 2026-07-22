using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private static readonly SButton[] WheelHeldNavigationButtons =
    {
        SButton.Up,
        SButton.Down,
        SButton.Left,
        SButton.Right,
        SButton.W,
        SButton.A,
        SButton.S,
        SButton.D,
        SButton.DPadUp,
        SButton.DPadDown,
        SButton.DPadLeft,
        SButton.DPadRight,
        SButton.LeftThumbstickUp,
        SButton.LeftThumbstickDown,
        SButton.LeftThumbstickLeft,
        SButton.LeftThumbstickRight,
        SButton.RightThumbstickUp,
        SButton.RightThumbstickDown,
        SButton.RightThumbstickLeft,
        SButton.RightThumbstickRight
    };

    private enum CompanionNpcWheelAction
    {
        Profile,
        Work,
        Stop,
        Dismiss,
        Follow
    }

    private readonly record struct ContextWorldTarget(
        CompanionTaskKind Kind,
        string LocationName,
        Vector2 Tile,
        string TitleKey,
        string TargetNameKey,
        string TargetToken,
        object TargetInstance);

    private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
    {
        if (!Context.IsWorldReady || this.companionActionWheels?.Value is not { IsOpen: true } wheel)
            return;

        this.Helper.Input.SuppressScrollWheel();
        if (e.Delta != 0 && wheel.ChangePage(e.Delta > 0 ? -1 : 1))
            Game1.playSound("shiny4");
    }

    private bool TryHandleCompanionActionWheelInput(ButtonPressedEventArgs e)
    {
        CompanionActionWheel? wheel = this.companionActionWheels?.Value;
        if (wheel is null)
            return false;

        bool keyboardWheelKeyPressed = this.config.QuickActionWheelKey.JustPressed();
        bool controllerWheelKeyPressed = this.config.ControllerQuickActionWheelKey.JustPressed();
        bool wheelKeyPressed = keyboardWheelKeyPressed || controllerWheelKeyPressed;
        if (wheel.IsOpen)
        {
            if (e.Button == SButton.MouseLeft)
                PlayActionWheelResult(wheel.TryActivate(GetUiScreenPixels(e.Cursor)));
            else if (e.Button is SButton.MouseRight or SButton.Escape or SButton.ControllerB or SButton.ControllerBack)
            {
                wheel.Close();
                Game1.playSound("bigDeSelect");
            }
            else if (e.Button is SButton.Enter or SButton.Space or SButton.ControllerA)
            {
                // Confirmation takes precedence over a custom wheel binding.
                // This keeps ControllerA usable when a player binds it as the
                // wheel opener and then presses it again to choose an option.
                wheel.AnchorPointerForNavigation(GetUiScreenPixels(e.Cursor));
                PlayActionWheelResult(wheel.TryActivateFocused());
            }
            else if (wheelKeyPressed)
            {
                if (keyboardWheelKeyPressed)
                    this.Helper.Input.SuppressActiveKeybinds(this.config.QuickActionWheelKey);
                if (controllerWheelKeyPressed)
                    this.Helper.Input.SuppressActiveKeybinds(this.config.ControllerQuickActionWheelKey);
                if (wheel.LastKeybindHandledTick != Game1.ticks)
                {
                    wheel.MarkKeybindHandled(Game1.ticks);
                    wheel.Close();
                    Game1.playSound("bigDeSelect");
                }
            }
            else if (e.Button is SButton.PageUp or SButton.LeftShoulder)
            {
                wheel.AnchorPointerForNavigation(GetUiScreenPixels(e.Cursor));
                if (wheel.ChangePage(-1))
                    Game1.playSound("shiny4");
            }
            else if (e.Button is SButton.PageDown or SButton.RightShoulder)
            {
                wheel.AnchorPointerForNavigation(GetUiScreenPixels(e.Cursor));
                if (wheel.ChangePage(1))
                    Game1.playSound("shiny4");
            }
            else if (TryGetActionWheelDirection(e.Button, out Vector2 direction))
            {
                wheel.AnchorPointerForNavigation(GetUiScreenPixels(e.Cursor));
                if (wheel.MoveFocus(direction.X, direction.Y))
                    Game1.playSound("shiny4");
            }

            this.Helper.Input.Suppress(e.Button);
            return true;
        }

        if (!wheelKeyPressed)
            return false;

        if (!Game1.displayHUD
            || Game1.activeClickableMenu is not null
            || this.IsBlockedGameState(blockForMenu: false))
        {
            return false;
        }

        if (keyboardWheelKeyPressed)
            this.Helper.Input.SuppressActiveKeybinds(this.config.QuickActionWheelKey);
        if (controllerWheelKeyPressed)
            this.Helper.Input.SuppressActiveKeybinds(this.config.ControllerQuickActionWheelKey);
        if (wheel.LastKeybindHandledTick == Game1.ticks)
            return true;

        wheel.MarkKeybindHandled(Game1.ticks);
        if (!this.TryBuildContextActionWheel(e.Cursor, out CompanionActionWheelModel model))
        {
            Game1.playSound("cancel");
            return true;
        }

        // Neutralize a mouse action which was already held when X opened the
        // modal wheel, so it cannot leak through as a tool/action click.
        this.Helper.Input.Suppress(SButton.MouseLeft);
        this.Helper.Input.Suppress(SButton.MouseRight);
        this.SuppressHeldActionWheelNavigation();
        wheel.Open(model, GetUiScreenPixels(e.Cursor));
        Game1.playSound("smallSelect");
        return true;
    }

    private static bool TryGetActionWheelDirection(SButton button, out Vector2 direction)
    {
        direction = button switch
        {
            SButton.Up or SButton.W or SButton.DPadUp or SButton.LeftThumbstickUp or SButton.RightThumbstickUp => new Vector2(0f, -1f),
            SButton.Down or SButton.S or SButton.DPadDown or SButton.LeftThumbstickDown or SButton.RightThumbstickDown => new Vector2(0f, 1f),
            SButton.Left or SButton.A or SButton.DPadLeft or SButton.LeftThumbstickLeft or SButton.RightThumbstickLeft => new Vector2(-1f, 0f),
            SButton.Right or SButton.D or SButton.DPadRight or SButton.LeftThumbstickRight or SButton.RightThumbstickRight => new Vector2(1f, 0f),
            _ => Vector2.Zero
        };
        return direction != Vector2.Zero;
    }

    private static void PlayActionWheelResult(CompanionActionWheelActivationResult result)
    {
        switch (result)
        {
            case CompanionActionWheelActivationResult.Activated:
                Game1.playSound("smallSelect");
                break;
            case CompanionActionWheelActivationResult.PageChanged:
                Game1.playSound("shiny4");
                break;
            case CompanionActionWheelActivationResult.Cancelled:
                Game1.playSound("bigDeSelect");
                break;
        }
    }

    private void SuppressHeldActionWheelNavigation()
    {
        foreach (SButton button in WheelHeldNavigationButtons)
        {
            if (this.Helper.Input.IsDown(button))
                this.Helper.Input.Suppress(button);
        }
    }

    private bool TryBuildContextActionWheel(ICursorPosition cursor, out CompanionActionWheelModel model)
    {
        model = null!;
        NPC? npc = this.FindNpcUnderCursor(cursor);
        if (npc is not null)
        {
            string npcName = npc.Name;
            string locationName = npc.currentLocation?.NameOrUniqueName ?? "";
            if (this.members.TryGetValue(npcName, out SquadMemberState? member))
            {
                if (member.OwnerId != Game1.player.UniqueMultiplayerID)
                {
                    this.Warn("recruitment.not_owner", new { npc = npc.displayName });
                    return false;
                }

                model = this.BuildOwnedNpcActionWheel(npcName, npc.displayName, locationName);
                return true;
            }

            model = this.BuildRecruitActionWheel(npcName, npc.displayName, locationName);
            return true;
        }

        if (!this.TryGetContextWorldTargetUnderCursor(cursor, out ContextWorldTarget target))
        {
            return this.TryBuildGroundActionWheel(cursor, out model);
        }

        if (target.Kind != CompanionTaskKind.Fishing
            && !IsContextTargetWithinCompanionReach(Game1.player.Tile, target.Tile))
        {
            this.Warn("tasks.no_valid_target");
            return false;
        }

        List<SquadMemberState> workers = (target.Kind == CompanionTaskKind.Fishing
                ? this.GetFishingContextCommandMembers(target.LocationName, target.Tile)
                : this.GetContextCommandMembers(target.LocationName, target.Tile))
            .ToList();
        if (workers.Count == 0)
        {
            this.Warn(target.Kind == CompanionTaskKind.Fishing
                ? "fishing.no_rod"
                : "commands.no_followers");
            return false;
        }

        List<CompanionActionWheelOption> options = new()
        {
            new(
                this.Tr(target.Kind == CompanionTaskKind.Fishing
                    ? "wheel.send_all_fishing"
                    : "wheel.send_all"),
                CompanionActionWheelTone.Positive,
                () => this.RequestContextTask(target, npcName: null))
        };
        bool useShortLabels = workers.Count > CompanionActionWheelPagination.PageSize;
        foreach (SquadMemberState worker in workers.Take(12))
        {
            string workerName = worker.NpcName;
            string displayName = worker.DisplayName;
            string actionLabel = this.Tr(
                target.Kind == CompanionTaskKind.Fishing
                    ? "wheel.send_npc_fishing"
                    : "wheel.send_npc",
                new { npc = displayName });
            options.Add(new CompanionActionWheelOption(
                useShortLabels ? displayName : actionLabel,
                CompanionActionWheelTone.Profile,
                () => this.RequestContextTask(target, workerName),
                actionLabel));
        }

        model = new CompanionActionWheelModel(
            this.Tr(target.TitleKey),
            this.Tr("wheel.hint"),
            options,
            () => this.IsContextWorldTargetValid(target),
            PinnedOptionCount: 1);
        return true;
    }

    private bool TryBuildGroundActionWheel(
        ICursorPosition cursor,
        out CompanionActionWheelModel model)
    {
        model = null!;
        GameLocation? location = Game1.currentLocation;
        if (location is null || this.HasBlockingVisualFeatureUnderCursor(cursor))
        {
            this.Warn("wheel.no_context");
            return false;
        }

        Vector2 tile = NormalizeTile(cursor.Tile);
        string locationName = location.NameOrUniqueName;
        if (!this.IsLocalGroundCommandContextValid(locationName, tile))
        {
            bool hasCompanions = this.members.Values.Any(
                member => member.OwnerId == Game1.player.UniqueMultiplayerID);
            this.Warn(hasCompanions ? "wheel.no_safe_ground" : "commands.no_followers");
            return false;
        }

        Vector2 wheelScreenPosition = GetUiScreenPixels(cursor);
        List<CompanionActionWheelOption> options = new()
        {
            new(
                this.Tr("wheel.work"),
                CompanionActionWheelTone.Positive,
                () => this.OpenWorkAreaSpecialtyWheel(locationName, tile, wheelScreenPosition)),
            new(
                this.Tr("management.dismiss_all"),
                CompanionActionWheelTone.Danger,
                this.ConfirmDismissAllFromActionWheel)
        };
        List<SquadMemberState> workers = this.GetGroundCommandMembers(locationName, tile)
            .Take(12)
            .ToList();
        bool useShortLabels = workers.Count + 1 > CompanionActionWheelPagination.PageSize;
        foreach (SquadMemberState worker in workers)
        {
            string workerName = worker.NpcName;
            string displayName = worker.DisplayName;
            string actionLabel = this.Tr("wheel.send_npc", new { npc = displayName });
            options.Add(new CompanionActionWheelOption(
                useShortLabels ? displayName : actionLabel,
                CompanionActionWheelTone.Profile,
                () => this.RequestMoveCompanionToWait(workerName, locationName, tile),
                actionLabel));
        }

        model = new CompanionActionWheelModel(
            this.Tr("wheel.target.ground"),
            this.Tr("wheel.hint"),
            options,
            () => this.IsLocalGroundCommandContextValid(locationName, tile),
            PinnedOptionCount: 2);
        return true;
    }

    private void OpenWorkAreaSpecialtyWheel(string locationName, Vector2 tile, Vector2 screenPosition)
    {
        CompanionActionWheel? wheel = this.companionActionWheels?.Value;
        if (wheel is null || !this.IsWorkAreaWheelContextValid(locationName, tile))
            return;

        CompanionActionWheelOption[] options =
        {
            new(
                this.Tr("companion.directive.wood.short"),
                CompanionActionWheelTone.Positive,
                () => this.OpenWorkAreaMemberWheel(locationName, tile, CompanionWorkSpecialty.Wood, screenPosition)),
            new(
                this.Tr("companion.directive.mining.short"),
                CompanionActionWheelTone.Profile,
                () => this.OpenWorkAreaMemberWheel(locationName, tile, CompanionWorkSpecialty.Mining, screenPosition)),
            new(
                this.Tr("companion.directive.clear.short"),
                CompanionActionWheelTone.Warning,
                () => this.OpenWorkAreaMemberWheel(locationName, tile, CompanionWorkSpecialty.ClearArea, screenPosition))
        };
        wheel.Open(
            new CompanionActionWheelModel(
                this.Tr("wheel.work"),
                this.Tr("wheel.hint"),
                options,
                () => this.IsWorkAreaWheelContextValid(locationName, tile)),
            screenPosition);
    }

    private void OpenWorkAreaMemberWheel(
        string locationName,
        Vector2 tile,
        CompanionWorkSpecialty specialty,
        Vector2 screenPosition)
    {
        CompanionActionWheel? wheel = this.companionActionWheels?.Value;
        if (wheel is null || !this.IsWorkAreaWheelContextValid(locationName, tile))
            return;

        List<SquadMemberState> workers = this.GetGroundCommandMembers(locationName, tile)
            .Take(12)
            .ToList();
        if (workers.Count == 0)
            return;

        List<CompanionActionWheelOption> options = new()
        {
            new(
                this.Tr("wheel.send_all"),
                CompanionActionWheelTone.Positive,
                () => this.RequestWorkArea(
                    npcName: null,
                    locationName,
                    rawCenter: tile,
                    specialty))
        };
        bool useShortLabels = workers.Count > CompanionActionWheelPagination.PageSize;
        foreach (SquadMemberState worker in workers)
        {
            string workerName = worker.NpcName;
            string displayName = worker.DisplayName;
            string actionLabel = this.Tr("wheel.send_npc", new { npc = displayName });
            options.Add(new CompanionActionWheelOption(
                useShortLabels ? displayName : actionLabel,
                CompanionActionWheelTone.Profile,
                () => this.RequestWorkArea(
                    workerName,
                    locationName,
                    rawCenter: tile,
                    specialty),
                actionLabel));
        }

        string specialtyKey = specialty switch
        {
            CompanionWorkSpecialty.Wood => "wood",
            CompanionWorkSpecialty.Mining => "mining",
            _ => "clear"
        };
        wheel.Open(
            new CompanionActionWheelModel(
                $"{this.Tr("wheel.work")}: {this.Tr($"companion.directive.{specialtyKey}.short")}",
                this.Tr("wheel.hint"),
                options,
                () => this.IsWorkAreaWheelContextValid(locationName, tile),
                PinnedOptionCount: 1),
            screenPosition);
    }

    private bool IsWorkAreaWheelContextValid(string locationName, Vector2 tile)
    {
        return this.IsLocalGroundCommandContextValid(locationName, tile)
            && this.GetGroundCommandMembers(locationName, tile).Any();
    }

    private CompanionActionWheelModel BuildOwnedNpcActionWheel(string npcName, string displayName, string locationName)
    {
        CompanionActionWheelOption[] options =
        {
            this.CreateNpcWheelOption(npcName, CompanionNpcWheelAction.Profile, "wheel.profile", CompanionActionWheelTone.Profile),
            this.CreateNpcWheelOption(npcName, CompanionNpcWheelAction.Work, "wheel.work", CompanionActionWheelTone.Positive),
            this.CreateNpcWheelOption(npcName, CompanionNpcWheelAction.Stop, "wheel.stop", CompanionActionWheelTone.Warning),
            this.CreateNpcWheelOption(npcName, CompanionNpcWheelAction.Dismiss, "wheel.dismiss", CompanionActionWheelTone.Danger),
            this.CreateNpcWheelOption(npcName, CompanionNpcWheelAction.Follow, "wheel.follow", CompanionActionWheelTone.Follow)
        };

        return new CompanionActionWheelModel(
            displayName,
            this.Tr("wheel.hint"),
            options,
            () => this.IsOwnedNpcWheelContextValid(npcName, locationName));
    }

    private CompanionActionWheelOption CreateNpcWheelOption(
        string npcName,
        CompanionNpcWheelAction action,
        string labelKey,
        CompanionActionWheelTone tone)
    {
        return new CompanionActionWheelOption(
            this.Tr(labelKey),
            tone,
            () => this.ExecuteOwnedNpcWheelAction(npcName, action));
    }

    private CompanionActionWheelModel BuildRecruitActionWheel(string npcName, string displayName, string locationName)
    {
        return new CompanionActionWheelModel(
            displayName,
            this.Tr("wheel.hint"),
            new[]
            {
                new CompanionActionWheelOption(
                    this.Tr("wheel.recruit"),
                    CompanionActionWheelTone.Positive,
                    () => this.RecruitFromActionWheel(npcName, locationName))
            },
            () => this.IsRecruitNpcWheelContextValid(npcName, locationName));
    }

    private void ExecuteOwnedNpcWheelAction(string npcName, CompanionNpcWheelAction action)
    {
        SquadMemberState? member = this.GetLocalMemberByName(npcName);
        if (member is null)
            return;

        switch (action)
        {
            case CompanionNpcWheelAction.Profile:
                this.OpenCompanionPanel(member.NpcName);
                break;
            case CompanionNpcWheelAction.Work:
                this.EnableCompanionQuickWork(member);
                break;
            case CompanionNpcWheelAction.Stop:
                this.SetWaiting(member.NpcName, member.OwnerId);
                break;
            case CompanionNpcWheelAction.Dismiss:
                this.ConfirmDismissFromActionWheel(member.NpcName, member.DisplayName);
                break;
            case CompanionNpcWheelAction.Follow:
                this.RecallCompanion(member.NpcName, member.OwnerId, showMessage: true);
                break;
        }
    }

    private void ConfirmDismissFromActionWheel(string npcName, string displayName)
    {
        Game1.currentLocation.createQuestionDialogue(
            this.Tr("management.dismiss_confirm", new { npc = displayName }),
            new[]
            {
                new Response("Dismiss", this.Tr("management.dismiss")),
                new Response("Cancel", this.Tr("generic.cancel"))
            },
            (_, answer) =>
            {
                if (answer == "Dismiss")
                    this.DismissMember(npcName);
            });
    }

    private void ConfirmDismissAllFromActionWheel()
    {
        Game1.currentLocation.createQuestionDialogue(
            this.Tr("management.dismiss_all_confirm"),
            new[]
            {
                new Response("DismissAll", this.Tr("management.dismiss_all")),
                new Response("Cancel", this.Tr("generic.cancel"))
            },
            (_, answer) =>
            {
                if (answer == "DismissAll")
                    this.DismissAll(Game1.player.UniqueMultiplayerID);
            });
    }

    private void RecruitFromActionWheel(string npcName, string locationName)
    {
        NPC? npc = this.GetNpcByName(npcName);
        if (npc is null
            || npc.currentLocation?.NameOrUniqueName != locationName
            || this.members.ContainsKey(npcName))
        {
            return;
        }

        this.TryRecruit(npc, Game1.player.UniqueMultiplayerID, showPrompt: false);
    }

    private bool IsOwnedNpcWheelContextValid(string npcName, string locationName)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member)
            || member.OwnerId != Game1.player.UniqueMultiplayerID)
        {
            return false;
        }

        NPC? npc = this.GetNpcByName(npcName);
        return npc is not null
            && Game1.currentLocation is not null
            && npc.currentLocation == Game1.currentLocation
            && npc.currentLocation.NameOrUniqueName == locationName;
    }

    private bool IsRecruitNpcWheelContextValid(string npcName, string locationName)
    {
        if (this.members.ContainsKey(npcName))
            return false;

        NPC? npc = this.GetNpcByName(npcName);
        return npc is not null
            && Game1.currentLocation is not null
            && npc.currentLocation == Game1.currentLocation
            && npc.currentLocation.NameOrUniqueName == locationName;
    }

    private NPC? FindNpcUnderCursor(ICursorPosition cursor)
    {
        GameLocation? location = Game1.currentLocation;
        if (location is null)
            return null;

        Point worldPoint = new((int)MathF.Round(cursor.AbsolutePixels.X), (int)MathF.Round(cursor.AbsolutePixels.Y));
        return location.characters
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)
                && !candidate.IsInvisible
                && candidate.currentLocation == location)
            .Select(candidate => new
            {
                Npc = candidate,
                CollisionBox = candidate.GetBoundingBox(),
                VisualBox = GetNpcVisualBox(candidate)
            })
            .Where(candidate => candidate.VisualBox.Contains(worldPoint))
            .OrderByDescending(candidate => candidate.VisualBox.Bottom)
            .ThenBy(candidate => Vector2.DistanceSquared(
                new Vector2(candidate.VisualBox.Center.X, candidate.VisualBox.Center.Y),
                new Vector2(worldPoint.X, worldPoint.Y)))
            .Select(candidate => candidate.Npc)
            .FirstOrDefault();
    }

    private static Rectangle GetNpcVisualBox(NPC npc)
    {
        Rectangle collisionBox = npc.GetBoundingBox();
        float scale = Math.Max(0.1f, npc.Scale);
        int spriteWidth = Math.Max(
            collisionBox.Width,
            (int)MathF.Ceiling(Math.Max(1, npc.Sprite.SpriteWidth) * Game1.pixelZoom * scale));
        int spriteHeight = Math.Max(
            collisionBox.Height,
            (int)MathF.Ceiling(Math.Max(1, npc.Sprite.SpriteHeight) * Game1.pixelZoom * scale));
        Vector2 drawOffset = npc.drawOffset
            + npc.appliedRouteAnimationOffset
            + new Vector2(0f, npc.yJumpOffset + npc.yOffset);
        return new Rectangle(
            collisionBox.Center.X - spriteWidth / 2 + (int)MathF.Round(drawOffset.X),
            collisionBox.Bottom - spriteHeight + (int)MathF.Round(drawOffset.Y),
            spriteWidth,
            spriteHeight);
    }

    private SquadMemberState? GetLocalMemberByName(string npcName)
    {
        if (!this.members.TryGetValue(npcName, out SquadMemberState? member)
            || member.OwnerId != Game1.player.UniqueMultiplayerID)
        {
            return null;
        }

        NPC? npc = this.GetNpcByName(member.NpcName);
        return npc is not null
            && Game1.currentLocation is not null
            && npc.currentLocation == Game1.currentLocation
                ? member
                : null;
    }

    private void EnableCompanionQuickWork(SquadMemberState member)
    {
        long ownerId = Game1.player.UniqueMultiplayerID;
        if (!this.CanOwnerMutate(member, ownerId))
            return;

        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("SetQuickWork", member.NpcName, desiredEnabled: true);
            return;
        }

        this.SetCompanionQuickWork(member, ownerId, enabled: true);
    }

    private void UpdateCompanionActionWheel()
    {
        CompanionActionWheel? wheel = this.companionActionWheels?.Value;
        if (wheel?.IsOpen == true
            && (this.saveWritesBlocked
                || !Game1.displayHUD
                || Game1.activeClickableMenu is not null
                || this.IsBlockedGameState(blockForMenu: false)))
        {
            wheel.Close();
        }
    }

    private void DrawCompanionActionWheel(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        CompanionActionWheel? wheel = this.companionActionWheels?.Value;
        if (wheel?.IsOpen != true || !Game1.displayHUD)
            return;

        wheel.Draw(spriteBatch, GetUiScreenPixels(this.Helper.Input.GetCursorPosition()));
    }

    private static Vector2 GetUiScreenPixels(ICursorPosition cursor)
    {
        return Utility.ModifyCoordinatesForUIScale(cursor.ScreenPixels);
    }
}
