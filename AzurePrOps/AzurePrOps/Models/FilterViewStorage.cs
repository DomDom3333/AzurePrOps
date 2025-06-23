using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AzurePrOps.Models;

public static class FilterViewStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "views.json");

    public static IReadOnlyList<FilterView> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Array.Empty<FilterView>();

            var json = File.ReadAllText(FilePath);
            var views = JsonSerializer.Deserialize<List<FilterView>>(json);
            return views ?? new List<FilterView>();
        }
        catch
        {
            return Array.Empty<FilterView>();
        }
    }

    public static void Save(IEnumerable<FilterView> views)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(views.ToList());
        File.WriteAllText(FilePath, json);
    }
}
