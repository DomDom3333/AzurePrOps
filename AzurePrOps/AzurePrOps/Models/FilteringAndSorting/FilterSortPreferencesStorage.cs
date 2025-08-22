using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AzurePrOps.Infrastructure;
using AzurePrOps.Logging;

namespace AzurePrOps.Models.FilteringAndSorting;

public class FilterSortPreferencesStorage
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<FilterSortPreferencesStorage>();
    private static readonly string _preferencesFileName = "filter-sort-preferences.json";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string PreferencesFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        _preferencesFileName);

    public static bool TryLoad(out FilterSortPreferences? preferences)
    {
        preferences = null;
        try
        {
            var filePath = PreferencesFilePath;
            if (!File.Exists(filePath))
            {
                _logger.LogInformation("Filter/Sort preferences file not found, will create default");
                preferences = CreateDefaultPreferences();
                return true;
            }

            var json = File.ReadAllText(filePath);
            preferences = JsonSerializer.Deserialize<FilterSortPreferences>(json, _jsonOptions);
            
            if (preferences == null)
            {
                _logger.LogWarning("Failed to deserialize filter/sort preferences, creating default");
                preferences = CreateDefaultPreferences();
                return true;
            }

            _logger.LogInformation("Successfully loaded filter/sort preferences");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load filter/sort preferences");
            preferences = CreateDefaultPreferences();
            return false;
        }
    }

    public static async Task<(bool Success, FilterSortPreferences? Preferences)> TryLoadAsync()
    {
        try
        {
            var filePath = PreferencesFilePath;
            if (!File.Exists(filePath))
            {
                _logger.LogInformation("Filter/Sort preferences file not found, will create default");
                return (true, CreateDefaultPreferences());
            }

            var json = await File.ReadAllTextAsync(filePath);
            var preferences = JsonSerializer.Deserialize<FilterSortPreferences>(json, _jsonOptions);
            
            if (preferences == null)
            {
                _logger.LogWarning("Failed to deserialize filter/sort preferences, creating default");
                return (true, CreateDefaultPreferences());
            }

            _logger.LogInformation("Successfully loaded filter/sort preferences");
            return (true, preferences);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load filter/sort preferences");
            return (false, CreateDefaultPreferences());
        }
    }

    public static bool Save(FilterSortPreferences preferences)
    {
        try
        {
            var filePath = PreferencesFilePath;
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            preferences.LastUpdated = DateTime.Now;
            var json = JsonSerializer.Serialize(preferences, _jsonOptions);
            File.WriteAllText(filePath, json);
            
            _logger.LogInformation("Successfully saved filter/sort preferences");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save filter/sort preferences");
            return false;
        }
    }

    public static async Task<bool> SaveAsync(FilterSortPreferences preferences)
    {
        try
        {
            var filePath = PreferencesFilePath;
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            preferences.LastUpdated = DateTime.Now;
            var json = JsonSerializer.Serialize(preferences, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("Successfully saved filter/sort preferences");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save filter/sort preferences");
            return false;
        }
    }

    private static FilterSortPreferences CreateDefaultPreferences()
    {
        var preferences = new FilterSortPreferences();
        
        // Create default saved views for different roles
        var defaultViews = new List<SavedFilterView>
        {
            new SavedFilterView(
                "All Pull Requests", 
                new FilterCriteriaData { WorkflowPreset = "All" },
                new SortCriteriaData { CurrentPreset = "Newest First" },
                "Shows all pull requests, newest first",
                "All")
            { IsDefault = true },

            new SavedFilterView(
                "My Pull Requests", 
                new FilterCriteriaData { MyPullRequestsOnly = true, WorkflowPreset = "Developer" },
                new SortCriteriaData { CurrentPreset = "Newest First" },
                "Shows only pull requests created by me",
                "Developer"),

            new SavedFilterView(
                "Needs My Review", 
                new FilterCriteriaData { NeedsMyReviewOnly = true, WorkflowPreset = "Reviewer" },
                new SortCriteriaData { CurrentPreset = "Review Priority" },
                "Shows pull requests that need my review, prioritized by urgency",
                "Reviewer"),

            new SavedFilterView(
                "Active PRs", 
                new FilterCriteriaData { SelectedStatuses = new List<string> { "Active" }, WorkflowPreset = "All" },
                new SortCriteriaData { CurrentPreset = "Status Priority" },
                "Shows only active pull requests",
                "All"),

            new SavedFilterView(
                "Team Lead Overview", 
                new FilterCriteriaData 
                { 
                    SelectedStatuses = new List<string> { "Active", "Completed" },
                    CreatedAfter = DateTimeOffset.Now.AddDays(-30),
                    WorkflowPreset = "TeamLead"
                },
                new SortCriteriaData { CurrentPreset = "Status Priority" },
                "Shows recent active and completed PRs for team oversight",
                "TeamLead"),

            new SavedFilterView(
                "QA Ready", 
                new FilterCriteriaData 
                { 
                    SelectedReviewerVotes = new List<string> { "Approved" },
                    SelectedStatuses = new List<string> { "Active" },
                    WorkflowPreset = "QA"
                },
                new SortCriteriaData { CurrentPreset = "Oldest First" },
                "Shows approved PRs ready for QA review, oldest first",
                "QA"),

            new SavedFilterView(
                "High Activity", 
                new FilterCriteriaData { WorkflowPreset = "All" },
                new SortCriteriaData { CurrentPreset = "High Activity" },
                "Shows PRs with most reviewer activity",
                "All")
        };

        preferences.SavedViews = defaultViews;
        preferences.LastSelectedView = "All Pull Requests";

        return preferences;
    }

    public static void DeletePreferences()
    {
        try
        {
            var filePath = PreferencesFilePath;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Successfully deleted filter/sort preferences");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete filter/sort preferences");
        }
    }

    public static bool BackupPreferences()
    {
        try
        {
            var filePath = PreferencesFilePath;
            if (!File.Exists(filePath))
                return true;

            var backupPath = filePath + $".backup.{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(filePath, backupPath);
            
            _logger.LogInformation("Successfully backed up filter/sort preferences to {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup filter/sort preferences");
            return false;
        }
    }
}