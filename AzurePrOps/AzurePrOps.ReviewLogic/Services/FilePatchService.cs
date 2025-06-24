using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class FilePatchService : IPatchService
{
    public PatchResult ApplyPatch(string filePath, string patch)
    {
        try
        {
            string original = File.ReadAllText(filePath);

            // Since we can't directly apply patches with DiffPlex,
            // we'll simulate it by creating a simple merge algorithm
            string[] patchLines = patch.Split('\n');
            string[] origLines = original.Split('\n');
            List<string> resultLines = new List<string>();

            // Basic patch parsing logic - in a real implementation this would be more robust
            for (int i = 0; i < patchLines.Length; i++)
            {
                string line = patchLines[i];
                if (line.StartsWith("+"))
                {
                    // Add line from patch
                    resultLines.Add(line.Substring(1));
                }
                else if (!line.StartsWith("-"))
                {
                    // Keep unchanged line
                    resultLines.Add(line);
                }
                // Skip lines that start with '-' (removed lines)
            }

            // Write merged content
            string patched = string.Join("\n", resultLines);
            File.WriteAllText(filePath, patched);

            return new PatchResult { Success = true, Messages = new[] { "Patch applied" } };
        }
        catch (Exception ex)
        {
            return new PatchResult { Success = false, Messages = new[] { ex.Message } };
        }
    }

    public void AcceptChange(int changeId)
    {
        // stub: hook into your own change-tracking
    }

    public void RejectChange(int changeId)
    {
        // stub: hook into your own change-tracking
    }

    public byte[] DownloadDiff(string filePath)
        => System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(filePath));
}