namespace AzurePrOps.Models;

using AzurePrOps.Models.FilteringAndSorting;

public class FilterView
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FilterCriteria? FilterCriteria { get; set; }
    
    // Legacy properties for backward compatibility
    public string Title { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
