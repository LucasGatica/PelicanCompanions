using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace PelicanCompanions;

internal sealed partial class CompanionPanelMenu
{
    private static readonly CompanionRoutineActivity[] RoutineActivities =
    {
        CompanionRoutineActivity.Follow,
        CompanionRoutineActivity.Wait,
        CompanionRoutineActivity.VanillaRoutine,
        CompanionRoutineActivity.Water,
        CompanionRoutineActivity.Lumber,
        CompanionRoutineActivity.Mine,
        CompanionRoutineActivity.Clear,
        CompanionRoutineActivity.Deposit
    };

    private readonly Dictionary<CompanionOperationalProfileKey, RoutineDraftEntry> routineDrafts = new();
    private readonly Dictionary<CompanionOperationalProfileKey, string> routineFeedbackKeys = new();
    private readonly List<(Rectangle Bounds, int Hour)> routineHourButtons = new();
    private readonly List<(Rectangle Bounds, CompanionRoutineActivity Activity)> routineActivityButtons = new();
    private Rectangle routineEnabledButton;
    private Rectangle routineRepeatButton;
    private Rectangle routineCompletionButton;
    private Rectangle routineAreaFreeButton;
    private Rectangle routineAreaDelimitedButton;
    private Rectangle routineQuickShiftButton;
    private Rectangle routineSaveButton;

    private void ResetRoutineGeometry()
    {
        this.routineHourButtons.Clear();
        this.routineActivityButtons.Clear();
        this.routineEnabledButton = new Rectangle();
        this.routineRepeatButton = new Rectangle();
        this.routineCompletionButton = new Rectangle();
        this.routineAreaFreeButton = new Rectangle();
        this.routineAreaDelimitedButton = new Rectangle();
        this.routineQuickShiftButton = new Rectangle();
        this.routineSaveButton = new Rectangle();
    }

    private void AddRoutineFocusTargets(List<Rectangle> targets)
    {
        if (this.currentTab != PanelTab.Routine)
            return;

        if (this.routineEnabledButton.Width > 0)
            targets.Add(this.routineEnabledButton);
        if (this.routineRepeatButton.Width > 0)
            targets.Add(this.routineRepeatButton);
        if (this.routineCompletionButton.Width > 0)
            targets.Add(this.routineCompletionButton);
        targets.AddRange(this.routineActivityButtons.Select(button => button.Bounds));
        targets.AddRange(this.routineHourButtons.Select(button => button.Bounds));
        if (this.routineAreaFreeButton.Width > 0)
            targets.Add(this.routineAreaFreeButton);
        if (this.routineAreaDelimitedButton.Width > 0)
            targets.Add(this.routineAreaDelimitedButton);
        if (this.routineQuickShiftButton.Width > 0)
            targets.Add(this.routineQuickShiftButton);
        if (this.routineSaveButton.Width > 0)
            targets.Add(this.routineSaveButton);
    }

    private RoutineDraftEntry GetRoutineDraft(SquadMemberState member)
    {
        CompanionOperationalProfileKey key = CompanionEquipmentPolicy.CreateKey(member.OwnerId, member.NpcName);
        if (this.routineDrafts.TryGetValue(key, out RoutineDraftEntry? cached))
        {
            if (!string.IsNullOrWhiteSpace(cached.PendingPreviousStateToken))
            {
                CompanionRoutineState current = this.getRoutine(member) ?? new CompanionRoutineState();
                string currentToken = CompanionRoutinePolicy.CreateStateToken(current);
                bool stillWaitingForSnapshot = string.Equals(
                        currentToken,
                        cached.PendingPreviousStateToken,
                        StringComparison.Ordinal)
                    && unchecked((uint)(Game1.ticks - cached.PendingSinceTick)) <= 300;
                if (!stillWaitingForSnapshot)
                {
                    cached = CreateRoutineDraftEntry(current, cached.SelectedPaintActivity);
                    this.routineDrafts[key] = cached;
                }
            }

            return cached;
        }

        CompanionRoutineState source = this.getRoutine(member) ?? new CompanionRoutineState();
        RoutineDraftEntry created = CreateRoutineDraftEntry(source);
        this.routineDrafts[key] = created;
        return created;
    }

