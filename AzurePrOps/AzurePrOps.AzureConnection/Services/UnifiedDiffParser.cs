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
                    result.Add(new FileDiff(currentFile, sb.ToString()));
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
            result.Add(new FileDiff(currentFile, sb.ToString()));
        return result;
    }
}
