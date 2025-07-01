using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls;

public class TemporaryHighlightTransformer : DocumentColorizingTransformer
{
    private readonly int _lineNumber;
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(255, 255, 150));

    public TemporaryHighlightTransformer(int lineNumber)
    {
        _lineNumber = lineNumber;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.LineNumber == _lineNumber)
        {
            ChangeLinePart(line.Offset, line.EndOffset, e =>
                e.TextRunProperties.SetBackgroundBrush(HighlightBrush));
        }
    }
}
