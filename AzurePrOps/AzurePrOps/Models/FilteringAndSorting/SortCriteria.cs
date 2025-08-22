using System;
using System.ComponentModel;

namespace AzurePrOps.Models.FilteringAndSorting;

public enum SortField
{
    Title,
    Creator,
    Created,
    Status,
    SourceBranch,
    TargetBranch,
    ReviewerVote,
    Id,
    ReviewerCount
}

public enum SortDirection
{
    Ascending,
    Descending
}

public class SortCriteria : INotifyPropertyChanged
{
    private SortField _primarySortField = SortField.Created;
    private SortDirection _primarySortDirection = SortDirection.Descending;
    private SortField? _secondarySortField;
    private SortDirection _secondarySortDirection = SortDirection.Ascending;
    private SortField? _tertiarySortField;
    private SortDirection _tertiarySortDirection = SortDirection.Ascending;

    public SortField PrimarySortField
    {
        get => _primarySortField;
        set
        {
            _primarySortField = value;
            OnPropertyChanged();
        }
    }

    public SortDirection PrimarySortDirection
    {
        get => _primarySortDirection;
        set
        {
            _primarySortDirection = value;
            OnPropertyChanged();
        }
    }

    public SortField? SecondarySortField
    {
        get => _secondarySortField;
        set
        {
            _secondarySortField = value;
            OnPropertyChanged();
        }
    }

    public SortDirection SecondarySortDirection
    {
        get => _secondarySortDirection;
        set
        {
            _secondarySortDirection = value;
            OnPropertyChanged();
        }
    }

    public SortField? TertiarySortField
    {
        get => _tertiarySortField;
        set
        {
            _tertiarySortField = value;
            OnPropertyChanged();
        }
    }

    public SortDirection TertiarySortDirection
    {
        get => _tertiarySortDirection;
        set
        {
            _tertiarySortDirection = value;
            OnPropertyChanged();
        }
    }

    public void Reset()
    {
        PrimarySortField = SortField.Created;
        PrimarySortDirection = SortDirection.Descending;
        SecondarySortField = null;
        SecondarySortDirection = SortDirection.Ascending;
        TertiarySortField = null;
        TertiarySortDirection = SortDirection.Ascending;
    }

    // Predefined sorting presets
    public void ApplyPreset(string preset)
    {
        switch (preset)
        {
            case "Newest First":
                PrimarySortField = SortField.Created;
                PrimarySortDirection = SortDirection.Descending;
                SecondarySortField = SortField.Id;
                SecondarySortDirection = SortDirection.Descending;
                TertiarySortField = null;
                break;
            case "Oldest First":
                PrimarySortField = SortField.Created;
                PrimarySortDirection = SortDirection.Ascending;
                SecondarySortField = SortField.Id;
                SecondarySortDirection = SortDirection.Ascending;
                TertiarySortField = null;
                break;
            case "Title A-Z":
                PrimarySortField = SortField.Title;
                PrimarySortDirection = SortDirection.Ascending;
                SecondarySortField = SortField.Created;
                SecondarySortDirection = SortDirection.Descending;
                TertiarySortField = null;
                break;
            case "Creator A-Z":
                PrimarySortField = SortField.Creator;
                PrimarySortDirection = SortDirection.Ascending;
                SecondarySortField = SortField.Created;
                SecondarySortDirection = SortDirection.Descending;
                TertiarySortField = null;
                break;
            case "Status Priority":
                PrimarySortField = SortField.Status;
                PrimarySortDirection = SortDirection.Ascending; // Active, Draft, Completed, Abandoned
                SecondarySortField = SortField.ReviewerVote;
                SecondarySortDirection = SortDirection.Ascending;
                TertiarySortField = SortField.Created;
                TertiarySortDirection = SortDirection.Descending;
                break;
            case "Review Priority":
                PrimarySortField = SortField.ReviewerVote;
                PrimarySortDirection = SortDirection.Ascending; // No vote, Waiting, Rejected, etc.
                SecondarySortField = SortField.Created;
                SecondarySortDirection = SortDirection.Ascending; // Older PRs need attention first
                TertiarySortField = null;
                break;
            case "High Activity":
                PrimarySortField = SortField.ReviewerCount;
                PrimarySortDirection = SortDirection.Descending;
                SecondarySortField = SortField.Created;
                SecondarySortDirection = SortDirection.Descending;
                TertiarySortField = null;
                break;
            default:
                Reset();
                break;
        }
    }

    public string GetDisplayName(SortField field)
    {
        return field switch
        {
            SortField.Title => "Title",
            SortField.Creator => "Creator",
            SortField.Created => "Created Date",
            SortField.Status => "Status",
            SortField.SourceBranch => "Source Branch",
            SortField.TargetBranch => "Target Branch",
            SortField.ReviewerVote => "Reviewer Vote",
            SortField.Id => "PR ID",
            SortField.ReviewerCount => "Reviewer Count",
            _ => field.ToString()
        };
    }

    public string GetCurrentDescription()
    {
        var primary = $"{GetDisplayName(PrimarySortField)} ({(PrimarySortDirection == SortDirection.Ascending ? "A-Z" : "Z-A")})";
        
        if (SecondarySortField.HasValue)
        {
            var secondary = $"{GetDisplayName(SecondarySortField.Value)} ({(SecondarySortDirection == SortDirection.Ascending ? "A-Z" : "Z-A")})";
            primary += $", then {secondary}";
            
            if (TertiarySortField.HasValue)
            {
                var tertiary = $"{GetDisplayName(TertiarySortField.Value)} ({(TertiarySortDirection == SortDirection.Ascending ? "A-Z" : "Z-A")})";
                primary += $", then {tertiary}";
            }
        }
        
        return primary;
    }

    public static string GetSortPresetTooltip(string preset)
    {
        return preset switch
        {
            "Newest First" => "Sort by Created Date (newest first), then by PR ID (highest first).",
            "Oldest First" => "Sort by Created Date (oldest first), then by PR ID (lowest first).",
            "Title A-Z" => "Sort by Title (A-Z), then by Created Date (newest first).",
            "Creator A-Z" => "Sort by Creator name (A-Z), then by Created Date (newest first).",
            "Status Priority" => "Sort by Status priority (Active, Draft, Completed, Abandoned), then by Reviewer Vote, then by Created Date (newest first).",
            "Review Priority" => "Sort by Reviewer Vote priority (Rejected, Waiting, No vote, Approved with suggestions, Approved), then by Created Date (oldest first for urgent attention).",
            "High Activity" => "Sort by Reviewer Count (most reviewers first), then by Created Date (newest first).",
            _ => "Custom sort preset"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}