using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls
{
    /// <summary>
    /// Temporarily highlights a specific line with a fade-out effect
    /// </summary>
    public class TemporaryHighlightTransformer : DocumentColorizingTransformer
    {
        private readonly int _lineNumber;
        private readonly IBrush _highlightBrush;
        private readonly IBrush _textBrush;

        public TemporaryHighlightTransformer(int lineNumber)
        {
            _lineNumber = lineNumber;
            // Use unified color scheme - selected background with primary text
            _highlightBrush = new SolidColorBrush(Color.FromRgb(231, 243, 255)) { Opacity = 0.8 }; // SelectedBrush equivalent
            _textBrush = new SolidColorBrush(Color.FromRgb(36, 41, 47)); // TextPrimaryBrush equivalent
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.LineNumber == _lineNumber)
            {
                ChangeLinePart(line.Offset, line.EndOffset, element =>
                {
                    element.TextRunProperties.SetBackgroundBrush(_highlightBrush);
                    element.TextRunProperties.SetForegroundBrush(_textBrush);
                });
            }
        }
    }
}
