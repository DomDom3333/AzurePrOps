using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls
{
    // Temporary highlight for navigation
    public class TemporaryHighlightTransformer : DocumentColorizingTransformer
    {
        private readonly int _lineNumber;
        private static readonly SolidColorBrush HighlightBrush = new SolidColorBrush(Color.FromRgb(255, 255, 150));

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

    // Search term highlighter
    public class SearchHighlightTransformer : DocumentColorizingTransformer
    {
        private readonly string _searchTerm;
        private static readonly SolidColorBrush SearchBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0));

        public SearchHighlightTransformer(string searchTerm)
        {
            _searchTerm = searchTerm;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (string.IsNullOrEmpty(_searchTerm))
                return;

            string text = CurrentContext.Document.GetText(line);
            if (string.IsNullOrEmpty(text))
                return;

            // Use case-insensitive search
            int index = 0;
            int startIndex;
            while ((startIndex = text.IndexOf(_searchTerm, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int endIndex = startIndex + _searchTerm.Length;
                ChangeLinePart(
                    line.Offset + startIndex,
                    line.Offset + endIndex,
                    e => {
                        e.TextRunProperties.SetBackgroundBrush(SearchBrush);
                        e.TextRunProperties.SetForegroundBrush(Brushes.Black);
                    });
                index = endIndex;
            }
        }
    }

    // Line marker for the gutter
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
                    // Get the visual position of the line
                    double y = visualLine.VisualTop;
                    double height = visualLine.Height;

                    ISolidColorBrush? brush = type switch
                    {
                        DiffLineType.Added => new SolidColorBrush(Colors.LightGreen),
                        DiffLineType.Removed => new SolidColorBrush(Colors.LightCoral),
                        DiffLineType.Modified => new SolidColorBrush(Colors.LightGoldenrodYellow),
                        _ => null
                    };

                    if (brush != null)
                    {
                        // Draw a marker on the left margin
                        drawingContext.DrawRectangle(
                            brush, 
                            null,
                            new Avalonia.Rect(0, y, 3, height));
                    }
                }
            }
        }
    }
}
