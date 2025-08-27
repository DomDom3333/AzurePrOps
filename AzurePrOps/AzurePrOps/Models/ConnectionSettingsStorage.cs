using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AzurePrOps.Services;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.Models;

public static class ConnectionSettingsStorage
{
    private static readonly ILogger _logger = AppLogger.CreateLogger(nameof(ConnectionSettingsStorage));
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "settings.json");
    
    private static readonly SecureCredentialService _credentialService = new();
    private static bool _migrationAttempted = false;

    public static bool TryLoad(out ConnectionSettings? settings)
    {
        settings = null;
        try
        {
            if (!File.Exists(FilePath))
                return false;

            var json = File.ReadAllText(FilePath);
            
            // Try to deserialize with the old format first (for migration)
            // Only attempt migration once per application session
            if (!_migrationAttempted)
            {
                _migrationAttempted = true;
                try
                {
                    var legacySettings = JsonSerializer.Deserialize<LegacyConnectionSettings>(json);
                    if (legacySettings != null && !string.IsNullOrEmpty(legacySettings.PersonalAccessToken))
                    {
                        // Migrate the PAT token to secure storage (only log if migration actually happens)
                        if (_credentialService.StorePersonalAccessToken(legacySettings.PersonalAccessToken, legacySettings.ReviewerId))
                        {
                            // Create new settings without the PAT token
                            settings = new ConnectionSettings(
                                legacySettings.Organization,
                                legacySettings.Project,
                                legacySettings.Repository,
                                legacySettings.ReviewerId,
                                legacySettings.EditorCommand,
                                legacySettings.UseGitDiff,
                                legacySettings.SelectedReviewerGroups,
                                legacySettings.IncludeGroupReviews,
                                legacySettings.SelectedGroupsForFiltering,
                                legacySettings.EnableGroupFiltering,
                                legacySettings.UserDisplayName)
                            {
                                HasSecureToken = true
                            };
                            
                            // Save the updated settings without the PAT token
                            Save(settings);
                            _logger.LogInformation("Personal Access Token migration completed");
                            return true;
                        }
                        else
                        {
                            _logger.LogError("Failed to migrate Personal Access Token to secure storage");
                            return false;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Not legacy format, continue with new format
                }
            }
            
            // Try new format
            settings = JsonSerializer.Deserialize<ConnectionSettings>(json);
            if (settings != null)
            {
                // Update HasSecureToken based on actual credential store state
                bool hasToken = _credentialService.HasPersonalAccessToken();
                if (settings.HasSecureToken != hasToken)
                {
                    settings = settings with { HasSecureToken = hasToken };
                    Save(settings); // Update the file with correct token status
                }
            }
            
            return settings != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading connection settings");
            settings = null;
            return false;
        }
    }

    public static void Save(ConnectionSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Ensure HasSecureToken reflects actual state
            bool hasToken = _credentialService.HasPersonalAccessToken();
            var settingsToSave = settings with { HasSecureToken = hasToken };
            
            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving connection settings");
        }
    }

    public static async Task SaveAsync(ConnectionSettings connectionSettings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Ensure HasSecureToken reflects actual state
            bool hasToken = _credentialService.HasPersonalAccessToken();
            var settingsToSave = connectionSettings with { HasSecureToken = hasToken };

            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving connection settings asynchronously");
        }
    }
    
    /// <summary>
    /// Saves a Personal Access Token securely using Windows Credential Manager
    /// </summary>
    public static bool SavePersonalAccessToken(string token, string reviewerId)
    {
        return _credentialService.StorePersonalAccessToken(token, reviewerId);
    }
    
    /// <summary>
    /// Retrieves the Personal Access Token from secure storage
    /// </summary>
    public static string? GetPersonalAccessToken()
    {
        return _credentialService.GetPersonalAccessToken();
    }
    
    /// <summary>
    /// Removes the Personal Access Token from secure storage
    /// </summary>
    public static bool RemovePersonalAccessToken()
    {
        return _credentialService.RemovePersonalAccessToken();
    }
    
    /// <summary>
    /// Clears all secure credentials and connection settings
    /// </summary>
    public static void ClearSecureCredentials()
    {
        try
        {
            // Remove PAT from secure storage
            _credentialService.RemovePersonalAccessToken();
            
            // Delete the settings file
            Delete();
            
            _logger.LogInformation("Secure credentials and settings cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing secure credentials");
        }
    }
    
    /// <summary>
    /// Deletes the connection settings file
    /// </summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
                _logger.LogInformation("Connection settings file deleted");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting connection settings file");
        }
    }
}

// Legacy format for migration purposes
internal record LegacyConnectionSettings(
    string Organization,
    string Project,
    string Repository,
    string PersonalAccessToken,
    string ReviewerId,
    string EditorCommand = "code",
    bool UseGitDiff = true,
    List<string>? SelectedReviewerGroups = null,
    bool IncludeGroupReviews = true,
    List<string>? SelectedGroupsForFiltering = null,
    bool EnableGroupFiltering = false,
    string UserDisplayName = "");