    private static RoutineDraftEntry CreateRoutineDraftEntry(
        CompanionRoutineState source,
        CompanionRoutineActivity? selectedPaintActivity = null)
    {
        CompanionRoutineState draft = CompanionOperationsStateCopy.CloneRoutine(source);
        draft.Hours = CompanionRoutinePolicy.NormalizeHours(draft.Hours).ToList();
        draft.AreaPresets = CompanionRoutinePolicy.NormalizeAreaPresets(draft.AreaPresets).ToList();
        CompanionRoutineActivity selected = selectedPaintActivity is CompanionRoutineActivity requested
            && Enum.IsDefined(requested)
                ? requested
                : CompanionRoutinePolicy.GetPreferredEditorActivity(draft, Game1.timeOfDay);
        return new RoutineDraftEntry(
            draft,
            CompanionRoutinePolicy.CreateStateToken(source),
            selected);
    }

    private bool HandleRoutineLeftClick(SquadMemberState member, int x, int y)
    {
        RoutineDraftEntry entry = this.GetRoutineDraft(member);
        CompanionRoutineState draft = entry.State;
        CompanionOperationalProfileKey key = CompanionEquipmentPolicy.CreateKey(member.OwnerId, member.NpcName);

        if (this.routineEnabledButton.Contains(x, y))
        {
            draft.Enabled = !draft.Enabled;
            this.routineFeedbackKeys.Remove(key);
            Game1.playSound("drumkit6");
            return true;
        }
        if (this.routineRepeatButton.Contains(x, y))
        {
            draft.RepeatDaily = !draft.RepeatDaily;
            this.routineFeedbackKeys.Remove(key);
            Game1.playSound("drumkit6");
            return true;
        }
        if (this.routineCompletionButton.Contains(x, y))
        {
            int values = Enum.GetValues<CompanionRoutineCompletionBehavior>().Length;
            draft.CompletionBehavior = (CompanionRoutineCompletionBehavior)(((int)draft.CompletionBehavior + 1) % values);
            this.routineFeedbackKeys.Remove(key);
            Game1.playSound("smallSelect");
            return true;
        }

        if (this.routineAreaFreeButton.Contains(x, y))
        {
            if (!CompanionRoutinePolicy.TryGetWorkSpecialty(
                    entry.SelectedPaintActivity,
                    out CompanionWorkSpecialty specialty)
                || Game1.currentLocation?.Map is null
                || Game1.currentLocation.Map.Layers.Count == 0)
            {
                Game1.playSound("cancel");
                return true;
            }

            string areaLocationName = Game1.currentLocation.NameOrUniqueName;
            CompanionRoutinePolicy.UpsertAreaPreset(draft, new CompanionRoutineAreaPreset
            {
                Specialty = specialty,
                RegionKind = CompanionWorkRegionKind.FarmWide,
                LocationName = areaLocationName
            });
            this.routineFeedbackKeys.Remove(key);
            Game1.playSound("drumkit6");
            return true;
        }

        if (this.routineAreaDelimitedButton.Contains(x, y))
        {
            if (!CompanionRoutinePolicy.TryGetWorkSpecialty(
                    entry.SelectedPaintActivity,
                    out CompanionWorkSpecialty specialty)
                || Game1.currentLocation?.Map is null
                || Game1.currentLocation.Map.Layers.Count == 0
                || Math.Min(
                    Game1.currentLocation.Map.Layers[0].LayerWidth,
                    Game1.currentLocation.Map.Layers[0].LayerHeight)
                    < CompanionWorkAreaPolicy.MinimumSquareSize)
            {
                Game1.playSound("cancel");
                return true;
            }

            GameLocation areaLocation = Game1.currentLocation;
            string areaLocationName = areaLocation.NameOrUniqueName;
            CompanionRoutineAreaPreset? initialArea = CompanionRoutinePolicy.GetAreaPreset(draft, specialty);
            bool initialAreaBelongsToCurrentMap = initialArea is not null
                && string.Equals(
                    initialArea.LocationName,
                    areaLocationName,
                    StringComparison.Ordinal);
            RoutineAreaSelectionPreset? initialSelection = initialAreaBelongsToCurrentMap
                ? initialArea!.RegionKind switch
                {
                    CompanionWorkRegionKind.DelimitedSquare => new RoutineAreaSelectionPreset(
                        initialArea.MinX,
                        initialArea.MinY,
                        initialArea.Size),
                    CompanionWorkRegionKind.Circle => new RoutineAreaSelectionPreset(
                        initialArea.CenterX - initialArea.Radius,
                        initialArea.CenterY - initialArea.Radius,
                        initialArea.Radius * 2 + 1),
                    _ => null
                }
                : null;
            Game1.activeClickableMenu = new RoutineAreaSelectionMenu(
                this,
                areaLocation,
                initialSelection,
                this.translate,
                (minX, minY, size) =>
                {
                    CompanionRoutinePolicy.UpsertAreaPreset(draft, new CompanionRoutineAreaPreset
                    {
                        Specialty = specialty,
                        RegionKind = CompanionWorkRegionKind.DelimitedSquare,
                        LocationName = areaLocationName,
                        MinX = minX,
                        MinY = minY,
                        Size = size
                    });
                    this.routineFeedbackKeys.Remove(key);
                });
            return true;
        }

        foreach ((Rectangle bounds, CompanionRoutineActivity activity) in this.routineActivityButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            entry.SelectedPaintActivity = activity;
            Game1.playSound("smallSelect");
            return true;
        }

        foreach ((Rectangle bounds, int hour) in this.routineHourButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            CompanionRoutinePolicy.PaintHour(draft, hour, entry.SelectedPaintActivity);
            this.routineFeedbackKeys.Remove(key);
            Game1.playSound("drumkit6");
            return true;
        }

        if (this.routineQuickShiftButton.Contains(x, y))
        {
            CompanionRoutineActivity workActivity = entry.SelectedPaintActivity;
            if (!CompanionRoutinePolicy.TryGetWorkSpecialty(workActivity, out _))
            {
                workActivity = CompanionRoutineActivity.Clear;
                entry.SelectedPaintActivity = workActivity;
            }
            CompanionRoutinePolicy.ApplyWorkUntilSixPm(draft, workActivity);
            this.routineFeedbackKeys.Remove(key);
            Game1.playSound("questcomplete");
            return true;
        }

        if (this.routineSaveButton.Contains(x, y))
        {
            string previousToken = entry.ExpectedStateToken;
            bool saved = this.saveRoutine(member, CompanionOperationsStateCopy.CloneRoutine(draft), entry.ExpectedStateToken);
            this.routineFeedbackKeys[key] = saved
                ? "companion.routine.save_sent"
                : "companion.routine.save_failed";
            if (saved)
            {
                draft.Revision = draft.Revision == long.MaxValue ? 0 : Math.Max(0, draft.Revision) + 1;
                entry.ExpectedStateToken = CompanionRoutinePolicy.CreateStateToken(draft);
                entry.PendingPreviousStateToken = previousToken;
                entry.PendingSinceTick = Game1.ticks;
            }
            Game1.playSound(saved ? "coin" : "cancel");
            return true;
        }

        return false;
    }

