using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AzurePrOps.Models;

public static class EditorDetector
{
    private static readonly string[] CandidateCommands = new[]
    {
        // Common editors
        "code",            // Visual Studio Code
        "code-insiders",   // VS Code Insiders
        "rider",           // JetBrains Rider
        "rider64",
        "subl",            // Sublime Text
        "notepad++",
        "notepad",
        "gedit",
        "vim",
        "vi",
        "nano",
        "emacs",

        // JetBrains IDEs
        "idea",            // IntelliJ IDEA
        "idea64",

        // Android Studio
        "studio",          // linux/mac script name
        "studio64",

        // Visual Studio (Windows)
        "devenv"          // Visual Studio IDE
    };

    public static IReadOnlyList<string> GetAvailableEditors()
    {
        var result = new List<string>();
        foreach (var cmd in CandidateCommands)
        {
            if (IsCommandAvailable(cmd))
                result.Add(cmd);
        }
        return result;
    }

    public static string GetDefaultEditor() => GetAvailableEditors().FirstOrDefault() ?? "code";

    private static bool IsCommandAvailable(string command)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            var full = Path.Combine(p, command);
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(full) || File.Exists(full + ".exe"))
                    return true;
            }
            else if (File.Exists(full))
            {
                return true;
            }
        }
        return false;
    }
}
