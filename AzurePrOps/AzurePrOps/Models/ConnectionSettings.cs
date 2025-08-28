using System.Collections.Generic;

namespace AzurePrOps.Models;

public record ConnectionSettings(
    string Organization,
    string Project,
    string Repository,
    string ReviewerId,
    string EditorCommand = "code",
    bool UseGitDiff = true,
    List<string>? SelectedReviewerGroups = null,
    bool IncludeGroupReviews = true,
    List<string>? SelectedGroupsForFiltering = null,
    bool EnableGroupFiltering = false,
    string UserDisplayName = "",
    UserRole UserRole = UserRole.Developer)
{
    public List<string> SelectedReviewerGroups { get; init; } = SelectedReviewerGroups ?? new List<string>();
    public List<string> SelectedGroupsForFiltering { get; init; } = SelectedGroupsForFiltering ?? new List<string>();
    
    /// <summary>
    /// Indicates whether a Personal Access Token is stored securely in Windows Credential Manager
    /// </summary>
    public bool HasSecureToken { get; init; } = false;
    
    /// <summary>
    /// Gets the Personal Access Token from secure storage when needed
    /// </summary>
    public string PersonalAccessToken => HasSecureToken ? 
        ConnectionSettingsStorage.GetPersonalAccessToken() ?? string.Empty : 
        string.Empty;
    
    /// <summary>
    /// The primary role of the user, which affects workflow preset behavior
    /// </summary>
    public UserRole UserRole { get; init; } = UserRole;
}
