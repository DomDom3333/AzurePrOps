using System;

namespace AzurePrOps.Models;

/// <summary>
/// Stores UI/interface preferences that apply across the application.
/// The <see cref="PreferencesChanged"/> event is raised whenever a value changes
/// so open views can update themselves.
/// </summary>
public static class UIPreferences
{
    private static bool _autoRefreshEnabled = true;
    private static int _selectedThemeIndex = 0;
    private static int _refreshIntervalSeconds = 60;
    private static bool _showNotifications = true;
    private static bool _minimizeToTray = false;

    static UIPreferences()
    {
        var loaded = UIPreferencesStorage.Load();
        _autoRefreshEnabled = loaded.AutoRefreshEnabled;
        _selectedThemeIndex = loaded.SelectedThemeIndex;
        _refreshIntervalSeconds = loaded.RefreshIntervalSeconds;
        _showNotifications = loaded.ShowNotifications;
        _minimizeToTray = loaded.MinimizeToTray;
    }

    /// <summary>
    /// Raised whenever any preference value changes.
    /// </summary>
    public static event EventHandler? PreferencesChanged;

    /// <summary>
    /// When true, pull requests will be automatically refreshed at regular intervals.
    /// </summary>
    public static bool AutoRefreshEnabled
    {
        get => _autoRefreshEnabled;
        set
        {
            if (_autoRefreshEnabled != value)
            {
                _autoRefreshEnabled = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                UIPreferencesStorage.Save(new UIPreferencesData(_autoRefreshEnabled, _selectedThemeIndex, _refreshIntervalSeconds, _showNotifications, _minimizeToTray));
            }
        }
    }

    /// <summary>
    /// Selected theme index: 0 = System, 1 = Light, 2 = Dark.
    /// </summary>
    public static int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set
        {
            if (_selectedThemeIndex != value)
            {
                _selectedThemeIndex = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                UIPreferencesStorage.Save(new UIPreferencesData(_autoRefreshEnabled, _selectedThemeIndex, _refreshIntervalSeconds, _showNotifications, _minimizeToTray));
            }
        }
    }

    /// <summary>
    /// Auto-refresh interval in seconds.
    /// </summary>
    public static int RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set
        {
            if (_refreshIntervalSeconds != value)
            {
                _refreshIntervalSeconds = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                UIPreferencesStorage.Save(new UIPreferencesData(_autoRefreshEnabled, _selectedThemeIndex, _refreshIntervalSeconds, _showNotifications, _minimizeToTray));
            }
        }
    }

    /// <summary>
    /// When true, desktop notifications will be shown for pull request updates.
    /// </summary>
    public static bool ShowNotifications
    {
        get => _showNotifications;
        set
        {
            if (_showNotifications != value)
            {
                _showNotifications = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                UIPreferencesStorage.Save(new UIPreferencesData(_autoRefreshEnabled, _selectedThemeIndex, _refreshIntervalSeconds, _showNotifications, _minimizeToTray));
            }
        }
    }

    /// <summary>
    /// When true, application will minimize to system tray instead of taskbar.
    /// </summary>
    public static bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray != value)
            {
                _minimizeToTray = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                UIPreferencesStorage.Save(new UIPreferencesData(_autoRefreshEnabled, _selectedThemeIndex, _refreshIntervalSeconds, _showNotifications, _minimizeToTray));
            }
        }
    }
}