namespace AzurePrOps.Models;

/// <summary>
/// Represents the primary role of the user in the development process.
/// This affects how workflow presets behave and which filters are applied.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Developer who creates and reviews pull requests
    /// </summary>
    Developer,
    
    /// <summary>
    /// QA/Tester who tests approved pull requests
    /// </summary>
    Tester,
    
    /// <summary>
    /// Team Lead or Product Owner who oversees team work
    /// </summary>
    TeamLead,
    
    /// <summary>
    /// Solution Architect who reviews architectural aspects
    /// </summary>
    Architect,
    
    /// <summary>
    /// Scrum Master who tracks team progress
    /// </summary>
    ScrumMaster,
    
    /// <summary>
    /// General team member with mixed responsibilities
    /// </summary>
    General
}

/// <summary>
/// Extensions for UserRole enum to provide display names and descriptions
/// </summary>
public static class UserRoleExtensions
{
    public static string GetDisplayName(this UserRole role) => role switch
    {
        UserRole.Developer => "Developer",
        UserRole.Tester => "QA/Tester",
        UserRole.TeamLead => "Team Lead/PO",
        UserRole.Architect => "Solution Architect",
        UserRole.ScrumMaster => "Scrum Master",
        UserRole.General => "General",
        _ => role.ToString()
    };

    public static string GetDescription(this UserRole role) => role switch
    {
        UserRole.Developer => "Creates and reviews pull requests, focuses on personal workflow",
        UserRole.Tester => "Tests approved pull requests, focuses on PRs ready for testing",
        UserRole.TeamLead => "Oversees team work, focuses on team progress and bottlenecks",
        UserRole.Architect => "Reviews architectural aspects, focuses on design decisions",
        UserRole.ScrumMaster => "Tracks team progress, focuses on workflow and blockers",
        UserRole.General => "Mixed responsibilities, general workflow view",
        _ => "User role"
    };
}
