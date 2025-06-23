namespace AzurePrOps.Models;

public record FilterView(
    string Name,
    string Title,
    string Creator,
    string SourceBranch,
    string TargetBranch,
    string Status);
