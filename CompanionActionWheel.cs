using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace PelicanCompanions;

internal enum CompanionActionWheelTone
{
    Neutral,
    Positive,
    Warning,
    Danger,
    Follow,
    Profile
}

internal sealed record CompanionActionWheelOption(
    string Label,
    CompanionActionWheelTone Tone,
    Action Activate,
    string? HoverText = null);

internal sealed record CompanionActionWheelModel(
    string Title,
    string Hint,
    IReadOnlyList<CompanionActionWheelOption> Options,
    Func<bool> IsValid,
    int PinnedOptionCount = 0);

internal enum CompanionActionWheelActivationResult
{
    None,
    Cancelled,
    Activated,
    PageChanged
}

internal enum CompanionActionWheelSlotKind
{
    Option,
    PreviousPage,
    NextPage,
    Empty
}

internal readonly record struct CompanionActionWheelSlot(
    CompanionActionWheelSlotKind Kind,
    int OptionIndex = -1);

internal readonly record struct CompanionActionWheelPageLayout(
    int PageIndex,
    int PageCount,
    CompanionActionWheelSlot[] Slots);

/// <summary>Pure page-to-sector mapping for the action wheel.</summary>
internal static class CompanionActionWheelPagination
{
    public const int PageSize = 3;
    public const int MaximumVisibleSlots = 6;

    public static CompanionActionWheelPageLayout Create(
        int optionCount,
        int pinnedOptionCount,
        int requestedPageIndex)
    {
        if (optionCount < 1)
            throw new ArgumentOutOfRangeException(nameof(optionCount));
        if (pinnedOptionCount < 0
            || pinnedOptionCount > MaximumVisibleSlots - 3
            || pinnedOptionCount > optionCount)
            throw new ArgumentOutOfRangeException(nameof(pinnedOptionCount));

        int pagedOptionCount = optionCount - pinnedOptionCount;
        if (pinnedOptionCount == 0)
        {
            if (optionCount > MaximumVisibleSlots)
                throw new ArgumentOutOfRangeException(nameof(optionCount));

            return new CompanionActionWheelPageLayout(
                0,
                1,
                Enumerable.Range(0, optionCount)
                    .Select(index => new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, index))
                    .ToArray());
        }

        int unpagedCapacity = MaximumVisibleSlots - pinnedOptionCount;
        if (pagedOptionCount <= unpagedCapacity)
        {
            return new CompanionActionWheelPageLayout(
                0,
                1,
                Enumerable.Range(0, optionCount)
                    .Select(index => new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, index))
                    .ToArray());
        }

        int pageSize = MaximumVisibleSlots - pinnedOptionCount - 2;
        int pageCount = ((pagedOptionCount - 1) / pageSize) + 1;
        int pageIndex = NormalizePageIndex(requestedPageIndex, pageCount);
        CompanionActionWheelSlot[] slots = Enumerable.Repeat(
                new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Empty),
                MaximumVisibleSlots)
            .ToArray();
        for (int pinnedIndex = 0; pinnedIndex < pinnedOptionCount; pinnedIndex++)
            slots[pinnedIndex] = new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, pinnedIndex);
        slots[pinnedOptionCount] = new CompanionActionWheelSlot(CompanionActionWheelSlotKind.NextPage);
        slots[^1] = new CompanionActionWheelSlot(CompanionActionWheelSlotKind.PreviousPage);

        int sourceStart = pinnedOptionCount + pageIndex * pageSize;
        for (int pageOffset = 0; pageOffset < pageSize; pageOffset++)
        {
            int optionIndex = sourceStart + pageOffset;
            if (optionIndex < optionCount)
                slots[pinnedOptionCount + 1 + pageOffset] = new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, optionIndex);
        }

        return new CompanionActionWheelPageLayout(pageIndex, pageCount, slots);
    }

    private static int NormalizePageIndex(int pageIndex, int pageCount)
    {
        int normalized = pageIndex % pageCount;
        return normalized < 0 ? normalized + pageCount : normalized;
    }
}

