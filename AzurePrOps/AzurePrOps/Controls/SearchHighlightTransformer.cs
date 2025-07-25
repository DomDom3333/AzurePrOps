using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls
{
    /// <summary>
    /// Highlights search matches in the text editor
    /// </summary>
    public class SearchHighlightTransformer : DocumentColorizingTransformer
    {
        private readonly string _searchQuery;
        private readonly IBrush _highlightBrush;
        private readonly IBrush _textBrush;

        public SearchHighlightTransformer(string searchQuery)
        {
            _searchQuery = searchQuery ?? string.Empty;
            // Use unified color scheme - accent light background with primary text
            _highlightBrush = new SolidColorBrush(Color.FromRgb(221, 244, 255)); // AccentLightBrush equivalent
            _textBrush = new SolidColorBrush(Color.FromRgb(9, 105, 218)); // AccentBrush equivalent
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (string.IsNullOrWhiteSpace(_searchQuery) || line.Length == 0)
                return;

            var lineText = CurrentContext.Document.GetText(line);
            if (string.IsNullOrEmpty(lineText))
                return;

            // Case-insensitive search
            int index = 0;
            while ((index = lineText.IndexOf(_searchQuery, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int startOffset = line.Offset + index;
                int endOffset = startOffset + _searchQuery.Length;

                // Ensure we don't go beyond the line boundary
                if (endOffset <= line.EndOffset)
                {
                    ChangeLinePart(startOffset, endOffset, element =>
                    {
                        element.TextRunProperties.SetBackgroundBrush(_highlightBrush);
                        element.TextRunProperties.SetForegroundBrush(_textBrush);
                    });
                }

                index += _searchQuery.Length;
            }
        }
    }
}
