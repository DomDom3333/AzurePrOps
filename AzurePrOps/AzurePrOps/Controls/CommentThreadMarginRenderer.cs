using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Document;

namespace AzurePrOps.Controls;

/// <summary>
/// Draws a small comment marker in the gutter for lines that have comment threads.
/// </summary>
public class CommentThreadMarginRenderer : IBackgroundRenderer
{
    private readonly HashSet<int> _commentLines;

    public CommentThreadMarginRenderer(IEnumerable<int> lines)
    {
        _commentLines = new HashSet<int>(lines);
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            int lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (!_commentLines.Contains(lineNumber))
                continue;

            double y = visualLine.VisualTop + visualLine.Height / 2 - 3;
            var rect = new Rect(6, y, 6, 6);
            drawingContext.DrawEllipse(Brushes.Goldenrod, null, rect.Center, 3, 3);
        }
    }
}