/// <summary>Pure spatial focus selection shared by keyboard, D-pad, and sticks.</summary>
internal static class CompanionActionWheelNavigation
{
    public static int? MoveFocus(
        IReadOnlyList<CompanionActionWheelSlot> slots,
        int? currentIndex,
        float directionX,
        float directionY,
        float firstSegmentCenterAngle)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0
            || !float.IsFinite(directionX)
            || !float.IsFinite(directionY)
            || !float.IsFinite(firstSegmentCenterAngle))
        {
            return currentIndex;
        }

        float directionLength = MathF.Sqrt(directionX * directionX + directionY * directionY);
        if (!float.IsFinite(directionLength) || directionLength < 0.001f)
            return currentIndex;

        directionX /= directionLength;
        directionY /= directionLength;
        float segmentAngle = MathF.PI * 2f / slots.Count;

        bool currentIsValid = currentIndex is int selected
            && selected >= 0
            && selected < slots.Count
            && IsFocusable(slots[selected]);
        if (!currentIsValid)
        {
            int? closest = null;
            float bestAlignment = float.NegativeInfinity;
            for (int index = 0; index < slots.Count; index++)
            {
                if (!IsFocusable(slots[index]))
                    continue;

                float angle = firstSegmentCenterAngle + index * segmentAngle;
                float alignment = MathF.Cos(angle) * directionX + MathF.Sin(angle) * directionY;
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    closest = index;
                }
            }

            return closest;
        }

        int current = currentIndex!.Value;
        float currentAngle = firstSegmentCenterAngle + current * segmentAngle;
        float currentX = MathF.Cos(currentAngle);
        float currentY = MathF.Sin(currentAngle);
        int bestIndex = -1;
        float bestScore = float.PositiveInfinity;
        for (int index = 0; index < slots.Count; index++)
        {
            if (index == current || !IsFocusable(slots[index]))
                continue;

            float candidateAngle = firstSegmentCenterAngle + index * segmentAngle;
            float deltaX = MathF.Cos(candidateAngle) - currentX;
            float deltaY = MathF.Sin(candidateAngle) - currentY;
            float forward = deltaX * directionX + deltaY * directionY;
            if (forward <= 0.001f)
                continue;

            float lateral = MathF.Abs(deltaX * -directionY + deltaY * directionX);
            float score = forward + lateral * 2.75f;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex >= 0 ? bestIndex : current;
    }

    public static bool IsFocusable(CompanionActionWheelSlot slot)
    {
        return slot.Kind != CompanionActionWheelSlotKind.Empty;
    }
}

/// <summary>A mouse-centered radial menu whose sectors depend on the hovered target.</summary>
internal sealed class CompanionActionWheel
{
    private const float InnerRadius = 43f;
    private const float OuterRadius = 132f;
    private const float LabelRadius = 89f;
    private const float FiveOptionLabelRadius = 96f;
    private const float FirstSegmentCenterAngle = -MathHelper.PiOver2;
    private const float SectorGap = 0.035f;
    private const float SectorStep = 0.035f;
    private const float BandThickness = 4.4f;
    private const int HorizontalMargin = 18;
    private const int NameSpace = 48;
    private const int HintSpace = 36;
    private const int MaximumOptions = 6;

    private static readonly Color NeutralColor = new(102, 112, 128, 238);
    private static readonly Color PositiveColor = new(67, 145, 91, 238);
    private static readonly Color WarningColor = new(207, 147, 57, 238);
    private static readonly Color DangerColor = new(176, 71, 67, 238);
    private static readonly Color FollowColor = new(139, 93, 166, 238);
    private static readonly Color ProfileColor = new(66, 121, 178, 238);
    private static readonly Color RingBorderColor = new(42, 31, 25, 225);
    private static readonly Color CenterColor = new(247, 232, 204, 245);
    private static readonly Color CenterBorderColor = new(78, 55, 38, 245);
    private static readonly Color TextColor = new(255, 248, 225);
    private static readonly Color CaptionBackgroundColor = new(35, 27, 24, 205);
    private static readonly Color CaptionBorderColor = new(111, 82, 54, 210);
    private static readonly Color CaptionHoverBorderColor = new(255, 224, 157, 225);

