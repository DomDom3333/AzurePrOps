namespace AzurePrOps.AzureConnection.Models;

public record FileDiff(string FilePath, string Diff, string OldText, string NewText);
