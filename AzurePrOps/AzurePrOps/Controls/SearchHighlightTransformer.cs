using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls;

public class SearchHighlightTransformer : DocumentColorizingTransformer
{
    private readonly string _searchTerm;
    private static readonly SolidColorBrush SearchBrush = new(Color.FromRgb(255, 165, 0));

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
