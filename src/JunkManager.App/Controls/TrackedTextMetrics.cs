namespace JunkManager.App.Controls;

/// <summary>
/// The layout arithmetic of tracked text, pulled out of the element so it can be
/// tested without a window, an STA thread or a font.
/// </summary>
/// <remarks>
/// internal, а не public: CA1515 при AnalysisLevel=latest-all требует этого от
/// исполняемого проекта, и требует справедливо. Тестам тип виден через
/// InternalsVisibleTo в JunkManager.App.csproj.
/// </remarks>
internal static class TrackedTextMetrics
{
    public static double Width(IReadOnlyList<double> glyphWidths, double fontSize, double tracking)
    {
        ArgumentNullException.ThrowIfNull(glyphWidths);

        if (glyphWidths.Count == 0)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 0; i < glyphWidths.Count; i++)
        {
            total += glyphWidths[i];
        }

        // No gap after the last glyph.
        return total + (fontSize * tracking * (glyphWidths.Count - 1));
    }
}
