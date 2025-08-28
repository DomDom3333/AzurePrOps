using System;
using System.Collections.Generic;
using System.Linq;

namespace AzurePrOps.AzureConnection.Models;

public record PullRequestInfo(
    int Id,
    string Title,
    string Creator,
    string CreatorId,
    DateTime Created,
    string Status,
    IReadOnlyList<ReviewerInfo> Reviewers,
    string SourceBranch,
    string TargetBranch,
    string Url,
    bool IsDraft = false,
    string ReviewerVote = "No vote",
    bool ShowDraftBadge = false,
    DateTime? LastActivity = null)
{
    public string WebUrl => !string.IsNullOrWhiteSpace(Url) && Uri.IsWellFormedUriString(Url, UriKind.Absolute)
        ? Url
        : string.Empty;

    public string ReviewersText => string.Join(", ",
        Reviewers.Select(r => $"{VoteToIcon(r.Vote)} {r.DisplayName} ({r.Vote})"));

    public string ReviewerVoteIcon => VoteToIcon(ReviewerVote);

    private static string VoteToIcon(string vote) => vote.ToLowerInvariant() switch
    {
        "approved" => "✅",
        "approved with suggestions" => "📝",
        "waiting for author" => "⏳",
        "rejected" => "❌",
        _ => "❔"
    };

    // Use LastActivity if available, otherwise fall back to Created date
    public DateTime EffectiveLastActivity => LastActivity ?? Created;
}
