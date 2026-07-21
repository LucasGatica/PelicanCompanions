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
    Action Activate);

internal sealed record CompanionActionWheelModel(
    string Title,
    string Hint,
    IReadOnlyList<CompanionActionWheelOption> Options,
    Func<bool> IsValid);

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
        if (model.Options.Count is < 1 or > MaximumOptions)
            throw new ArgumentOutOfRangeException(nameof(model), $"The wheel needs between 1 and {MaximumOptions} options.");

        CompanionActionWheelOption[] options = model.Options
            .Select(option => option ?? throw new ArgumentException("Wheel options cannot be null.", nameof(model)))
            .ToArray();
        if (options.Any(option => option.Activate is null))
            throw new ArgumentException("Wheel option callbacks cannot be null.", nameof(model));

        this.captionLayouts.Clear();
        this.model = model with { Options = options };
        this.center = ClampToViewport(screenPosition);
    }

    public void Close()
    {
        this.captionLayouts.Clear();
        this.model = null;
    }

    public bool TryActivate(Vector2 screenPosition)
    {
        CompanionActionWheelModel? activeModel = this.model;
        if (activeModel is null)
            return false;

        int? optionIndex = this.GetOptionIndex(screenPosition, activeModel.Options.Count);
        this.Close();
        if (optionIndex is null || !IsModelValid(activeModel))
            return true;

        activeModel.Options[optionIndex.Value].Activate();
        return true;
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
        int optionCount = activeModel.Options.Count;
        int? hoveredIndex = this.GetOptionIndex(mousePosition, optionCount);

        DrawRadialBand(b, this.center + new Vector2(3f, 4f), InnerRadius - 4f, OuterRadius + 3f, 0f, MathHelper.TwoPi, Color.Black * 0.42f);
        DrawRadialBand(b, this.center, InnerRadius - 4f, OuterRadius + 3f, 0f, MathHelper.TwoPi, RingBorderColor);

        float segmentAngle = MathHelper.TwoPi / optionCount;
        for (int index = 0; index < optionCount; index++)
        {
            float segmentCenter = FirstSegmentCenterAngle + index * segmentAngle;
            float gap = optionCount > 1 ? SectorGap : 0f;
            this.DrawSector(
                b,
                segmentCenter - segmentAngle / 2f + gap,
                segmentCenter + segmentAngle / 2f - gap,
                GetToneColor(activeModel.Options[index].Tone),
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
            float angle = FirstSegmentCenterAngle + index * segmentAngle;
            Vector2 offset = new(MathF.Cos(angle) * labelRadius, MathF.Sin(angle) * labelRadius);
            this.DrawCaption(
                b,
                activeModel.Options[index].Label,
                this.center + offset,
                Game1.tinyFont,
                TextColor,
                maxLabelWidth,
                allowWrap: true,
                maxLines: maxLabelLines,
                emphasized: hoveredIndex == index);
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
            ? activeModel.Options[selectedIndex].Label
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

    private int? GetOptionIndex(Vector2 screenPosition, int optionCount)
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
