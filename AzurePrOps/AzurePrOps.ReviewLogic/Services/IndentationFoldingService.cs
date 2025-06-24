using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class IndentationFoldingService : ICodeFoldingService
{
    public IEnumerable<FoldRegion> GetFoldRegions(string code)
    {
        string[] lines = code.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
        Stack<(int indent, int lineIndex)> stack = new Stack<(int indent, int lineIndex)>();

        for (int i = 0; i < lines.Length; i++)
        {
            int indent = lines[i].TakeWhile(ch => ch == ' ' || ch == '\t').Count();
            while (stack.Any() && indent <= stack.Peek().indent)
            {
                (int startIndent, int start) = stack.Pop();
                if (i - 1 > start)
                    yield return new FoldRegion { StartLine = start + 1, EndLine = i };
            }
            stack.Push((indent, i));
        }

        while (stack.Any())
        {
            (_, int start) = stack.Pop();
            if (lines.Length - 1 > start)
                yield return new FoldRegion { StartLine = start + 1, EndLine = lines.Length };
        }
    }
}