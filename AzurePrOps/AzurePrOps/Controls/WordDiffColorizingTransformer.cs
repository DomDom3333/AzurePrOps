using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls
{
    /// <summary>
    /// Highlights intra-line word/character differences for a specific editor.
    /// Provide spans per line that should be emphasized (e.g., inserted parts for new editor,
    /// deleted parts for old editor).
    /// </summary>
    public class WordDiffColorizingTransformer : DocumentColorizingTransformer
    {
        private readonly Dictionary<int, List<(int startColumn, int length)>> _spansByLine;
        private readonly IBrush _backgroundBrush;
        private readonly IBrush _foregroundBrush;

        /// <param name="spansByLine">Key: 1-based line number. Value: list of (startColumn, length) using 0-based column offsets within the line.</param>
        /// <param name="isAdditionStyle">If true, use green-ish style (additions); otherwise red-ish (deletions).</param>
        public WordDiffColorizingTransformer(Dictionary<int, List<(int startColumn, int length)>> spansByLine, bool isAdditionStyle)
        {
            _spansByLine = spansByLine;
            if (isAdditionStyle)
            {
                _backgroundBrush = new SolidColorBrush(Color.FromRgb(198, 246, 213)); // light green
                _foregroundBrush = new SolidColorBrush(Color.FromRgb(26, 127, 55));   // green
            }
            else
            {
                _backgroundBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202)); // light red
                _foregroundBrush = new SolidColorBrush(Color.FromRgb(207, 34, 46));   // red
            }
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_spansByLine.TryGetValue(line.LineNumber, out var spans) || spans.Count == 0)
                return;

            // Retrieve text of the line to ensure bounds
            var text = CurrentContext.Document.GetText(line);
            int lineStart = line.Offset;
            foreach (var (startColumn, length) in spans)
            {
                if (startColumn < 0 || length <= 0) continue;
                int start = lineStart + startColumn;
                int end = start + length;
                if (start >= line.EndOffset) continue;
                if (end > line.EndOffset) end = line.EndOffset;

                ChangeLinePart(start, end, e =>
                {
                    e.TextRunProperties.SetBackgroundBrush(_backgroundBrush);
                    e.TextRunProperties.SetForegroundBrush(_foregroundBrush);
                });
            }
        }
    }
}
