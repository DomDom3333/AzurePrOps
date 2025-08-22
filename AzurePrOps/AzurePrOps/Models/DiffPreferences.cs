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
    private static bool _ignoreNewlines;
    private static bool _expandAllOnOpen;

    static DiffPreferences()
    {
        var loaded = DiffPreferencesStorage.Load();
        _ignoreWhitespace = loaded.IgnoreWhitespace;
        _wrapLines = loaded.WrapLines;
        _ignoreNewlines = loaded.IgnoreNewlines;
        _expandAllOnOpen = loaded.ExpandAllOnOpen;
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
                DiffPreferencesStorage.Save(new DiffPreferencesData(_ignoreWhitespace, _wrapLines, _ignoreNewlines, _expandAllOnOpen));
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
                DiffPreferencesStorage.Save(new DiffPreferencesData(_ignoreWhitespace, _wrapLines, _ignoreNewlines, _expandAllOnOpen));
            }
        }
    }

    /// <summary>
    /// When true, CR/LF vs LF-only differences are ignored (normalize line endings).
    /// </summary>
    public static bool IgnoreNewlines
    {
        get => _ignoreNewlines;
        set
        {
            if (_ignoreNewlines != value)
            {
                _ignoreNewlines = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                DiffPreferencesStorage.Save(new DiffPreferencesData(_ignoreWhitespace, _wrapLines, _ignoreNewlines, _expandAllOnOpen));
            }
        }
    }

    /// <summary>
    /// When true, all file diffs will be expanded automatically when opening the PR details view.
    /// </summary>
    public static bool ExpandAllOnOpen
    {
        get => _expandAllOnOpen;
        set
        {
            if (_expandAllOnOpen != value)
            {
                _expandAllOnOpen = value;
                PreferencesChanged?.Invoke(null, EventArgs.Empty);
                DiffPreferencesStorage.Save(new DiffPreferencesData(_ignoreWhitespace, _wrapLines, _ignoreNewlines, _expandAllOnOpen));
            }
        }
    }
}
