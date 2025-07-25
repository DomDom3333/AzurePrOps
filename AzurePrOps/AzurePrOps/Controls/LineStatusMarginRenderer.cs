using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Document;

namespace AzurePrOps.Controls;

public class LineStatusMarginRenderer : IBackgroundRenderer
{
    private readonly Dictionary<int, DiffLineType> _lineTypes;

    public LineStatusMarginRenderer(Dictionary<int, DiffLineType> lineTypes)
    {
        _lineTypes = lineTypes;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView == null || textView.VisualLinesValid == false)
            return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0)
            return;

        foreach (var visualLine in visualLines)
        {
            int lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (_lineTypes.TryGetValue(lineNumber, out var type))
            {
                double y = visualLine.VisualTop;
                double height = visualLine.Height;

                ISolidColorBrush? brush = type switch
                {
                    DiffLineType.Added => new SolidColorBrush(Color.FromRgb(26, 127, 55)), // SuccessBrush equivalent
                    DiffLineType.Removed => new SolidColorBrush(Color.FromRgb(207, 34, 46)), // DangerBrush equivalent  
                    DiffLineType.Modified => new SolidColorBrush(Color.FromRgb(191, 135, 0)), // WarningBrush equivalent
                    _ => null
                };

                if (brush != null)
                {
                    drawingContext.DrawRectangle(
                        brush,
                        null,
                        new Avalonia.Rect(0, y, 3, height));
                }
            }
        }
    }
}