    private readonly record struct CaptionLayout(string Text, Vector2 Size);
    private readonly record struct CaptionLayoutKey(string Text, SpriteFont Font, float MaxWidth, bool AllowWrap, int MaxLines);

    private CompanionActionWheelModel? model;
    private Vector2 center;
    private Vector2 lastPointerPosition;
    private int pageIndex;
    private int? focusedSlotIndex;
    private bool hasPointerPosition;
    private readonly Dictionary<CaptionLayoutKey, CaptionLayout> captionLayouts = new();

    public bool IsOpen => this.model is not null;

    public int LastKeybindHandledTick { get; private set; } = int.MinValue;

    public void MarkKeybindHandled(int tick)
    {
        this.LastKeybindHandledTick = tick;
    }

    public void Open(CompanionActionWheelModel model, Vector2 screenPosition)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(model.Options);
        ArgumentNullException.ThrowIfNull(model.IsValid);
        if (model.Options.Count < 1)
            throw new ArgumentOutOfRangeException(nameof(model), "The wheel needs at least one option.");
        if (model.PinnedOptionCount < 0
            || model.PinnedOptionCount > MaximumOptions - 3
            || (model.PinnedOptionCount == 0 && model.Options.Count > MaximumOptions))
        {
            throw new ArgumentOutOfRangeException(nameof(model), "A paginated wheel needs one to three pinned options.");
        }

        CompanionActionWheelOption[] options = model.Options
            .Select(option => option ?? throw new ArgumentException("Wheel options cannot be null.", nameof(model)))
            .ToArray();
        if (options.Any(option => option.Activate is null))
            throw new ArgumentException("Wheel option callbacks cannot be null.", nameof(model));

