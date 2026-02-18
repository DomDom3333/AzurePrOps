using System;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.Styling;
using AzurePrOps.Models;

namespace AzurePrOps.Services;

/// <summary>
/// Manages application theme switching between Light, Dark, and System themes.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// Initializes theme management by setting the current theme based on saved preferences
    /// and subscribing to theme changes.
    /// </summary>
    public static void Initialize()
    {
        // Set initial theme based on saved preferences
        ApplyTheme(UIPreferences.SelectedThemeIndex);
        
        // Subscribe to preference changes
        UIPreferences.PreferencesChanged += OnPreferencesChanged;
    }

    /// <summary>
    /// Handles preference changes and applies theme updates when needed.
    /// </summary>
    private static void OnPreferencesChanged(object? sender, EventArgs e)
    {
        ApplyTheme(UIPreferences.SelectedThemeIndex);
    }

    /// <summary>
    /// Applies the specified theme to the application.
    /// </summary>
    /// <param name="themeIndex">Theme index: 0 = System, 1 = Light, 2 = Dark</param>
    public static void ApplyTheme(int themeIndex)
    {
        if (Application.Current == null) return;

        var themeVariant = themeIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default // System theme
        };

        // Apply the theme variant
        Application.Current.RequestedThemeVariant = themeVariant;

        // Load appropriate color resources
        LoadThemeResources(themeIndex);
    }

    /// <summary>
    /// Loads the appropriate theme resources (light or dark color palette).
    /// </summary>
    private static void LoadThemeResources(int themeIndex)
    {
        if (Application.Current == null) return;

        // Remove existing custom theme resources
        RemoveCustomThemeResources();

        // Determine which theme to load based on the theme index and system theme
        var shouldUseDarkTheme = themeIndex switch
        {
            1 => false, // Light theme
            2 => true,  // Dark theme
            _ => IsSystemDarkTheme() // System theme - check system preference
        };

        if (shouldUseDarkTheme)
        {
            LoadDarkThemeResources();
        }
        else
        {
            LoadLightThemeResources();
        }
    }

    /// <summary>
    /// Loads dark theme color resources.
    /// </summary>
    private static void LoadDarkThemeResources()
    {
        try
        {
            var darkThemeUri = new Uri("avares://AzurePrOps/Styles/DarkTheme.xaml");
            var darkThemeStyle = new StyleInclude(darkThemeUri)
            {
                Source = darkThemeUri
            };
            
            // Add dark theme resources to application styles
            Application.Current?.Styles.Add(darkThemeStyle);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading dark theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads light theme color resources (default - no additional resources needed).
    /// </summary>
    private static void LoadLightThemeResources()
    {
        // Light theme is the default in Styles.xaml, so no additional loading needed
        // This method is here for symmetry and future extensibility
    }

    /// <summary>
    /// Removes any previously loaded custom theme resources.
    /// </summary>
    private static void RemoveCustomThemeResources()
    {
        if (Application.Current?.Styles == null) return;

        // Remove any previously loaded dark theme styles
        for (int i = Application.Current.Styles.Count - 1; i >= 0; i--)
        {
            if (Application.Current.Styles[i] is StyleInclude styleInclude &&
                styleInclude.Source?.ToString().Contains("DarkTheme.xaml") == true)
            {
                Application.Current.Styles.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Determines if the system is currently using a dark theme.
    /// </summary>
    private static bool IsSystemDarkTheme()
    {
        // On Windows, we can check if the system theme is dark
        // For now, we'll default to light theme when system preference is unknown
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Check Windows registry for system theme preference
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var systemUsesLightTheme = key.GetValue("SystemUsesLightTheme");
                    if (systemUsesLightTheme is int lightTheme)
                    {
                        return lightTheme == 0; // 0 = dark theme, 1 = light theme
                    }
                }
            }
        }
        catch
        {
            // If we can't determine system theme, default to light
        }

        return false; // Default to light theme
    }

    /// <summary>
    /// Gets the current theme name for display purposes.
    /// </summary>
    public static string GetCurrentThemeName()
    {
        return UIPreferences.SelectedThemeIndex switch
        {
            1 => "Light",
            2 => "Dark", 
            _ => "System"
        };
    }
}