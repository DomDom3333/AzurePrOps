using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls;

/// <summary>
/// A custom margin that displays original file line numbers instead of editor line numbers.
/// </summary>
public class DiffLineNumberMargin : AbstractMargin
{
    private readonly Dictionary<int, int> _lineNumbers; // Editor Line -> Original Line

    public DiffLineNumberMargin(Dictionary<int, int> lineNumbers)
    {
        _lineNumbers = lineNumbers;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Estimate width based on max line number
        return new Size(40, 0);
    }

    public override void Render(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        var foreground = Brushes.Gray;
        var typeface = new Typeface("JetBrains Mono, Consolas, monospace");
        double fontSize = 12;

        foreach (var visualLine in textView.VisualLines)
        {
            int lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (_lineNumbers.TryGetValue(lineNumber, out int originalLineNumber))
            {
                string text = originalLineNumber.ToString(CultureInfo.InvariantCulture);
                var formattedText = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    foreground
                );

                double y = visualLine.VisualTop - textView.VerticalOffset;
                drawingContext.DrawText(formattedText, new Point(35 - formattedText.Width, y));
            }
        }
    }
}
