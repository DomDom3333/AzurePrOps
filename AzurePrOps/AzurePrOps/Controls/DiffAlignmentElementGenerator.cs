using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;

namespace AzurePrOps.Controls;

/// <summary>
/// Injects visual spacers to align lines between the two diff editors.
/// </summary>
public class DiffAlignmentElementGenerator : VisualLineElementGenerator
{
    private readonly Dictionary<int, int> _spacers; // Key: line number, Value: number of spacer lines to add ABOVE

    public DiffAlignmentElementGenerator(Dictionary<int, int> spacers)
    {
        _spacers = spacers;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.Document.GetLineByOffset(startOffset);
        if (line.Offset == startOffset && _spacers.ContainsKey(line.LineNumber))
        {
            return startOffset;
        }
        
        // If we are already past the start of the line, we are not interested in this line anymore
        // for the spacer (which is added at the very beginning of the line).
        var nextLine = line.NextLine;
        while (nextLine != null)
        {
            if (_spacers.ContainsKey(nextLine.LineNumber))
                return nextLine.Offset;
            nextLine = nextLine.NextLine;
        }
        
        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var line = CurrentContext.Document.GetLineByOffset(offset);
        if (line.Offset == offset && _spacers.TryGetValue(line.LineNumber, out int spacerCount))
        {
            return new SpacerElement(spacerCount);
        }
        return null;
    }

    private class SpacerElement : VisualLineElement
    {
        private readonly int _spacerCount;

        public SpacerElement(int spacerCount) : base(0, 0)
        {
            _spacerCount = spacerCount;
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            return new TextCharacters(" ", context.GlobalTextRunProperties);
        }
    }
}