    private void HandleRoutineHover(SquadMemberState member, int x, int y)
    {
        RoutineDraftEntry entry = this.GetRoutineDraft(member);
        CompanionRoutineState draft = entry.State;
        if (this.routineEnabledButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.routine.enabled_hint", null);
            return;
        }
        if (this.routineRepeatButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.routine.repeat_hint", null);
            return;
        }
        if (this.routineCompletionButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.routine.completion_hint", null);
            return;
        }
        if (this.routineAreaFreeButton.Contains(x, y))
        {
            this.hoverText = CompanionRoutinePolicy.TryGetWorkSpecialty(entry.SelectedPaintActivity, out _)
                ? this.translate("companion.routine.scope.free_hint", new
                {
                    activity = this.GetRoutineActivityLabel(entry.SelectedPaintActivity, shortLabel: false),
                    location = Game1.currentLocation?.NameOrUniqueName ?? "?"
                })
                : this.translate("companion.routine.scope.select_work_hint", null);
            return;
        }
        if (this.routineAreaDelimitedButton.Contains(x, y))
        {
            this.hoverText = CompanionRoutinePolicy.TryGetWorkSpecialty(entry.SelectedPaintActivity, out _)
                ? this.translate("companion.routine.scope.delimited_hint", new
                {
                    activity = this.GetRoutineActivityLabel(entry.SelectedPaintActivity, shortLabel: false),
                    location = Game1.currentLocation?.NameOrUniqueName ?? "?"
                })
                : this.translate("companion.routine.scope.select_work_hint", null);
            return;
        }
        if (this.routineQuickShiftButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.routine.quick_shift_hint", null);
            return;
        }
        if (this.routineSaveButton.Contains(x, y))
        {
            this.hoverText = this.translate("companion.routine.save_hint", null);
            return;
        }

        foreach ((Rectangle bounds, CompanionRoutineActivity activity) in this.routineActivityButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            if (CompanionRoutinePolicy.TryGetWorkSpecialty(activity, out CompanionWorkSpecialty specialty)
                && CompanionRoutinePolicy.GetAreaPreset(draft, specialty) is null)
            {
                this.hoverText = this.translate("companion.routine.area_missing_hint", new
                {
                    activity = this.GetRoutineActivityLabel(activity, shortLabel: false)
                });
            }
            else
            {
                this.hoverText = this.translate("companion.routine.paint_hint", new
                {
                    activity = this.GetRoutineActivityLabel(activity, shortLabel: false)
                });
            }
            return;
        }

        foreach ((Rectangle bounds, int hour) in this.routineHourButtons)
        {
            if (!bounds.Contains(x, y))
                continue;
            CompanionRoutineActivity activity = draft.Hours.First(value => value.Hour == hour).Activity;
            this.hoverText = this.translate("companion.routine.hour_hint", new
            {
                hour = FormatRoutineHour(hour),
                activity = this.GetRoutineActivityLabel(activity, shortLabel: false)
            });
            return;
        }
    }

