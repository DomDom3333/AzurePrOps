using System;
using System.IO;

namespace AzurePrOps.Models;

/// <summary>
/// Stores diff viewer preferences that apply across the application.
/// The <see cref="PreferencesChanged"/> event is raised whenever a value changes
/// so open views can update themselves.
/// </summary>
public static class DiffPreferences
{
    private static bool _ignoreWhitespace;
    private static bool _wrapLines;

    static DiffPreferences()
    {
        var loaded = DiffPreferencesStorage.Load();
        _ignoreWhitespace = loaded.IgnoreWhitespace;
        _wrapLines = loaded.WrapLines;
    }

    /// <summary>
    /// Raised whenever any preference value changes.
    /// </summary>
    public static event EventHandler? PreferencesChanged;

    /// <summary>
    /// When true, whitespace differences will be ignored when generating diffs.
    /// </summary>
    public static bool IgnoreWhitespace
    {
        get => _ignoreWhitespace;
        set
        {
            if (_ignoreWhitespace != value)
            {
                _ignoreWhitespace = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                DiffPreferencesStorage.Save(new DiffPreferencesData(_ignoreWhitespace, _wrapLines));
            }
        }
    }

    /// <summary>
    /// Controls line wrapping in the diff editors.
    /// </summary>
    public static bool WrapLines
    {
        get => _wrapLines;
        set
        {
            if (_wrapLines != value)
            {
                _wrapLines = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                DiffPreferencesStorage.Save(new DiffPreferencesData(_ignoreWhitespace, _wrapLines));
            }
        }
    }
}
