using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using XTileLocation = xTile.Dimensions.Location;

namespace PelicanCompanions;

/// <summary>A normalized square which can be restored when reopening the routine area picker.</summary>
internal readonly record struct RoutineAreaSelectionPreset(int MinX, int MinY, int Size);

/// <summary>
/// Farm-view picker for a square routine work area. It intentionally imitates
/// the public camera/view flow used by the carpenter menu without inheriting
/// its building, resource, or multiplayer-lock behavior.
/// </summary>
internal sealed class RoutineAreaSelectionMenu : IClickableMenu
{
    private const int MinimumSize = 3;
    private const int MaximumSize = 41;
    private const int DefaultSize = 17;
    private const int TileSize = 64;
    private const int EdgePanMargin = 64;
    private const int EdgePanSpeed = 8;
    private const int KeyboardPanSpeed = 4;
    private const int ButtonGap = 8;
    private const int ButtonHeight = 48;

    private static readonly Color PreviewFill = new(74, 178, 92);
    private static readonly Color PreviewBorder = new(232, 246, 210);
    private static readonly Color PanelFill = new(255, 244, 211);
    private static readonly Color PanelText = new(91, 57, 36);
    private static readonly Color ConfirmFill = new(116, 172, 103);
    private static readonly Color CancelFill = new(205, 116, 92);

    private readonly IClickableMenu returnMenu;
    private readonly Farm farm;
    private readonly Func<string, object?, string> translate;
    private readonly Action<int, int, int> onConfirm;
    private readonly GameLocation originLocation;
    private readonly string originLocationName;
    private readonly string? originViewingLocation;
    private readonly XTileLocation originViewport;
    private readonly Point originTile;
    private readonly int originFacingDirection;
    private readonly bool originDisplayHud;
    private readonly bool originDisplayFarmer;
    private readonly bool originViewportFreeze;
    private readonly int mapWidth;
    private readonly int mapHeight;
    private readonly Point initialCenterTile;

    private Rectangle minusButton;
    private Rectangle plusButton;
    private Rectangle confirmButton;
    private Rectangle cancelButton;
    private int size;
    private bool viewReady;
    private bool returning;
    private bool restored;
    private bool invokeConfirmation;
    private RoutineAreaSelectionPreset confirmedArea;

