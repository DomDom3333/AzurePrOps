using System.Collections.Generic;

namespace AzurePrOps.Models;

public record ConnectionSettings(
    string Organization,
    string Project,
    string Repository,
    string PersonalAccessToken,
    string ReviewerId,
    string EditorCommand = "code",
    bool UseGitDiff = true,
    List<string> SelectedReviewerGroups = null,
    bool IncludeGroupReviews = true)
{
    public List<string> SelectedReviewerGroups { get; init; } = SelectedReviewerGroups ?? new List<string>();
}