    private void DrawRoutine(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        if (area.Width <= 1 || area.Height <= 1)
            return;

        RoutineDraftEntry entry = this.GetRoutineDraft(member);
        CompanionRoutineState draft = entry.State;
        CompanionRoutineActivity selectedPaintActivity = entry.SelectedPaintActivity;
        if (area.Height < 180)
        {
            this.DrawUltraCompactRoutine(b, draft, selectedPaintActivity, area);
            return;
        }

        int gap = area.Width >= 700 ? 8 : area.Width >= 440 ? 6 : 3;
        int controlHeight = Math.Clamp(area.Height / 10, 23, 32);
        int controlWidth = Math.Max(1, (area.Width - gap * 2) / 3);
        this.routineEnabledButton = new Rectangle(area.X, area.Y, controlWidth, controlHeight);
        this.routineRepeatButton = new Rectangle(this.routineEnabledButton.Right + gap, area.Y, controlWidth, controlHeight);
        this.routineCompletionButton = new Rectangle(
            this.routineRepeatButton.Right + gap,
            area.Y,
            Math.Max(1, area.Right - this.routineRepeatButton.Right - gap),
            controlHeight);
        this.DrawButton(
            b,
            this.routineEnabledButton,
            this.translate(draft.Enabled ? "companion.routine.enabled" : "companion.routine.disabled", null),
            draft.Enabled,
            danger: false);
        this.DrawButton(
            b,
            this.routineRepeatButton,
            this.translate(draft.RepeatDaily ? "companion.routine.repeat_daily" : "companion.routine.once", null),
            draft.RepeatDaily,
            danger: false);
        this.DrawButton(
            b,
            this.routineCompletionButton,
            this.translate("companion.routine.completion", new
            {
                activity = this.GetRoutineCompletionLabel(draft.CompletionBehavior)
            }),
            active: false,
            danger: false);

        bool compactPalette = area.Width < 600;
        int paletteColumns = compactPalette ? 4 : RoutineActivities.Length;
        int paletteRows = (int)Math.Ceiling(RoutineActivities.Length / (double)paletteColumns);
        int paletteHeight = Math.Clamp(area.Height / (compactPalette ? 13 : 11), 21, 29);
        int paletteTop = this.routineEnabledButton.Bottom + gap;
        int paletteWidth = Math.Max(1, (area.Width - gap * (paletteColumns - 1)) / paletteColumns);
        for (int index = 0; index < RoutineActivities.Length; index++)
        {
            int row = index / paletteColumns;
            int column = index % paletteColumns;
            int x = area.X + column * (paletteWidth + gap);
            int width = column == paletteColumns - 1 ? Math.Max(1, area.Right - x) : paletteWidth;
            Rectangle button = new(x, paletteTop + row * (paletteHeight + gap), width, paletteHeight);
            CompanionRoutineActivity activity = RoutineActivities[index];
            bool hasRequiredArea = !CompanionRoutinePolicy.TryGetWorkSpecialty(activity, out CompanionWorkSpecialty specialty)
                || CompanionRoutinePolicy.GetAreaPreset(draft, specialty) is not null;
            string label = this.GetRoutineActivityLabel(
                activity,
                shortLabel: paletteColumns == RoutineActivities.Length || button.Width < 112);
            if (!hasRequiredArea)
                label += " !";
            this.DrawRoutineActivityButton(b, button, label, activity == selectedPaintActivity, activity, hasRequiredArea);
        }

        int actionHeight = Math.Clamp(area.Height / 10, 23, 32);
        int actionTop = area.Bottom - actionHeight;
        int actionWidth = Math.Max(1, (area.Width - gap * 3) / 4);
        this.routineAreaFreeButton = new Rectangle(area.X, actionTop, actionWidth, actionHeight);
        this.routineAreaDelimitedButton = new Rectangle(
            this.routineAreaFreeButton.Right + gap,
            actionTop,
            actionWidth,
            actionHeight);
        this.routineQuickShiftButton = new Rectangle(
            this.routineAreaDelimitedButton.Right + gap,
            actionTop,
            actionWidth,
            actionHeight);
        this.routineSaveButton = new Rectangle(
            this.routineQuickShiftButton.Right + gap,
            actionTop,
            Math.Max(1, area.Right - this.routineQuickShiftButton.Right - gap),
            actionHeight);
        this.DrawRoutineAreaModeButtons(b, draft, selectedPaintActivity);
        this.DrawButton(b, this.routineQuickShiftButton, this.translate("companion.routine.quick_shift", null), false, danger: false);
        this.DrawButton(b, this.routineSaveButton, this.translate("companion.routine.save", null), true, danger: false);

        int gridTop = paletteTop + paletteRows * paletteHeight + Math.Max(0, paletteRows - 1) * gap + gap;
        int feedbackHeight = area.Height >= 260 ? 18 : 0;
        int gridBottom = Math.Max(gridTop + 1, actionTop - gap - feedbackHeight);
        Rectangle grid = new(area.X, gridTop, area.Width, Math.Max(1, gridBottom - gridTop));
        this.DrawRoutineGrid(b, draft, selectedPaintActivity, grid, gap);

        CompanionOperationalProfileKey key = CompanionEquipmentPolicy.CreateKey(member.OwnerId, member.NpcName);
        if (feedbackHeight > 0 && this.routineFeedbackKeys.TryGetValue(key, out string? feedbackKey))
        {
            DrawPanelText(
                b,
                FitText(this.translate(feedbackKey, null), Game1.tinyFont, area.Width, PanelMetaTextScale),
                Game1.tinyFont,
                new Vector2(area.X + 2, actionTop - feedbackHeight),
                feedbackKey.EndsWith("failed", StringComparison.Ordinal) ? DangerColor : AccentGreen,
                PanelMetaTextScale);
        }
    }

