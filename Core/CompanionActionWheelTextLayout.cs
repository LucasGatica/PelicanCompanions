using System.Globalization;

namespace PelicanCompanions;

internal readonly record struct CompanionActionWheelTextFit(string Text);

/// <summary>Pure fixed-size text wrapping helpers used by the radial action wheel.</summary>
internal static class CompanionActionWheelTextLayout
{
    private readonly record struct WrapCandidate(
        string[] Lines,
        float Overflow,
        int BrokenWordCount,
        float WidestLine,
        float Imbalance);

    public static string Normalize(string? text)
    {
        return string.Join(
            " ",
            (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static CompanionActionWheelTextFit Fit(
        string? text,
        float maxWidth,
        bool allowWrap,
        int maxLines,
        Func<string, float> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(measureWidth);
        if (!float.IsFinite(maxWidth) || maxWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxLines < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLines));

        string value = Normalize(text);
        if (Measure(value, measureWidth) <= maxWidth)
            return new CompanionActionWheelTextFit(value);

        string[] lines = allowWrap && maxLines > 1
            ? WrapToWidth(value, maxWidth, maxLines, measureWidth)
            : new[] { value };
        return new CompanionActionWheelTextFit(string.Join("\n", lines));
    }

    private static string[] WrapToWidth(
        string text,
        float maxWidth,
        int maxLines,
        Func<string, float> measureWidth)
    {
        string[] elements = GetTextElements(text);
        if (elements.Length < 2)
            return new[] { text };

        WrapCandidate? best = null;
        int lineLimit = Math.Min(maxLines, elements.Length);
        for (int lineCount = 2; lineCount <= lineLimit; lineCount++)
        {
            List<string> lines = new(lineCount);
            List<int> boundaries = new(lineCount - 1);
            EvaluatePartitions(
                elements,
                start: 0,
                linesRemaining: lineCount,
                lines,
                boundaries,
                maxWidth,
                measureWidth,
                ref best);
        }

        return best?.Lines ?? new[] { text };
    }

    private static void EvaluatePartitions(
        string[] elements,
        int start,
        int linesRemaining,
        List<string> lines,
        List<int> boundaries,
        float maxWidth,
        Func<string, float> measureWidth,
        ref WrapCandidate? best)
    {
        if (linesRemaining == 1)
        {
            string finalLine = JoinAndTrim(elements, start, elements.Length);
            if (finalLine.Length == 0)
                return;

            lines.Add(finalLine);
            WrapCandidate candidate = CreateCandidate(lines, boundaries, elements, maxWidth, measureWidth);
            if (best is null || IsBetter(candidate, best.Value))
                best = candidate;
            lines.RemoveAt(lines.Count - 1);
            return;
        }

        int lastEnd = elements.Length - linesRemaining + 1;
        for (int end = start + 1; end <= lastEnd; end++)
        {
            string line = JoinAndTrim(elements, start, end);
            if (line.Length == 0)
                continue;

            lines.Add(line);
            boundaries.Add(end);
            EvaluatePartitions(
                elements,
                end,
                linesRemaining - 1,
                lines,
                boundaries,
                maxWidth,
                measureWidth,
                ref best);
            boundaries.RemoveAt(boundaries.Count - 1);
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static WrapCandidate CreateCandidate(
        List<string> lines,
        List<int> boundaries,
        string[] elements,
        float maxWidth,
        Func<string, float> measureWidth)
    {
        float[] widths = lines.Select(line => Measure(line, measureWidth)).ToArray();
        float overflow = widths.Sum(width => Math.Max(0f, width - maxWidth));
        int brokenWordCount = boundaries.Count(boundary => IsInsideWordBoundary(elements, boundary));
        float widestLine = widths.Max();
        float imbalance = widestLine - widths.Min();
        return new WrapCandidate(lines.ToArray(), overflow, brokenWordCount, widestLine, imbalance);
    }

    private static bool IsBetter(WrapCandidate candidate, WrapCandidate current)
    {
        int comparison = candidate.Overflow.CompareTo(current.Overflow);
        if (comparison != 0)
            return comparison < 0;

        comparison = candidate.BrokenWordCount.CompareTo(current.BrokenWordCount);
        if (comparison != 0)
            return comparison < 0;

        comparison = candidate.Lines.Length.CompareTo(current.Lines.Length);
        if (comparison != 0)
            return comparison < 0;

        comparison = candidate.WidestLine.CompareTo(current.WidestLine);
        if (comparison != 0)
            return comparison < 0;

        return candidate.Imbalance < current.Imbalance;
    }

    private static bool IsInsideWordBoundary(string[] elements, int boundary)
    {
        return boundary > 0
            && boundary < elements.Length
            && !string.IsNullOrWhiteSpace(elements[boundary - 1])
            && !string.IsNullOrWhiteSpace(elements[boundary]);
    }

    private static string JoinAndTrim(string[] elements, int start, int end)
    {
        return string.Concat(elements[start..end]).Trim();
    }

    private static string[] GetTextElements(string text)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        string[] elements = new string[starts.Length];
        for (int index = 0; index < starts.Length; index++)
        {
            int end = index + 1 < starts.Length ? starts[index + 1] : text.Length;
            elements[index] = text[starts[index]..end];
        }

        return elements;
    }

    private static float Measure(string text, Func<string, float> measureWidth)
    {
        float width = measureWidth(text);
        if (!float.IsFinite(width) || width < 0f)
            throw new ArgumentOutOfRangeException(nameof(measureWidth), "Measured text widths must be finite and non-negative.");

        return width;
    }

}
