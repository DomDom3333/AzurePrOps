using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AzurePrOps.Models.FilteringAndSorting;

/// <summary>
/// Handles persistence of filter and sort preferences
/// </summary>
public static class FilterSortPreferencesStorage
{
    private static readonly string StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps");

    private static readonly string PreferencesFile = Path.Combine(StorageDirectory, "filter-sort-preferences.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Tries to load filter and sort preferences synchronously
    /// </summary>
    public static bool TryLoad(out FilterSortPreferences? preferences)
    {
        preferences = null;
        try
        {
            if (!File.Exists(PreferencesFile))
            {
                preferences = new FilterSortPreferences();
                return true;
            }

            var json = File.ReadAllText(PreferencesFile);
            var migratedJson = MigrateLegacyGroupsWithoutVote(json, out var migrated);
            preferences = JsonSerializer.Deserialize<FilterSortPreferences>(migratedJson, JsonOptions) ?? new FilterSortPreferences();

            NormalizePreferences(preferences);

            if (migrated)
            {
                Save(preferences);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading filter preferences: {ex.Message}");
            preferences = new FilterSortPreferences();
            return false;
        }
    }

    /// <summary>
    /// Saves filter and sort preferences synchronously
    /// </summary>
    public static void Save(FilterSortPreferences preferences)
    {
        try
        {
            NormalizePreferences(preferences);
            Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(preferences, JsonOptions);
            File.WriteAllText(PreferencesFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving filter preferences: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads all saved filter/sort preferences asynchronously
    /// </summary>
    public static async Task<List<SavedFilterView>> LoadAllAsync()
    {
        try
        {
            if (!File.Exists(PreferencesFile))
            {
                return new List<SavedFilterView>();
            }

            var json = await File.ReadAllTextAsync(PreferencesFile);
            var migratedJson = MigrateLegacyGroupsWithoutVote(json, out _);
            var preferences = JsonSerializer.Deserialize<FilterSortPreferences>(migratedJson, JsonOptions);
            if (preferences == null)
            {
                return new List<SavedFilterView>();
            }

            NormalizePreferences(preferences);
            return preferences.SavedViews ?? new List<SavedFilterView>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading filter preferences: {ex.Message}");
            return new List<SavedFilterView>();
        }
    }

    /// <summary>
    /// Saves all filter/sort preferences asynchronously
    /// </summary>
    public static async Task SaveAllAsync(List<SavedFilterView> savedViews)
    {
        try
        {
            var preferences = TryLoad(out var existing) ? existing! : new FilterSortPreferences();
            preferences.SavedViews = savedViews;

            NormalizePreferences(preferences);
            Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(preferences, JsonOptions);
            await File.WriteAllTextAsync(PreferencesFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving filter preferences: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Saves a single preference set
    /// </summary>
    public static async Task SavePreferenceAsync(SavedFilterView preference)
    {
        var allViews = await LoadAllAsync();

        // Remove existing preference with same name
        allViews.RemoveAll(p => p.Name.Equals(preference.Name, StringComparison.OrdinalIgnoreCase));

        // Add the new/updated preference
        allViews.Add(preference);

        await SaveAllAsync(allViews);
    }

    /// <summary>
    /// Deletes a preference by name
    /// </summary>
    public static async Task DeletePreferenceAsync(string name)
    {
        var allViews = await LoadAllAsync();
        allViews.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        await SaveAllAsync(allViews);
    }

    /// <summary>
    /// Loads the last used preferences (most recently used preference)
    /// </summary>
    public static async Task<SavedFilterView?> LoadLastUsedAsync()
    {
        var allViews = await LoadAllAsync();

        SavedFilterView? lastUsed = null;
        DateTime latestDate = DateTime.MinValue;

        foreach (var view in allViews)
        {
            if (view.LastUsed > latestDate)
            {
                latestDate = view.LastUsed;
                lastUsed = view;
            }
        }

        return lastUsed;
    }

    /// <summary>
    /// Updates the last used timestamp for a preference
    /// </summary>
    public static async Task UpdateLastUsedAsync(string name)
    {
        var allViews = await LoadAllAsync();
        var view = allViews.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (view != null)
        {
            view.LastUsed = DateTime.Now;
            await SaveAllAsync(allViews);
        }
    }

    private static string MigrateLegacyGroupsWithoutVote(string json, out bool migrated)
    {
        migrated = false;

        var root = JsonNode.Parse(json) as JsonObject;
        if (root == null)
        {
            return json;
        }

        if (MigrateLegacyGroupsWithoutVoteRecursive(root))
        {
            migrated = true;
            return root.ToJsonString(JsonOptions);
        }

        return json;
    }

    private static bool MigrateLegacyGroupsWithoutVoteRecursive(JsonNode node)
    {
        var migrated = false;

        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("groupsWithoutVote", out var legacyGroupsNode))
            {
                var selectedGroupsNode = obj["selectedGroupsWithoutVote"] as JsonArray;
                var selectedGroups = selectedGroupsNode?.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList()
                    ?? new List<string>();

                if (!selectedGroups.Any() && legacyGroupsNode is JsonArray legacyArray)
                {
                    obj["selectedGroupsWithoutVote"] = new JsonArray(legacyArray.Select(x => JsonValue.Create(x?.GetValue<string>())).ToArray());
                    migrated = true;
                }

                obj.Remove("groupsWithoutVote");
                migrated = true;
            }

            foreach (var child in obj.Select(kvp => kvp.Value).Where(v => v != null))
            {
                if (MigrateLegacyGroupsWithoutVoteRecursive(child!))
                {
                    migrated = true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(v => v != null))
            {
                if (MigrateLegacyGroupsWithoutVoteRecursive(child!))
                {
                    migrated = true;
                }
            }
        }

        return migrated;
    }

    private static void NormalizePreferences(FilterSortPreferences preferences)
    {
        preferences.FilterCriteria.SelectedGroupsWithoutVote ??= new List<string>();
        preferences.SavedViews ??= new List<SavedFilterView>();

        foreach (var savedView in preferences.SavedViews)
        {
            savedView.FilterCriteria.SelectedGroupsWithoutVote ??= new List<string>();
        }
    }
}