    private void DrawUltraCompactRoutine(
        SpriteBatch b,
        CompanionRoutineState draft,
        CompanionRoutineActivity selectedPaintActivity,
        Rectangle area)
    {
        const int gap = 2;

        // Use fixed, sequential bands so every control remains inside the body.
        // The 426x240 viewport yields 82px here; any excess goes to the grid.
        int availableBandHeight = Math.Max(4, area.Height - gap * 3);
        int maximumFixedHeight = Math.Max(3, availableBandHeight - 1);
        int desiredControlHeight = Math.Clamp(area.Height / 6, 10, 18);
        int desiredPaletteHeight = Math.Clamp(area.Height / 7, 10, 17);
        int desiredActionHeight = Math.Clamp(area.Height / 6, 10, 18);
        int desiredFixedHeight = desiredControlHeight + desiredPaletteHeight + desiredActionHeight;
        int fixedHeight = Math.Min(maximumFixedHeight, desiredFixedHeight);
        int controlHeight = Math.Min(desiredControlHeight, Math.Max(1, fixedHeight - 2));
        int remainingFixedHeight = fixedHeight - controlHeight;
        int paletteHeight = Math.Min(desiredPaletteHeight, Math.Max(1, remainingFixedHeight - 1));
        int actionHeight = Math.Max(1, remainingFixedHeight - paletteHeight);
        int gridHeight = Math.Max(1, availableBandHeight - controlHeight - paletteHeight - actionHeight);

        int controlWidth = Math.Max(1, (area.Width - gap * 2) / 3);
        this.routineEnabledButton = new Rectangle(area.X, area.Y, controlWidth, controlHeight);
        this.routineRepeatButton = new Rectangle(this.routineEnabledButton.Right + gap, area.Y, controlWidth, controlHeight);
        this.routineCompletionButton = new Rectangle(
            this.routineRepeatButton.Right + gap,
            area.Y,
            Math.Max(1, area.Right - this.routineRepeatButton.Right - gap),
            controlHeight);
        this.DrawButton(
            b,
            this.routineEnabledButton,
            this.translate(draft.Enabled ? "companion.routine.enabled" : "companion.routine.disabled", null),
            draft.Enabled,
            danger: false);
        this.DrawButton(
            b,
            this.routineRepeatButton,
            this.translate(draft.RepeatDaily ? "companion.routine.repeat_daily" : "companion.routine.once", null),
            draft.RepeatDaily,
            danger: false);
        this.DrawButton(
            b,
            this.routineCompletionButton,
            this.GetRoutineCompletionLabel(draft.CompletionBehavior),
            active: false,
            danger: false);

        int paletteTop = this.routineEnabledButton.Bottom + gap;
        int paletteWidth = Math.Max(1, (area.Width - gap * (RoutineActivities.Length - 1)) / RoutineActivities.Length);
        for (int index = 0; index < RoutineActivities.Length; index++)
        {
            int x = area.X + index * (paletteWidth + gap);
            int width = index == RoutineActivities.Length - 1 ? Math.Max(1, area.Right - x) : paletteWidth;
            Rectangle button = new(x, paletteTop, width, paletteHeight);
            CompanionRoutineActivity activity = RoutineActivities[index];
            bool hasRequiredArea = !CompanionRoutinePolicy.TryGetWorkSpecialty(activity, out CompanionWorkSpecialty specialty)
                || CompanionRoutinePolicy.GetAreaPreset(draft, specialty) is not null;
            string label = this.GetRoutineActivityLabel(activity, shortLabel: true);
            if (!hasRequiredArea)
                label += "!";
            this.DrawRoutineActivityButton(
                b,
                button,
                label,
                activity == selectedPaintActivity,
                activity,
                hasRequiredArea);
        }

        int gridTop = paletteTop + paletteHeight + gap;
        int actionTop = gridTop + gridHeight + gap;
        int actionWidth = Math.Max(1, (area.Width - gap * 3) / 4);
        this.routineAreaFreeButton = new Rectangle(area.X, actionTop, actionWidth, actionHeight);
        this.routineAreaDelimitedButton = new Rectangle(
            this.routineAreaFreeButton.Right + gap,
            actionTop,
            actionWidth,
            actionHeight);
        this.routineQuickShiftButton = new Rectangle(
            this.routineAreaDelimitedButton.Right + gap,
            actionTop,
            actionWidth,
            actionHeight);
        this.routineSaveButton = new Rectangle(
            this.routineQuickShiftButton.Right + gap,
            actionTop,
            Math.Max(1, area.Right - this.routineQuickShiftButton.Right - gap),
            actionHeight);

        Rectangle grid = new(area.X, gridTop, area.Width, gridHeight);
        this.DrawRoutineGrid(
            b,
            draft,
            selectedPaintActivity,
            grid,
            gap,
            columnsOverride: area.Width >= 240 ? 10 : 5);

        this.DrawRoutineAreaModeButtons(b, draft, selectedPaintActivity);
        this.DrawButton(b, this.routineQuickShiftButton, this.translate("companion.routine.quick_shift", null), false, danger: false);
        this.DrawButton(b, this.routineSaveButton, this.translate("companion.routine.save", null), true, danger: false);
    }

