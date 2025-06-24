namespace AzurePrOps.ReviewLogic.Models;

public record FileDiff(string FilePath, string Diff, string OldText, string NewText);
