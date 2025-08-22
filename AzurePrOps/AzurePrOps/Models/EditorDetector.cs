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
            var fullPath = GetEditorFullPath(cmd);
            if (!string.IsNullOrEmpty(fullPath))
                result.Add(fullPath);
        }
        return result;
    }

    public static string GetDefaultEditor() => GetAvailableEditors().FirstOrDefault() ?? "code";

    public static string GetEditorFullPath(string command)
    {
        // First check PATH
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            var full = Path.Combine(p, command);
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(full))
                    return full;
                if (File.Exists(full + ".exe"))
                    return full + ".exe";
            }
            else if (File.Exists(full))
            {
                return full;
            }
        }

        // If not found in PATH, check common installation locations (Windows)
        if (OperatingSystem.IsWindows())
        {
            var commonLocations = GetCommonInstallationPaths(command);
            foreach (var location in commonLocations)
            {
                if (File.Exists(location))
                    return location;
            }
        }

        return string.Empty;
    }

    private static bool IsCommandAvailable(string command)
    {
        // First check PATH
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

        // If not found in PATH, check common installation locations (Windows)
        if (OperatingSystem.IsWindows())
        {
            var commonLocations = GetCommonInstallationPaths(command);
            foreach (var location in commonLocations)
            {
                if (File.Exists(location))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetCommonInstallationPaths(string command)
    {
        var locations = new List<string>();
        
        switch (command.ToLowerInvariant())
        {
            case "code":
                locations.AddRange(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "bin", "code.cmd"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "bin", "code.cmd"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "bin", "code.cmd"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe")
                });
                break;

            case "code-insiders":
                locations.AddRange(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code Insiders", "bin", "code-insiders.cmd"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code Insiders", "bin", "code-insiders.cmd"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code Insiders", "Code - Insiders.exe")
                });
                break;

            case "rider":
            case "rider64":
                var toolboxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetBrains", "Toolbox", "apps", "Rider");
                if (Directory.Exists(toolboxPath))
                {
                    foreach (var versionDir in Directory.GetDirectories(toolboxPath))
                    {
                        locations.Add(Path.Combine(versionDir, "bin", "rider64.exe"));
                    }
                }
                locations.AddRange(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JetBrains", "JetBrains Rider", "bin", "rider64.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetBrains", "JetBrains Rider", "bin", "rider64.exe")
                });
                break;

            case "subl":
                locations.AddRange(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sublime Text", "subl.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sublime Text 3", "subl.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sublime Text 4", "subl.exe")
                });
                break;

            case "notepad++":
                locations.AddRange(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Notepad++", "notepad++.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Notepad++", "notepad++.exe")
                });
                break;

            case "devenv":
                // Visual Studio locations
                var vsWhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (File.Exists(vsWhere))
                {
                    try
                    {
                        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = vsWhere,
                            Arguments = "-latest -products * -requires Microsoft.Component.MSBuild -property installationPath",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });
                        if (process != null)
                        {
                            var output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            if (!string.IsNullOrWhiteSpace(output))
                            {
                                locations.Add(Path.Combine(output.Trim(), "Common7", "IDE", "devenv.exe"));
                            }
                        }
                    }
                    catch
                    {
                        // Fallback to common locations if vswhere fails
                    }
                }
                
                // Fallback locations for Visual Studio
                locations.AddRange(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Enterprise", "Common7", "IDE", "devenv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Professional", "Common7", "IDE", "devenv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Community", "Common7", "IDE", "devenv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2019", "Enterprise", "Common7", "IDE", "devenv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2019", "Professional", "Common7", "IDE", "devenv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2019", "Community", "Common7", "IDE", "devenv.exe")
                });
                break;
        }

        return locations;
    }
}