    private void DrawRoutineAreaModeButtons(
        SpriteBatch b,
        CompanionRoutineState draft,
        CompanionRoutineActivity selectedPaintActivity)
    {
        bool workSelected = CompanionRoutinePolicy.TryGetWorkSpecialty(
            selectedPaintActivity,
            out CompanionWorkSpecialty specialty);
        CompanionRoutineAreaPreset? preset = workSelected
            ? CompanionRoutinePolicy.GetAreaPreset(draft, specialty)
            : null;
        bool free = preset?.RegionKind == CompanionWorkRegionKind.FarmWide;
        bool delimited = preset is not null && !free;
        bool shortLabel = this.routineAreaFreeButton.Width < 126;
        object? labelTokens = workSelected
            ? new
            {
                activity = this.GetRoutineActivityLabel(selectedPaintActivity, shortLabel: true)
            }
            : null;
        this.DrawButton(
            b,
            this.routineAreaFreeButton,
            this.translate(
                shortLabel || !workSelected
                    ? "companion.routine.scope.free.short"
                    : "companion.routine.scope.free_for",
                labelTokens),
            free,
            danger: false);
        this.DrawButton(
            b,
            this.routineAreaDelimitedButton,
            this.translate(
                shortLabel || !workSelected
                    ? "companion.routine.scope.delimited.short"
                    : "companion.routine.scope.delimited_for",
                labelTokens),
            delimited,
            danger: false);

        if (workSelected)
            return;

        Color disabled = Color.Black * 0.18f;
        b.Draw(Game1.staminaRect, this.routineAreaFreeButton, disabled);
        b.Draw(Game1.staminaRect, this.routineAreaDelimitedButton, disabled);
    }

