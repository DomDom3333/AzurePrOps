using System;
using System.Collections.Generic;
using System.Linq;

namespace AzurePrOps.ReviewLogic.Models;

public record ReviewerInfo(string Id, string DisplayName, string Vote);

public record PullRequestComment(string Author, string Content, DateTime PostedDate);

public record PullRequestInfo(
    int Id,
    string Title,
    string Creator,
    DateTime Created,
    string Status,
    IReadOnlyList<ReviewerInfo> Reviewers,
    string SourceBranch,
    string TargetBranch,
    string Url)
    {
    // Ensure URL is valid and well-formed
    public string WebUrl => !string.IsNullOrWhiteSpace(Url) && Uri.IsWellFormedUriString(Url, UriKind.Absolute) 
        ? Url 
        : string.Empty;

    public string ReviewersText => string.Join(", ",
        Reviewers.Select(r => $"{VoteToIcon(r.Vote)} {r.DisplayName} ({r.Vote})"));

    private static string VoteToIcon(string vote) => vote.ToLowerInvariant() switch
    {
        "approved" => "✅",
        "approved with suggestions" => "📝",
        "waiting for author" => "⏳",
        "rejected" => "❌",
        _ => "❔"
    };
}
