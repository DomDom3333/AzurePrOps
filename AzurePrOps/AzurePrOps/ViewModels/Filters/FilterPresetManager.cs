using System;
using System.Collections.Generic;
using AzurePrOps.Models.FilteringAndSorting;
using AzurePrOps.Models;

namespace AzurePrOps.ViewModels.Filters;

/// <summary>
/// Manages filter presets including role-based quick filters and saved filter views
/// This extracts preset logic from the main ViewModel
/// </summary>
public class FilterPresetManager
{
    private readonly Dictionary<string, Action<FilterState, UserRole>> _presets;

    public FilterPresetManager()
    {
        _presets = new Dictionary<string, Action<FilterState, UserRole>>
        {
            { "Needs My Review", ApplyNeedsMyReviewPreset },
            { "My Pull Requests", ApplyMyPullRequestsPreset },
            { "Assigned to Me", ApplyAssignedToMePreset },
            { "Approved - Ready for Testing", ApplyApprovedReadyForTestingPreset },
            { "Waiting for Author", ApplyWaitingForAuthorPreset },
            { "All Active", ApplyAllActivePreset },
            { "Recently Updated", ApplyRecentlyUpdatedPreset }
        };
    }

    public IEnumerable<string> AvailablePresets => _presets.Keys;

    public void ApplyPreset(string presetName, FilterState filterState, UserRole userRole = UserRole.General)
    {
        if (string.IsNullOrEmpty(presetName) || !_presets.ContainsKey(presetName))
            return;

        // Reset filters first
        filterState.ResetToDefaults();
        
        // Apply the specific preset
        _presets[presetName](filterState, userRole);
        
        // Update the filter source
        filterState.Criteria.SetFilterSource("Preset", presetName);
    }

    #region Preset Implementations

    private void ApplyNeedsMyReviewPreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                filterState.NeedsMyReviewOnly = true;
                filterState.ReviewerVoteFilter = "No vote";
                break;
            case UserRole.Tester:
                // For testers: Show PRs that need testing (approved by developers)
                filterState.ReviewerVoteFilter = "Approved";
                filterState.NeedsMyReviewOnly = true;
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // For team leads: Show all PRs needing any review for oversight
                filterState.ReviewerVoteFilter = "No vote";
                break;
            case UserRole.General:
            default:
                filterState.NeedsMyReviewOnly = true;
                filterState.ReviewerVoteFilter = "No vote";
                break;
        }
    }

    private void ApplyMyPullRequestsPreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";
        filterState.MyPullRequestsOnly = true;
        
        // Role doesn't significantly affect "my PRs" view
        // Could add role-specific sorting or additional filters if needed
    }

    private void ApplyAssignedToMePreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";
        filterState.AssignedToMeOnly = true;

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                // Developers typically want to see what they need to work on
                break;
            case UserRole.Tester:
                // Testers want to see what's assigned for testing
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // Team leads might have different assignment meanings
                break;
            case UserRole.General:
            default:
                break;
        }
    }

    private void ApplyApprovedReadyForTestingPreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                // For developers: Show PRs where I have approved
                filterState.ReviewerVoteFilter = "Approved";
                break;
            case UserRole.Tester:
                // For testers: Show ALL PRs that have been approved by anyone and are ready for testing
                // Don't filter by my vote - show PRs approved by developers
                filterState.ReviewerVoteFilter = "Approved";
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // For team leads: Show all approved PRs for oversight
                filterState.ReviewerVoteFilter = "Approved";
                break;
            case UserRole.General:
            default:
                // General role: Default developer behavior
                filterState.ReviewerVoteFilter = "Approved";
                break;
        }
    }

    private void ApplyWaitingForAuthorPreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                // For developers: Show PRs where I voted "waiting for author"
                filterState.ReviewerVoteFilter = "Waiting for author";
                break;
            case UserRole.Tester:
                // For testers: Show ALL PRs waiting for author response (not just mine)
                // This helps them track what's blocked in testing pipeline
                filterState.ReviewerVoteFilter = "Waiting for author";
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // For team leads: Show all PRs waiting for author for bottleneck analysis
                filterState.ReviewerVoteFilter = "Waiting for author";
                break;
            case UserRole.General:
            default:
                // General role: Default developer behavior
                filterState.ReviewerVoteFilter = "Waiting for author";
                break;
        }
    }

    private void ApplyAllActivePreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";
        // No additional filters - show all active PRs
        
        // Could add role-specific sorting preferences
        switch (userRole)
        {
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // Team leads might want to see by priority or age
                break;
            case UserRole.Tester:
                // Testers might want to see by readiness for testing
                break;
            default:
                break;
        }
    }

    private void ApplyRecentlyUpdatedPreset(FilterState filterState, UserRole userRole)
    {
        filterState.StatusFilter = "Active";
        
        // Set to show PRs updated in the last 7 days
        var sevenDaysAgo = DateTimeOffset.Now.AddDays(-7);
        filterState.SetDateRange(null, null, sevenDaysAgo, null);
        
        // Role-specific considerations for "recently updated"
        switch (userRole)
        {
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // Team leads might want to see all recent activity
                break;
            case UserRole.Developer:
            case UserRole.Architect:
                // Developers might want to focus on their involvement
                break;
            case UserRole.Tester:
                // Testers might want to see what's been updated and ready for testing
                break;
            default:
                break;
        }
    }

    #endregion

    #region Saved Filter Views

    public void SaveFilterView(string name, FilterState filterState)
    {
        // This would save the current filter state to storage
        // Implementation would depend on your storage mechanism
        // Could use FilterSortPreferencesStorage or similar
    }

    public void LoadFilterView(string name, FilterState filterState)
    {
        // This would load a saved filter view from storage
        // and apply it to the filter state
    }

    public void DeleteFilterView(string name)
    {
        // This would delete a saved filter view from storage
    }

    public IEnumerable<string> GetSavedFilterViews()
    {
        // This would return the names of all saved filter views
        // Implementation would depend on your storage mechanism
        return new List<string>();
    }

    #endregion
}