    private void DrawRoutineGrid(
        SpriteBatch b,
        CompanionRoutineState draft,
        CompanionRoutineActivity selectedPaintActivity,
        Rectangle grid,
        int gap,
        int? columnsOverride = null)
    {
        // A tall routine body reads better as four compact rows instead of two
        // oversized strips. Keep the 10-column layout only when vertical room is
        // scarce (notably the 426x240 compact viewport).
        int columns = columnsOverride ?? (grid.Height >= 160 ? 5 : grid.Width >= 380 ? 10 : 5);
        int rows = (int)Math.Ceiling(CompanionRoutinePolicy.HourCount / (double)columns);
        int cellWidth = Math.Max(1, (grid.Width - gap * (columns - 1)) / columns);
        int cellHeight = Math.Max(1, (grid.Height - gap * (rows - 1)) / rows);
        IReadOnlyList<CompanionRoutineHourState> hours = CompanionRoutinePolicy.NormalizeHours(draft.Hours);
        for (int index = 0; index < hours.Count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            int x = grid.X + column * (cellWidth + gap);
            int y = grid.Y + row * (cellHeight + gap);
            int width = column == columns - 1 ? Math.Max(1, grid.Right - x) : cellWidth;
            int height = row == rows - 1 ? Math.Max(1, grid.Bottom - y) : cellHeight;
            Rectangle cell = new(x, y, width, height);
            CompanionRoutineHourState hour = hours[index];
            Color accent = GetRoutineActivityColor(hour.Activity);
            Color fill = Color.Lerp(new Color(255, 242, 205), accent, 0.12f);
            if (cell.Contains(Game1.getMouseX(), Game1.getMouseY()))
                fill = Color.Lerp(fill, Color.White, 0.25f);
            this.DrawFlatPanel(b, cell, fill, accent, hour.Activity == selectedPaintActivity ? 2 : 1);
            string hourText = FormatRoutineHour(hour.Hour);
            string activityText = this.GetRoutineActivityLabel(hour.Activity, shortLabel: true);
            if (cell.Height >= 44 && cell.Width >= 52)
            {
                DrawPanelText(
                    b,
                    FitText(hourText, Game1.tinyFont, Math.Max(1, cell.Width - 12), PanelNumericTextScale),
                    Game1.tinyFont,
                    new Vector2(cell.X + 7, cell.Y + 5),
                    TextColor,
                    PanelNumericTextScale);
                Rectangle activityBounds = new(
                    cell.X + 5,
                    cell.Y + 18,
                    Math.Max(1, cell.Width - 10),
                    Math.Max(1, cell.Height - 23));
                DrawCenteredPanelText(
                    b,
                    activityText,
                    Game1.tinyFont,
                    activityBounds,
                    TextColor,
                    PanelTextScale,
                    4,
                    3,
                    minimumScale: 0.44f);
            }
            else if (cell.Height >= 29)
            {
                int hourHeight = Math.Max(12, cell.Height / 2);
                Rectangle hourBounds = new(cell.X + 2, cell.Y + 1, Math.Max(1, cell.Width - 4), hourHeight);
                Rectangle compactActivityBounds = new(
                    cell.X + 2,
                    hourBounds.Bottom - 1,
                    Math.Max(1, cell.Width - 4),
                    Math.Max(1, cell.Bottom - hourBounds.Bottom));
                DrawCenteredPanelText(
                    b,
                    hourText,
                    Game1.tinyFont,
                    hourBounds,
                    TextColor,
                    PanelCompactNumericTextScale,
                    2,
                    1,
                    minimumScale: 0.54f);
                DrawCenteredPanelText(
                    b,
                    activityText,
                    Game1.tinyFont,
                    compactActivityBounds,
                    MutedTextColor,
                    PanelCompactTextScale,
                    2,
                    1,
                    minimumScale: 0.42f);
            }
            else
            {
                DrawCenteredPanelText(
                    b,
                    hourText,
                    Game1.tinyFont,
                    cell,
                    TextColor,
                    PanelCompactNumericTextScale,
                    2,
                    1,
                    minimumScale: 0.54f);
            }
            this.routineHourButtons.Add((cell, hour.Hour));
        }
    }

