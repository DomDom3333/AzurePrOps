namespace AzurePrOps.ReviewLogic.Models;

public record MergeOptions(
    bool Squash,
    bool DeleteSourceBranch,
    string CommitMessage);