        this.captionLayouts.Clear();
        this.model = model with { Options = options };
        this.center = ClampToViewport(screenPosition);
        this.lastPointerPosition = screenPosition;
        this.hasPointerPosition = float.IsFinite(screenPosition.X) && float.IsFinite(screenPosition.Y);
        this.pageIndex = 0;
        this.focusedSlotIndex = GetFirstFocusableSlot(this.GetPageLayout());
    }

    public void Close()
    {
        this.captionLayouts.Clear();
        this.model = null;
        this.pageIndex = 0;
        this.focusedSlotIndex = null;
        this.hasPointerPosition = false;
    }

    public CompanionActionWheelActivationResult TryActivate(Vector2 screenPosition)
    {
        if (this.model is null)
            return CompanionActionWheelActivationResult.None;

        CompanionActionWheelPageLayout layout = this.GetPageLayout();
        int? slotIndex = this.GetSlotIndex(screenPosition, layout.Slots.Length);
        return this.ActivateSlot(layout, slotIndex);
    }

    public CompanionActionWheelActivationResult TryActivateFocused()
    {
        if (this.model is null)
            return CompanionActionWheelActivationResult.None;

        CompanionActionWheelPageLayout layout = this.GetPageLayout();
        return this.ActivateSlot(layout, this.focusedSlotIndex);
    }

    public bool ChangePage(int delta)
    {
        CompanionActionWheelModel? activeModel = this.model;
        if (activeModel is null || delta == 0)
            return false;
        if (!IsModelValid(activeModel))
        {
            this.Close();
            return false;
        }

        CompanionActionWheelPageLayout current = this.GetPageLayout();
        if (current.PageCount <= 1)
            return false;

        int oldPage = current.PageIndex;
        this.pageIndex = current.PageIndex + Math.Sign(delta);
        CompanionActionWheelPageLayout next = this.GetPageLayout();
        if (next.PageIndex == oldPage)
            return false;

        if (this.focusedSlotIndex is not int focused
            || focused < 0
            || focused >= next.Slots.Length
            || !CompanionActionWheelNavigation.IsFocusable(next.Slots[focused]))
        {
            this.focusedSlotIndex = GetFirstPageOptionSlot(next, activeModel.PinnedOptionCount)
                ?? GetFirstFocusableSlot(next);
        }

        this.captionLayouts.Clear();
        return true;
    }

    public bool MoveFocus(float directionX, float directionY)
    {
        if (this.model is null)
            return false;

        CompanionActionWheelPageLayout layout = this.GetPageLayout();
        int? next = CompanionActionWheelNavigation.MoveFocus(
            layout.Slots,
            this.focusedSlotIndex,
            directionX,
            directionY,
            FirstSegmentCenterAngle);
        if (next == this.focusedSlotIndex)
            return false;

        this.focusedSlotIndex = next;
        return true;
    }

    public void AnchorPointerForNavigation(Vector2 pointerPosition)
    {
        if (!float.IsFinite(pointerPosition.X) || !float.IsFinite(pointerPosition.Y))
            return;

        this.lastPointerPosition = pointerPosition;
        this.hasPointerPosition = true;
    }

    public void Draw(SpriteBatch b, Vector2 mousePosition)
    {
        CompanionActionWheelModel? activeModel = this.model;
        if (activeModel is null)
            return;

        ArgumentNullException.ThrowIfNull(b);
        if (!IsModelValid(activeModel))
        {
            this.Close();
            return;
        }

        this.center = ClampToViewport(this.center);
        CompanionActionWheelPageLayout layout = this.GetPageLayout();
        this.UpdatePointerFocus(mousePosition, layout);
        int optionCount = layout.Slots.Length;
        int? hoveredIndex = this.focusedSlotIndex;

        DrawRadialBand(b, this.center + new Vector2(3f, 4f), InnerRadius - 4f, OuterRadius + 3f, 0f, MathHelper.TwoPi, Color.Black * 0.42f);
        DrawRadialBand(b, this.center, InnerRadius - 4f, OuterRadius + 3f, 0f, MathHelper.TwoPi, RingBorderColor);

        float segmentAngle = MathHelper.TwoPi / optionCount;
        for (int index = 0; index < optionCount; index++)
        {
            CompanionActionWheelSlot slot = layout.Slots[index];
            float segmentCenter = FirstSegmentCenterAngle + index * segmentAngle;
            float gap = optionCount > 1 ? SectorGap : 0f;
            Color color = slot.Kind == CompanionActionWheelSlotKind.Empty
                ? NeutralColor * 0.28f
                : GetToneColor(this.GetSlotTone(activeModel, slot));
            this.DrawSector(
                b,
                segmentCenter - segmentAngle / 2f + gap,
                segmentCenter + segmentAngle / 2f - gap,
                color,
                hoveredIndex == index);
        }

        DrawFilledCircle(b, this.center, (int)InnerRadius, CenterBorderColor);
        DrawFilledCircle(b, this.center, (int)InnerRadius - 4, CenterColor);

        float maxLabelWidth = optionCount switch
        {
            1 => 184f,
            2 => 156f,
            3 => 126f,
            4 => 108f,
            5 => 92f,
            _ => 88f
        };
        float labelRadius = optionCount == 5 ? FiveOptionLabelRadius : LabelRadius;
        int maxLabelLines = optionCount <= 4 ? 3 : 2;
        for (int index = 0; index < optionCount; index++)
        {
            CompanionActionWheelSlot slot = layout.Slots[index];
            string label = this.GetSlotLabel(activeModel, slot);
            if (label.Length == 0)
                continue;

            float angle = FirstSegmentCenterAngle + index * segmentAngle;
            Vector2 offset = new(MathF.Cos(angle) * labelRadius, MathF.Sin(angle) * labelRadius);
            this.DrawCaption(
                b,
                label,
                this.center + offset,
                Game1.tinyFont,
                TextColor,
                maxLabelWidth,
                allowWrap: true,
                maxLines: maxLabelLines,
                emphasized: hoveredIndex == index);
        }

        if (layout.PageCount > 1)
        {
            string pageText = $"{layout.PageIndex + 1}/{layout.PageCount}";
            Vector2 pageSize = Game1.tinyFont.MeasureString(pageText);
            Vector2 pagePosition = this.center - pageSize / 2f;
            b.DrawString(Game1.tinyFont, pageText, pagePosition + new Vector2(1f, 1f), Color.White * 0.7f);
            b.DrawString(Game1.tinyFont, pageText, pagePosition, CenterBorderColor);
        }

        this.DrawCaption(
            b,
            activeModel.Title,
            this.center + new Vector2(0f, -OuterRadius - 22f),
            Game1.smallFont,
            TextColor,
            238f,
            allowWrap: false,
            maxLines: 1);
        string footer = hoveredIndex is int selectedIndex
            ? this.GetSlotFooter(activeModel, layout, layout.Slots[selectedIndex])
            : activeModel.Hint;
        this.DrawCaption(
            b,
            footer,
            this.center + new Vector2(0f, OuterRadius + 17f),
            Game1.tinyFont,
            TextColor,
            262f,
            allowWrap: false,
            maxLines: 1);
    }

    private static bool IsModelValid(CompanionActionWheelModel model)
    {
        try
        {
            return model.IsValid();
        }
        catch
        {
            return false;
        }
    }

    private CompanionActionWheelActivationResult ActivateSlot(
        CompanionActionWheelPageLayout layout,
        int? slotIndex)
    {
        CompanionActionWheelModel? activeModel = this.model;
        if (activeModel is null)
            return CompanionActionWheelActivationResult.None;
        if (slotIndex is not int selected
            || selected < 0
            || selected >= layout.Slots.Length
            || layout.Slots[selected].Kind == CompanionActionWheelSlotKind.Empty)
        {
            this.Close();
            return CompanionActionWheelActivationResult.Cancelled;
        }
        if (!IsModelValid(activeModel))
        {
            this.Close();
            return CompanionActionWheelActivationResult.Cancelled;
        }

        CompanionActionWheelSlot slot = layout.Slots[selected];
        if (slot.Kind == CompanionActionWheelSlotKind.PreviousPage)
            return this.ChangePage(-1)
                ? CompanionActionWheelActivationResult.PageChanged
                : CompanionActionWheelActivationResult.None;
        if (slot.Kind == CompanionActionWheelSlotKind.NextPage)
            return this.ChangePage(1)
                ? CompanionActionWheelActivationResult.PageChanged
                : CompanionActionWheelActivationResult.None;

        CompanionActionWheelOption option = activeModel.Options[slot.OptionIndex];
        this.Close();
        option.Activate();
        return CompanionActionWheelActivationResult.Activated;
    }

    private void UpdatePointerFocus(Vector2 pointerPosition, CompanionActionWheelPageLayout layout)
    {
        if (!float.IsFinite(pointerPosition.X) || !float.IsFinite(pointerPosition.Y))
            return;
        if (!this.hasPointerPosition)
        {
            this.lastPointerPosition = pointerPosition;
            this.hasPointerPosition = true;
            return;
        }

        if (Vector2.DistanceSquared(pointerPosition, this.lastPointerPosition) < 4f)
            return;

        this.lastPointerPosition = pointerPosition;
        int? slotIndex = this.GetSlotIndex(pointerPosition, layout.Slots.Length);
        this.focusedSlotIndex = slotIndex is int selected
            && CompanionActionWheelNavigation.IsFocusable(layout.Slots[selected])
                ? selected
                : null;
    }

    private CompanionActionWheelPageLayout GetPageLayout()
    {
        CompanionActionWheelModel activeModel = this.model
            ?? throw new InvalidOperationException("The action wheel is closed.");
        CompanionActionWheelPageLayout layout = CompanionActionWheelPagination.Create(
            activeModel.Options.Count,
            activeModel.PinnedOptionCount,
            this.pageIndex);
        this.pageIndex = layout.PageIndex;
        return layout;
    }

    private static int? GetFirstFocusableSlot(CompanionActionWheelPageLayout layout)
    {
        for (int index = 0; index < layout.Slots.Length; index++)
        {
            if (CompanionActionWheelNavigation.IsFocusable(layout.Slots[index]))
                return index;
        }

        return null;
    }

    private static int? GetFirstPageOptionSlot(
        CompanionActionWheelPageLayout layout,
        int pinnedOptionCount)
    {
        int firstIndex = Math.Clamp(pinnedOptionCount + 1, 0, layout.Slots.Length);
        for (int index = firstIndex; index < layout.Slots.Length - 1; index++)
        {
            if (layout.Slots[index].Kind == CompanionActionWheelSlotKind.Option)
                return index;
        }

        return null;
    }

    private string GetSlotLabel(CompanionActionWheelModel activeModel, CompanionActionWheelSlot slot)
    {
        return slot.Kind switch
        {
            CompanionActionWheelSlotKind.Option => activeModel.Options[slot.OptionIndex].Label,
            CompanionActionWheelSlotKind.PreviousPage => "<",
            CompanionActionWheelSlotKind.NextPage => ">",
            _ => ""
        };
    }

    private string GetSlotFooter(
        CompanionActionWheelModel activeModel,
        CompanionActionWheelPageLayout layout,
        CompanionActionWheelSlot slot)
    {
        return slot.Kind switch
        {
            CompanionActionWheelSlotKind.Option => activeModel.Options[slot.OptionIndex].HoverText
                ?? activeModel.Options[slot.OptionIndex].Label,
            CompanionActionWheelSlotKind.PreviousPage => $"< {GetWrappedPageNumber(layout, -1)}/{layout.PageCount}",
            CompanionActionWheelSlotKind.NextPage => $"> {GetWrappedPageNumber(layout, 1)}/{layout.PageCount}",
            _ => activeModel.Hint
        };
    }

    private CompanionActionWheelTone GetSlotTone(
        CompanionActionWheelModel activeModel,
        CompanionActionWheelSlot slot)
    {
        return slot.Kind == CompanionActionWheelSlotKind.Option
            ? activeModel.Options[slot.OptionIndex].Tone
            : CompanionActionWheelTone.Neutral;
    }

    private static int GetWrappedPageNumber(CompanionActionWheelPageLayout layout, int delta)
    {
        int target = (layout.PageIndex + delta) % layout.PageCount;
        if (target < 0)
            target += layout.PageCount;
        return target + 1;
    }

    private void DrawSector(SpriteBatch b, float startAngle, float endAngle, Color color, bool hovered)
    {
        if (hovered)
            color = Color.Lerp(color, Color.White, 0.3f);

        DrawRadialBand(b, this.center, InnerRadius, OuterRadius, startAngle, endAngle, color);
    }

    private void DrawCaption(
        SpriteBatch b,
        string text,
        Vector2 position,
        SpriteFont font,
        Color color,
        float maxWidth,
        bool allowWrap,
        int maxLines,
        bool emphasized = false)
    {
        CaptionLayout layout = CreateCaptionLayout(text, font, maxWidth, allowWrap, maxLines);
        Vector2 textPosition = position - layout.Size / 2f;
        Rectangle background = new(
            (int)MathF.Floor(textPosition.X) - 5,
            (int)MathF.Floor(textPosition.Y) - 2,
            Math.Max(1, (int)MathF.Ceiling(layout.Size.X) + 10),
            Math.Max(1, (int)MathF.Ceiling(layout.Size.Y) + 4));
        Rectangle border = new(background.X - 1, background.Y - 1, background.Width + 2, background.Height + 2);
        b.Draw(Game1.staminaRect, border, emphasized ? CaptionHoverBorderColor : CaptionBorderColor);
        b.Draw(Game1.staminaRect, background, CaptionBackgroundColor);

        string[] lines = layout.Text.Split('\n');
        float lineAdvance = font.LineSpacing;
        Vector2 shadowOffset = new(2f, 2f);
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            float lineWidth = font.MeasureString(line).X;
            Vector2 linePosition = new(
                position.X - lineWidth / 2f,
                textPosition.Y + lineIndex * lineAdvance);
            b.DrawString(font, line, linePosition + shadowOffset, Color.Black * 0.72f);
            b.DrawString(font, line, linePosition, color);
        }
    }

    private int? GetSlotIndex(Vector2 screenPosition, int optionCount)
    {
        if (!float.IsFinite(screenPosition.X) || !float.IsFinite(screenPosition.Y))
            return null;

        Vector2 offset = screenPosition - this.center;
        return CompanionActionWheelHitTest.GetSegment(
            offset.X,
            offset.Y,
            InnerRadius,
            OuterRadius,
            optionCount,
            FirstSegmentCenterAngle,
            optionCount > 1 ? SectorGap : 0f);
    }

    private static Color GetToneColor(CompanionActionWheelTone tone)
    {
        return tone switch
        {
            CompanionActionWheelTone.Positive => PositiveColor,
            CompanionActionWheelTone.Warning => WarningColor,
            CompanionActionWheelTone.Danger => DangerColor,
            CompanionActionWheelTone.Follow => FollowColor,
            CompanionActionWheelTone.Profile => ProfileColor,
            _ => NeutralColor
        };
    }

    private CaptionLayout CreateCaptionLayout(
        string? text,
        SpriteFont font,
        float maxWidth,
        bool allowWrap,
        int maxLines)
    {
        float safeWidth = Math.Max(1f, maxWidth);
        CaptionLayoutKey key = new(text ?? "", font, safeWidth, allowWrap, maxLines);
        if (this.captionLayouts.TryGetValue(key, out CaptionLayout cached))
            return cached;

        CompanionActionWheelTextFit fit = CompanionActionWheelTextLayout.Fit(
            text,
            safeWidth,
            allowWrap,
            maxLines,
            candidate => font.MeasureString(candidate).X);
        Vector2 naturalSize = font.MeasureString(fit.Text);
        CaptionLayout layout = new(fit.Text, naturalSize);
        this.captionLayouts[key] = layout;
        return layout;
    }

    private static Vector2 ClampToViewport(Vector2 requested)
    {
        float fallbackX = Math.Max(1, Game1.uiViewport.Width) / 2f;
        float fallbackY = Math.Max(1, Game1.uiViewport.Height) / 2f;
        float x = float.IsFinite(requested.X) ? requested.X : fallbackX;
        float y = float.IsFinite(requested.Y) ? requested.Y : fallbackY;
        return new Vector2(
            ClampAxis(x, OuterRadius + HorizontalMargin, Game1.uiViewport.Width - OuterRadius - HorizontalMargin),
            ClampAxis(y, OuterRadius + NameSpace, Game1.uiViewport.Height - OuterRadius - HintSpace));
    }

    private static float ClampAxis(float value, float minimum, float maximum)
    {
        if (maximum < minimum)
            return (minimum + maximum) / 2f;
        return Math.Clamp(value, minimum, maximum);
    }

    private static void DrawRadialBand(
        SpriteBatch b,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        float startAngle,
        float endAngle,
        Color color)
    {
        float length = Math.Max(1f, outerRadius - innerRadius);
        for (float angle = startAngle; angle <= endAngle + SectorStep * 0.5f; angle += SectorStep)
        {
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            b.Draw(
                Game1.staminaRect,
                center + direction * innerRadius,
                sourceRectangle: null,
                color,
                angle,
                new Vector2(0f, 0.5f),
                new Vector2(length, BandThickness),
                SpriteEffects.None,
                layerDepth: 0f);
        }
    }

    private static void DrawFilledCircle(SpriteBatch b, Vector2 center, int radius, Color color)
    {
        int centerX = (int)MathF.Round(center.X);
        int centerY = (int)MathF.Round(center.Y);
        for (int y = -radius; y <= radius; y += 2)
        {
            int halfWidth = (int)MathF.Sqrt(Math.Max(0, radius * radius - y * y));
            b.Draw(
                Game1.staminaRect,
                new Rectangle(centerX - halfWidth, centerY + y, Math.Max(1, halfWidth * 2), 2),
                color);
        }
    }
}