    private void DrawRoutineActivityButton(
        SpriteBatch b,
        Rectangle bounds,
        string label,
        bool selected,
        CompanionRoutineActivity activity,
        bool hasRequiredArea)
    {
        Color accent = GetRoutineActivityColor(activity);
        Color fill = selected
            ? Color.Lerp(ButtonActive, accent, 0.17f)
            : bounds.Contains(Game1.getMouseX(), Game1.getMouseY())
                ? RowHoverColor
                : ButtonIdle;
        if (!hasRequiredArea)
            fill = Color.Lerp(fill, new Color(220, 199, 181), 0.36f);
        this.DrawFlatPanel(b, bounds, fill, selected ? accent : SurfaceBorder, selected ? 2 : 1);
        DrawCenteredPanelText(b, label, Game1.tinyFont, bounds, TextColor, PanelCompactTextScale, 4, 3);
        this.routineActivityButtons.Add((bounds, activity));
    }

    private string GetRoutineActivityLabel(CompanionRoutineActivity activity, bool shortLabel)
    {
        string suffix = activity switch
        {
            CompanionRoutineActivity.Follow => "follow",
            CompanionRoutineActivity.Wait => "wait",
            CompanionRoutineActivity.VanillaRoutine => "original",
            CompanionRoutineActivity.Water => "water",
            CompanionRoutineActivity.Lumber => "wood",
            CompanionRoutineActivity.Mine => "mine",
            CompanionRoutineActivity.Clear => "clear",
            CompanionRoutineActivity.Deposit => "deposit",
            _ => "follow"
        };
        return this.translate($"companion.routine.activity.{suffix}{(shortLabel ? ".short" : "")}", null);
    }

    private string GetRoutineCompletionLabel(CompanionRoutineCompletionBehavior behavior)
    {
        return this.GetRoutineActivityLabel(
            CompanionRoutinePolicy.GetCompletionActivity(behavior),
            shortLabel: true);
    }

    private static string FormatRoutineHour(int hour)
    {
        return $"{hour % 24:00}h";
    }

    private static Color GetRoutineActivityColor(CompanionRoutineActivity activity)
    {
        return activity switch
        {
            CompanionRoutineActivity.Follow => AccentGreen,
            CompanionRoutineActivity.Wait => new Color(126, 103, 84),
            CompanionRoutineActivity.VanillaRoutine => AccentBlue,
            CompanionRoutineActivity.Water => new Color(65, 147, 205),
            CompanionRoutineActivity.Lumber => new Color(83, 132, 73),
            CompanionRoutineActivity.Mine => new Color(112, 105, 122),
            CompanionRoutineActivity.Clear => AccentGold,
            CompanionRoutineActivity.Deposit => new Color(163, 101, 58),
            _ => SurfaceBorder
        };
    }

    private sealed class RoutineDraftEntry
    {
        public RoutineDraftEntry(
            CompanionRoutineState state,
            string expectedStateToken,
            CompanionRoutineActivity selectedPaintActivity)
        {
            this.State = state;
            this.ExpectedStateToken = expectedStateToken;
            this.SelectedPaintActivity = selectedPaintActivity;
        }

        public CompanionRoutineState State { get; }
        public string ExpectedStateToken { get; set; }
        public CompanionRoutineActivity SelectedPaintActivity { get; set; }
        public string PendingPreviousStateToken { get; set; } = "";
        public int PendingSinceTick { get; set; }
    }
}
