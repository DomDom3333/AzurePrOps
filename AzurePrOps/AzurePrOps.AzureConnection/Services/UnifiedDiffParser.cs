using System.Collections.Generic;
using System.Text;
using AzurePrOps.AzureConnection.Models;

namespace AzurePrOps.AzureConnection.Services;

public static class UnifiedDiffParser
{
    public static IReadOnlyList<FileDiff> Parse(string diff)
    {
        var result = new List<FileDiff>();
        var lines = diff.Split('\n');
        string? currentFile = null;
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git"))
            {
                if (currentFile != null)
                {
                    var patch = sb.ToString();
                    var (oldText, newText) = ParseOldNew(patch);
                    result.Add(new FileDiff(currentFile, patch, oldText, newText));
                    sb.Clear();
                }
                var parts = line.Split(' ');
                if (parts.Length >= 4)
                {
                    currentFile = parts[2].StartsWith("a/") ? parts[2][2..] : parts[2];
                }
            }
            else if (currentFile != null)
            {
                sb.AppendLine(line);
            }
        }
        if (currentFile != null)
        {
            var patch = sb.ToString();
            var (oldText, newText) = ParseOldNew(patch);
            result.Add(new FileDiff(currentFile, patch, oldText, newText));
        }
        return result;
    }

    private static (string oldText, string newText) ParseOldNew(string patch)
    {
        var oldLines = new List<string>();
        var newLines = new List<string>();
        foreach (var ln in patch.Split('\n'))
        {
            if (ln.StartsWith("+++") || ln.StartsWith("---") || ln.StartsWith("diff ") || ln.StartsWith("@@"))
                continue;
            if (ln.StartsWith("+"))
            {
                newLines.Add(ln[1..]);
            }
            else if (ln.StartsWith("-"))
            {
                oldLines.Add(ln[1..]);
            }
            else
            {
                var text = ln.StartsWith(" ") ? ln[1..] : ln;
                oldLines.Add(text);
                newLines.Add(text);
            }
        }
        return (string.Join('\n', oldLines), string.Join('\n', newLines));
    }
}