    public RoutineAreaSelectionMenu(
        IClickableMenu returnMenu,
        Farm farm,
        RoutineAreaSelectionPreset? initialPreset,
        Func<string, object?, string> translate,
        Action<int, int, int> onConfirm)
        : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, false)
    {
        this.returnMenu = returnMenu ?? throw new ArgumentNullException(nameof(returnMenu));
        this.farm = farm ?? throw new ArgumentNullException(nameof(farm));
        this.translate = translate ?? throw new ArgumentNullException(nameof(translate));
        this.onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));

        if (farm.Map is null || farm.Map.Layers.Count == 0)
            throw new ArgumentException("The farm must have a loaded map.", nameof(farm));

        this.mapWidth = farm.Map.Layers[0].LayerWidth;
        this.mapHeight = farm.Map.Layers[0].LayerHeight;
        int mapMaximumSize = Math.Min(MaximumSize, Math.Min(this.mapWidth, this.mapHeight));
        if (mapMaximumSize < MinimumSize)
            throw new ArgumentException("The farm map is too small for a routine area.", nameof(farm));

        this.originLocation = Game1.currentLocation
            ?? throw new InvalidOperationException("A current location is required to open the routine area picker.");
        this.originLocationName = this.originLocation.NameOrUniqueName;
        this.originViewingLocation = Game1.player.viewingLocation.Value;
        this.originViewport = Game1.viewport.Location;
        this.originTile = Game1.player.TilePoint;
        this.originFacingDirection = Game1.player.FacingDirection;
        this.originDisplayHud = Game1.displayHUD;
        this.originDisplayFarmer = Game1.displayFarmer;
        this.originViewportFreeze = Game1.viewportFreeze;

        if (initialPreset is RoutineAreaSelectionPreset preset)
        {
            this.size = Math.Clamp(preset.Size, MinimumSize, mapMaximumSize);
            int minX = Math.Clamp(preset.MinX, 0, this.mapWidth - this.size);
            int minY = Math.Clamp(preset.MinY, 0, this.mapHeight - this.size);
            this.initialCenterTile = new Point(minX + this.size / 2, minY + this.size / 2);
        }
        else
        {
            this.size = Math.Clamp(DefaultSize, MinimumSize, mapMaximumSize);
            Point farmhouseEntry = farm.GetMainFarmHouseEntry();
            this.initialCenterTile = new Point(
                Math.Clamp(farmhouseEntry.X, 0, this.mapWidth - 1),
                Math.Clamp(farmhouseEntry.Y, 0, this.mapHeight - 1));
        }

        this.ReflowButtons();
        Game1.player.forceCanMove();
        Game1.globalFadeToBlack(this.EnterFarmView, 0.02f);
    }

    public override bool shouldClampGamePadCursor()
    {
        return this.viewReady;
    }

    public override bool overrideSnappyMenuCursorMovementBan()
    {
        return this.viewReady;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (!this.CanAcceptInput())
            return;

        if (this.minusButton.Contains(x, y))
        {
            this.ChangeSize(-1);
            return;
        }
        if (this.plusButton.Contains(x, y))
        {
            this.ChangeSize(1);
            return;
        }
        if (this.cancelButton.Contains(x, y))
        {
            this.BeginReturn(confirm: false);
            return;
        }
        if (this.confirmButton.Contains(x, y))
        {
            this.BeginReturn(confirm: true);
            return;
        }

        this.BeginReturn(confirm: true);
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        if (this.CanAcceptInput())
            this.BeginReturn(confirm: false);
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (this.CanAcceptInput() && direction != 0)
            this.ChangeSize(direction > 0 ? 1 : -1);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (!this.CanAcceptInput())
            return;

        if (Game1.options.doesInputListContain(Game1.options.menuButton, key)
            || key is Keys.Escape)
        {
            this.BeginReturn(confirm: false);
            return;
        }

        if (key is Keys.Enter or Keys.Space)
        {
            this.BeginReturn(confirm: true);
            return;
        }
        if (key is Keys.OemPlus or Keys.Add)
        {
            this.ChangeSize(1);
            return;
        }
        if (key is Keys.OemMinus or Keys.Subtract)
        {
            this.ChangeSize(-1);
            return;
        }

        if (Game1.options.doesInputListContain(Game1.options.moveDownButton, key))
            Game1.panScreen(0, KeyboardPanSpeed);
        else if (Game1.options.doesInputListContain(Game1.options.moveRightButton, key))
            Game1.panScreen(KeyboardPanSpeed, 0);
        else if (Game1.options.doesInputListContain(Game1.options.moveUpButton, key))
            Game1.panScreen(0, -KeyboardPanSpeed);
        else if (Game1.options.doesInputListContain(Game1.options.moveLeftButton, key))
            Game1.panScreen(-KeyboardPanSpeed, 0);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (!this.CanAcceptInput())
            return;

        switch (button)
        {
            case Buttons.B:
            case Buttons.Back:
                this.BeginReturn(confirm: false);
                return;

            case Buttons.LeftShoulder:
                this.ChangeSize(-1);
                return;

            case Buttons.RightShoulder:
                this.ChangeSize(1);
                return;

            case Buttons.A:
                int mouseX = Game1.getMouseX();
                int mouseY = Game1.getMouseY();
                if (this.minusButton.Contains(mouseX, mouseY))
                    this.ChangeSize(-1);
                else if (this.plusButton.Contains(mouseX, mouseY))
                    this.ChangeSize(1);
                else if (this.cancelButton.Contains(mouseX, mouseY))
                    this.BeginReturn(confirm: false);
                else
                    this.BeginReturn(confirm: true);
                return;
        }
    }

    public override void gamePadButtonHeld(Buttons button)
    {
        if (!this.CanAcceptInput())
            return;

        int horizontal = button switch
        {
            Buttons.DPadLeft or Buttons.LeftThumbstickLeft => -1,
            Buttons.DPadRight or Buttons.LeftThumbstickRight => 1,
            _ => 0
        };
        int vertical = button switch
        {
            Buttons.DPadUp or Buttons.LeftThumbstickUp => -1,
            Buttons.DPadDown or Buttons.LeftThumbstickDown => 1,
            _ => 0
        };
        if (horizontal == 0 && vertical == 0)
            return;

        GamePadState state = Game1.input.GetGamePadState();
        int speed = state.IsButtonDown(Buttons.LeftTrigger) || state.IsButtonDown(Buttons.RightTrigger)
            ? 20
            : 12;
        MouseState mouse = Game1.input.GetMouseState();
        Game1.setMousePositionRaw(mouse.X + horizontal * speed, mouse.Y + vertical * speed);
    }

    public override void update(GameTime time)
    {
        base.update(time);
        if (!this.CanAcceptInput())
            return;

        int mouseX = Game1.getOldMouseX(false);
        int mouseY = Game1.getOldMouseY(false);
        bool overControls = this.minusButton.Contains(mouseX, mouseY)
            || this.plusButton.Contains(mouseX, mouseY)
            || this.confirmButton.Contains(mouseX, mouseY)
            || this.cancelButton.Contains(mouseX, mouseY);
        if (!overControls)
        {
            if (mouseX < EdgePanMargin)
                Game1.panScreen(-EdgePanSpeed, 0);
            else if (mouseX >= Game1.uiViewport.Width - EdgePanMargin)
                Game1.panScreen(EdgePanSpeed, 0);

            if (mouseY < EdgePanMargin)
                Game1.panScreen(0, -EdgePanSpeed);
            else if (mouseY >= Game1.uiViewport.Height - EdgePanMargin)
                Game1.panScreen(0, EdgePanSpeed);
        }

        foreach (Keys key in Game1.oldKBState.GetPressedKeys())
            this.receiveKeyPress(key);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.returnMenu.gameWindowSizeChanged(oldBounds, newBounds);
        this.width = Game1.uiViewport.Width;
        this.height = Game1.uiViewport.Height;
        this.ReflowButtons();
        if (this.viewReady)
            Game1.clampViewportToGameMap();
    }

    public override void draw(SpriteBatch b)
    {
        if (!this.viewReady || Game1.IsFading())
            return;

        RoutineAreaSelectionPreset area = this.GetSelection();
        Game1.StartWorldDrawInUI(b);
        try
        {
            Vector2 screen = Game1.GlobalToLocal(
                Game1.viewport,
                new Vector2(area.MinX * TileSize, area.MinY * TileSize));
            Rectangle bounds = new(
                (int)screen.X,
                (int)screen.Y,
                area.Size * TileSize,
                area.Size * TileSize);
            b.Draw(Game1.staminaRect, bounds, PreviewFill * 0.23f);

            const int border = 4;
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, bounds.Width, border), PreviewBorder * 0.92f);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Bottom - border, bounds.Width, border), PreviewBorder * 0.92f);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, border, bounds.Height), PreviewBorder * 0.92f);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.Right - border, bounds.Y, border, bounds.Height), PreviewBorder * 0.92f);
        }
        finally
        {
            Game1.EndWorldDrawInUI(b);
        }

        string title = this.GetText(
            "companion.routine.area_selector.title",
            new { size = this.size },
            $"Routine work area — {this.size} × {this.size}");
        string hint = this.GetText(
            "companion.routine.area_selector.hint",
            new { size = this.size },
            "Move the cursor to position the square. Scroll or LB/RB changes its size.");
        int bannerWidth = Math.Min(Math.Max(360, (int)Math.Max(
            Game1.smallFont.MeasureString(title).X,
            Game1.smallFont.MeasureString(hint).X) + 36), Math.Max(1, Game1.uiViewport.Width - 32));
        Rectangle banner = new(
            (Game1.uiViewport.Width - bannerWidth) / 2,
            16,
            bannerWidth,
            72);
        IClickableMenu.drawTextureBox(b, banner.X, banner.Y, banner.Width, banner.Height, PanelFill);
        this.DrawCenteredText(b, title, new Rectangle(banner.X + 12, banner.Y + 8, banner.Width - 24, 25), PanelText);
        this.DrawCenteredText(b, hint, new Rectangle(banner.X + 12, banner.Y + 38, banner.Width - 24, 22), PanelText * 0.82f);

        this.DrawButton(b, this.minusButton, "−", PanelFill);
        this.DrawButton(b, this.plusButton, "+", PanelFill);
        this.DrawButton(
            b,
            this.confirmButton,
            this.GetText("companion.routine.area_selector.confirm", null, "Confirm"),
            ConfirmFill);
        this.DrawButton(
            b,
            this.cancelButton,
            this.GetText("generic.cancel", null, "Cancel"),
            CancelFill);
        this.drawMouse(b);
    }

    public override void emergencyShutDown()
    {
        this.RestoreViewImmediately(reactivateReturnMenu: false);
        base.emergencyShutDown();
    }

    protected override void cleanupBeforeExit()
    {
        this.RestoreViewImmediately(reactivateReturnMenu: false);
        base.cleanupBeforeExit();
    }

    private void EnterFarmView()
    {
        if (this.returning || this.restored)
            return;

        Game1.currentLocation.cleanupBeforePlayerExit();
        Game1.currentLocation = this.farm;
        Game1.player.viewingLocation.Value = this.farm.NameOrUniqueName;
        this.farm.resetForPlayerEntry();
        Game1.displayHUD = false;
        Game1.viewportFreeze = true;
        Game1.displayFarmer = false;

        Game1.viewport.Location = new XTileLocation(
            this.initialCenterTile.X * TileSize + TileSize / 2 - Game1.viewport.Width / 2,
            this.initialCenterTile.Y * TileSize + TileSize / 2 - Game1.viewport.Height / 2);
        Game1.clampViewportToGameMap();
        Game1.panScreen(0, 0);
        Vector2 initialCursor = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(
                this.initialCenterTile.X * TileSize + TileSize / 2,
                this.initialCenterTile.Y * TileSize + TileSize / 2));
        Game1.setMousePositionRaw(
            Math.Clamp((int)initialCursor.X, 0, Math.Max(0, Game1.uiViewport.Width - 1)),
            Math.Clamp((int)initialCursor.Y, 0, Math.Max(0, Game1.uiViewport.Height - 1)));
        this.viewReady = true;
        Game1.globalFadeToClear(null, 0.02f);
    }

    private bool CanAcceptInput()
    {
        return this.viewReady && !this.returning && !this.restored && !Game1.IsFading();
    }

    private void BeginReturn(bool confirm)
    {
        if (!this.CanAcceptInput())
            return;

        this.returning = true;
        this.viewReady = false;
        this.invokeConfirmation = confirm;
        if (confirm)
            this.confirmedArea = this.GetSelection();
        Game1.playSound(confirm ? "smallSelect" : "bigDeSelect");

        try
        {
            LocationRequest request = Game1.getLocationRequest(this.originLocationName, false);
            request.OnWarp += this.OnReturnedToOrigin;
            Game1.warpFarmer(
                request,
                this.originTile.X,
                this.originTile.Y,
                this.originFacingDirection);
        }
        catch
        {
            this.RestoreViewImmediately(reactivateReturnMenu: true);
            this.InvokeConfirmationIfNeeded();
        }
    }

    private void OnReturnedToOrigin()
    {
        if (this.restored)
            return;

        this.restored = true;
        Game1.player.viewingLocation.Value = this.originViewingLocation;
        Game1.displayHUD = this.originDisplayHud;
        Game1.viewportFreeze = this.originViewportFreeze;
        Game1.viewport.Location = this.originViewport;
        Game1.displayFarmer = this.originDisplayFarmer;

        try
        {
            this.InvokeConfirmationIfNeeded();
        }
        finally
        {
            Game1.activeClickableMenu = this.returnMenu;
        }
    }

    private void RestoreViewImmediately(bool reactivateReturnMenu)
    {
        if (this.restored)
        {
            if (reactivateReturnMenu)
                Game1.activeClickableMenu = this.returnMenu;
            return;
        }

        this.restored = true;
        this.viewReady = false;
        try
        {
            if (Game1.currentLocation != this.originLocation)
            {
                Game1.currentLocation?.cleanupBeforePlayerExit();
                Game1.currentLocation = this.originLocation;
                this.originLocation.resetForPlayerEntry();
            }
            Game1.player.viewingLocation.Value = this.originViewingLocation;
        }
        finally
        {
            Game1.displayHUD = this.originDisplayHud;
            Game1.viewportFreeze = this.originViewportFreeze;
            Game1.viewport.Location = this.originViewport;
            Game1.displayFarmer = this.originDisplayFarmer;
            if (reactivateReturnMenu)
                Game1.activeClickableMenu = this.returnMenu;
        }
    }

    private void InvokeConfirmationIfNeeded()
    {
        if (!this.invokeConfirmation)
            return;

        this.invokeConfirmation = false;
        this.onConfirm(this.confirmedArea.MinX, this.confirmedArea.MinY, this.confirmedArea.Size);
    }

    private RoutineAreaSelectionPreset GetSelection()
    {
        int cursorTileX = (Game1.viewport.X + Game1.getMouseX(false)) / TileSize;
        int cursorTileY = (Game1.viewport.Y + Game1.getMouseY(false)) / TileSize;
        int minX = Math.Clamp(cursorTileX - this.size / 2, 0, this.mapWidth - this.size);
        int minY = Math.Clamp(cursorTileY - this.size / 2, 0, this.mapHeight - this.size);
        return new RoutineAreaSelectionPreset(minX, minY, this.size);
    }

    private void ChangeSize(int delta)
    {
        int maximum = Math.Min(MaximumSize, Math.Min(this.mapWidth, this.mapHeight));
        int changed = Math.Clamp(this.size + Math.Sign(delta), MinimumSize, maximum);
        if (changed == this.size)
        {
            Game1.playSound("cancel");
            return;
        }

        this.size = changed;
        Game1.playSound("shwip");
    }

    private void ReflowButtons()
    {
        int availableWidth = Math.Max(1, Game1.uiViewport.Width - 32);
        int sizeButtonWidth = 54;
        int actionButtonWidth = Math.Clamp((availableWidth - sizeButtonWidth * 2 - ButtonGap * 3) / 2, 92, 180);
        int totalWidth = sizeButtonWidth * 2 + actionButtonWidth * 2 + ButtonGap * 3;
        if (totalWidth > availableWidth)
        {
            actionButtonWidth = Math.Max(1, (availableWidth - sizeButtonWidth * 2 - ButtonGap * 3) / 2);
            totalWidth = sizeButtonWidth * 2 + actionButtonWidth * 2 + ButtonGap * 3;
        }

        int x = (Game1.uiViewport.Width - totalWidth) / 2;
        int y = Math.Max(8, Game1.uiViewport.Height - ButtonHeight - 20);
        this.minusButton = new Rectangle(x, y, sizeButtonWidth, ButtonHeight);
        this.plusButton = new Rectangle(this.minusButton.Right + ButtonGap, y, sizeButtonWidth, ButtonHeight);
        this.confirmButton = new Rectangle(this.plusButton.Right + ButtonGap, y, actionButtonWidth, ButtonHeight);
        this.cancelButton = new Rectangle(this.confirmButton.Right + ButtonGap, y, actionButtonWidth, ButtonHeight);
    }

    private void DrawButton(SpriteBatch b, Rectangle bounds, string label, Color fill)
    {
        bool hovered = bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color color = hovered ? Color.Lerp(fill, Color.White, 0.24f) : fill;
        IClickableMenu.drawTextureBox(b, bounds.X, bounds.Y, bounds.Width, bounds.Height, color);
        this.DrawCenteredText(b, label, new Rectangle(bounds.X + 6, bounds.Y + 5, bounds.Width - 12, bounds.Height - 10), PanelText);
    }

    private void DrawCenteredText(SpriteBatch b, string text, Rectangle bounds, Color color)
    {
        Vector2 size = Game1.smallFont.MeasureString(text);
        float scale = Math.Min(1f, Math.Min(
            bounds.Width / Math.Max(1f, size.X),
            bounds.Height / Math.Max(1f, size.Y)));
        Vector2 position = new(
            bounds.Center.X - size.X * scale / 2f,
            bounds.Center.Y - size.Y * scale / 2f);
        Utility.drawTextWithShadow(b, text, Game1.smallFont, position, color, scale);
    }

    private string GetText(string key, object? tokens, string fallback)
    {
        string value = this.translate(key, tokens);
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, key, StringComparison.Ordinal)
            || string.Equals(value, $"{{{{{key}}}}}", StringComparison.Ordinal)
                ? fallback
                : value;
    }
}
