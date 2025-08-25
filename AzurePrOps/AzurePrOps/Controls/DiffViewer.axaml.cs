using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Folding;
using Avalonia.VisualTree;
using Avalonia.Input;
using System.Threading.Tasks;
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.ReviewLogic.Services;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using AzurePrOps.Infrastructure;
using Markdown.Avalonia;
using System.IO;
using System.Text;

namespace AzurePrOps.Controls
{
    public enum DiffViewMode { Unified, SideBySide }

    public partial class DiffViewer : UserControl
    {
        private static readonly ILogger _logger = AppLogger.CreateLogger<DiffViewer>();

        // Dependency properties
        public static readonly StyledProperty<string> OldTextProperty =
            AvaloniaProperty.Register<DiffViewer, string>(nameof(OldText));
        public static readonly StyledProperty<string> NewTextProperty =
            AvaloniaProperty.Register<DiffViewer, string>(nameof(NewText));
        public static readonly StyledProperty<DiffViewMode> ViewModeProperty =
            AvaloniaProperty.Register<DiffViewer, DiffViewMode>(nameof(ViewMode), DiffViewMode.SideBySide);
        public static readonly StyledProperty<int> PullRequestIdProperty =
            AvaloniaProperty.Register<DiffViewer, int>(nameof(PullRequestId));

        // Services (injected via DI)
        public ICommentProvider CommentProvider { get; set; }
        public IPullRequestService PullRequestService { get; set; }
        public ILintingService LintingService { get; set; }
        public IBlameService BlameService { get; set; }
        public IAuditTrailService AuditService { get; set; } = default!;
        public INotificationService NotificationService { get; set; } = default!;
        public IMetricsService MetricsService { get; set; } = default!;
        public ISuggestionService SuggestionService { get; set; } = default!;
        public IPatchService PatchService { get; set; } = default!;
        public ICodeFoldingService FoldingService { get; set; } = default!;
        public ISearchService SearchService { get; set; } = default!;
        public IIDEIntegrationService IDEService { get; set; } = default!;
        public ICommentsService CommentsService { get; set; } = default!;

        // Enhanced UI elements
        private TextBox? _searchBox;
        private StackPanel? _metricsPanel;
        private TextEditor? _oldEditor;
        private TextEditor? _newEditor;
        private TextBlock? _addedLinesText;
        private TextBlock? _removedLinesText;
        private TextBlock? _modifiedLinesText;
        private TextBlock? _statusText;
        private TextBlock? _positionText;
        private TextBlock? _selectionText;
        private TextBlock? _oldFileInfo;
        private TextBlock? _newFileInfo;

        // Diff tracking
        private Dictionary<int, DiffLineType> _lineTypes = new();
        private Dictionary<int, DiffLineType> _oldLineTypes = new();
        private Dictionary<int, DiffLineType> _newLineTypes = new();
        private List<int> _changedLines = new();
        private int _currentChangeIndex = -1;
        private bool _codeFoldingEnabled = true;
        private bool _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
        private bool _wrapLines = DiffPreferences.WrapLines;
        private bool _ignoreNewlines = DiffPreferences.IgnoreNewlines;
        private ToggleButton? _ignoreWhitespaceButton;
        private ToggleButton? _ignoreNewlinesButton;
        private ToggleButton? _wrapLinesButton;
        private ScrollViewer? _oldScrollViewer;
        private ScrollViewer? _newScrollViewer;
        private FoldingManager? _oldFoldingManager;
        private FoldingManager? _newFoldingManager;
        private bool _syncScrollInProgress = false;
        private List<int> _searchMatches = new();
        private int _currentSearchIndex = -1;
        private Dictionary<int, CommentThread> _threadsByLine = new();
        private Popup? _commentPopup;

        // Line alignment support for better diff comparison
        private Dictionary<int, int> _lineMapping = new(); // Maps old editor lines to new editor lines
        private Dictionary<int, int> _reverseLineMapping = new(); // Maps new editor lines to old editor lines
        private bool _lineAlignmentEnabled = true;
        private ToggleButton? _lineAlignmentButton;

        static DiffViewer()
        {
            ViewModeProperty.Changed.AddClassHandler<DiffViewer>((x, e) =>
            {
                _logger.LogDebug("DiffViewer ViewMode changed: {Old} -> {New}", e.OldValue, e.NewValue);
                x.Render();
            });
            OldTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) =>
            {
                _logger.LogDebug("DiffViewer OldText changed: Length = {Length}", (e.NewValue as string)?.Length ?? 0);
                x.Render();
            });
            NewTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) =>
            {
                _logger.LogDebug("DiffViewer NewText changed: Length = {Length}", (e.NewValue as string)?.Length ?? 0);
                x.Render();
            });
        }

        public string OldText { get => GetValue(OldTextProperty); set => SetValue(OldTextProperty, value); }
        public string NewText { get => GetValue(NewTextProperty); set => SetValue(NewTextProperty, value); }
        public DiffViewMode ViewMode { get => GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }
        public int PullRequestId { get => GetValue(PullRequestIdProperty); set => SetValue(PullRequestIdProperty, value); }

        public DiffViewer()
        {
            // Provide simple default implementations so the control works
            CommentProvider = new InMemoryCommentProvider();
            PullRequestService = PullRequestServiceFactory.Create(PullRequestServiceType.AzureDevOps);
            LintingService = new RoslynLintingService();
            BlameService = new GitBlameService(Environment.CurrentDirectory);
            AuditService = new FileAuditTrailService();
            NotificationService = new AvaloniaNotificationService();
            MetricsService = new SimpleMetricsService();
            SuggestionService = new SimpleSuggestionService();
            PatchService = new FilePatchService();
            FoldingService = new IndentationFoldingService();
            SearchService = new SimpleSearchService();
            CommentsService = ServiceRegistry.Resolve<ICommentsService>() ?? new CommentsService(new AzureDevOpsClient());
            string editor = GetValidEditorCommand();
            IDEService = new IDEIntegrationService(editor);

            // Apply persisted preferences
            _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
            _wrapLines = DiffPreferences.WrapLines;
            DiffPreferences.PreferencesChanged += OnPreferencesChanged;

            InitializeComponent();
            Loaded += (_, __) => SetupEditors();
            Unloaded += (_, __) => DiffPreferences.PreferencesChanged -= OnPreferencesChanged;
        }

        private string GetValidEditorCommand()
        {
            // First check if we have stored settings
            if (ConnectionSettingsStorage.TryLoad(out var settings) && !string.IsNullOrWhiteSpace(settings.EditorCommand))
            {
                var storedEditor = settings.EditorCommand;
                
                // If it's already a full path and exists, use it
                if (Path.IsPathRooted(storedEditor) && File.Exists(storedEditor))
                {
                    return storedEditor;
                }
                
                // If it's just a command name, try to get the full path
                var fullPath = EditorDetector.GetEditorFullPath(storedEditor);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    return fullPath;
                }
            }
            
            // Fall back to EditorDetector default
            return EditorDetector.GetDefaultEditor();
        }

        private void OnPreferencesChanged(object? sender, EventArgs e)
        {
            _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
            _wrapLines = DiffPreferences.WrapLines;
            _ignoreNewlines = DiffPreferences.IgnoreNewlines;

            if (_ignoreWhitespaceButton != null)
                _ignoreWhitespaceButton.IsChecked = _ignoreWhitespace;
            if (_ignoreNewlinesButton != null)
                _ignoreNewlinesButton.IsChecked = _ignoreNewlines;
            if (_wrapLinesButton != null)
                _wrapLinesButton.IsChecked = _wrapLines;

            if (_oldEditor != null)
                _oldEditor.WordWrap = _wrapLines;
            if (_newEditor != null)
                _newEditor.WordWrap = _wrapLines;

            Render();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is FileDiff diff)
            {
                OldText = diff.OldText ?? string.Empty;
                NewText = diff.NewText ?? string.Empty;
                UpdateFileInfo(diff);
                LoadThreadsAsync(diff);
            }
            else
            {
                OldText = string.Empty;
                NewText = string.Empty;
                UpdateFileInfo(null);
            }
        }

        private void UpdateFileInfo(FileDiff? diff)
        {
            if (diff != null)
            {
                var fileName = Path.GetFileName(diff.FilePath);
                var directory = Path.GetDirectoryName(diff.FilePath);

                if (_oldFileInfo != null)
                    _oldFileInfo.Text = $"{fileName} • {directory}";
                if (_newFileInfo != null)
                    _newFileInfo.Text = $"{fileName} • {directory}";

                UpdateStatus($"Viewing {fileName}");
            }
            else
            {
                if (_oldFileInfo != null)
                    _oldFileInfo.Text = "";
                if (_newFileInfo != null)
                    _newFileInfo.Text = "";

                UpdateStatus("Ready");
            }
        }

        private void UpdateStatus(string message)
        {
            if (_statusText != null)
                _statusText.Text = message;
        }

        private void UpdatePosition()
        {
            var activeEditor = GetActiveEditor();
            if (activeEditor != null && _positionText != null)
            {
                var line = activeEditor.TextArea.Caret.Line;
                var column = activeEditor.TextArea.Caret.Column;
                _positionText.Text = $"Ln {line}, Col {column}";
            }
        }

        private void UpdateSelection()
        {
            var activeEditor = GetActiveEditor();
            if (activeEditor != null && _selectionText != null)
            {
                var selection = activeEditor.TextArea.Selection;
                if (selection.Length > 0)
                {
                    _selectionText.Text = $"{selection.Length} chars selected";
                }
                else
                {
                    _selectionText.Text = "";
                }
            }
        }

        private TextEditor? GetActiveEditor()
        {
            if (_newEditor?.IsFocused == true) return _newEditor;
            if (_oldEditor?.IsFocused == true) return _oldEditor;
            return _newEditor; // Default to new editor
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // Find UI elements with enhanced references
            _oldEditor = this.FindControl<TextEditor>("OldEditor");
            _newEditor = this.FindControl<TextEditor>("NewEditor");
            _searchBox = this.FindControl<TextBox>("PART_SearchBox");
            _metricsPanel = this.FindControl<StackPanel>("PART_MetricsPanel");
            _addedLinesText = this.FindControl<TextBlock>("PART_AddedLinesText");
            _removedLinesText = this.FindControl<TextBlock>("PART_RemovedLinesText");
            _modifiedLinesText = this.FindControl<TextBlock>("PART_ModifiedLinesText");
            _statusText = this.FindControl<TextBlock>("PART_StatusText");
            _positionText = this.FindControl<TextBlock>("PART_PositionText");
            _selectionText = this.FindControl<TextBlock>("PART_SelectionText");
            _oldFileInfo = this.FindControl<TextBlock>("PART_OldFileInfo");
            _newFileInfo = this.FindControl<TextBlock>("PART_NewFileInfo");

            // Find buttons
            var sideBySideButton = this.FindControl<ToggleButton>("PART_SideBySideButton");
            var unifiedButton = this.FindControl<ToggleButton>("PART_UnifiedButton");
            var nextChangeButton = this.FindControl<Button>("PART_NextChangeButton");
            var prevChangeButton = this.FindControl<Button>("PART_PrevChangeButton");
            var nextSearchButton = this.FindControl<Button>("PART_NextSearchButton");
            var prevSearchButton = this.FindControl<Button>("PART_PrevSearchButton");
            var openIdeButton = this.FindControl<Button>("PART_OpenInIDEButton");
            var codeFoldingButton = this.FindControl<ToggleButton>("PART_CodeFoldingButton");
            var copyButton = this.FindControl<Button>("PART_CopyButton");
            _ignoreWhitespaceButton = this.FindControl<ToggleButton>("PART_IgnoreWhitespaceButton");
            _ignoreNewlinesButton = this.FindControl<ToggleButton>("PART_IgnoreNewlinesButton");
            _wrapLinesButton = this.FindControl<ToggleButton>("PART_WrapLinesButton");
            var copyDiffButton = this.FindControl<Button>("PART_CopyDiffButton");
            _lineAlignmentButton = this.FindControl<ToggleButton>("PART_LineAlignmentButton");

            // Wire up events with enhanced feedback
            if (_searchBox != null)
            {
                _searchBox.KeyUp += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        NavigateToNextSearchResult();
                    }
                    else
                    {
                        Render();
                        UpdateSearchStatus();
                    }
                };
                _searchBox.PropertyChanged += (_, e) =>
                {
                    if (e.Property.Name == nameof(TextBox.Text))
                    {
                        Render();
                        UpdateSearchStatus();
                    }
                };
            }

            if (sideBySideButton != null && unifiedButton != null)
            {
                // Set up view mode toggle buttons
                sideBySideButton.IsCheckedChanged += (_, _) =>
                {
                    if (sideBySideButton.IsChecked == true)
                    {
                        unifiedButton!.IsChecked = false;
                        ViewMode = DiffViewMode.SideBySide;
                        UpdateStatus("Switched to side-by-side view");
                    }
                };

                unifiedButton.IsCheckedChanged += (_, _) =>
                {
                    if (unifiedButton.IsChecked == true)
                    {
                        sideBySideButton!.IsChecked = false;
                        ViewMode = DiffViewMode.Unified;
                        UpdateStatus("Switched to unified view");
                    }
                };
            }

            if (nextChangeButton != null)
            {
                nextChangeButton.Click += (_, __) =>
                {
                    NavigateToNextChange();
                    UpdateStatus($"Navigated to change {_currentChangeIndex + 1} of {_changedLines.Count}");
                };
            }

            if (prevChangeButton != null)
            {
                prevChangeButton.Click += (_, __) =>
                {
                    NavigateToPreviousChange();
                    UpdateStatus($"Navigated to change {_currentChangeIndex + 1} of {_changedLines.Count}");
                };
            }

            if (nextSearchButton != null)
            {
                nextSearchButton.Click += (_, __) =>
                {
                    NavigateToNextSearchResult();
                    UpdateSearchStatus();
                };
            }

            if (prevSearchButton != null)
            {
                prevSearchButton.Click += (_, __) =>
                {
                    NavigateToPreviousSearchResult();
                    UpdateSearchStatus();
                };
            }

            if (openIdeButton != null)
            {
                openIdeButton.Click += (_, __) =>
                {
                    OpenInIDE();
                    UpdateStatus("Opened in IDE");
                };
            }

            if (codeFoldingButton != null)
            {
                codeFoldingButton.IsCheckedChanged += (_, __) =>
                {
                    _codeFoldingEnabled = codeFoldingButton.IsChecked == true;
                    Render();
                    UpdateStatus(_codeFoldingEnabled ? "Code folding enabled" : "Code folding disabled");
                };
            }

            if (copyButton != null)
            {
                copyButton.Click += (_, __) =>
                {
                    CopySelectedText();
                    UpdateStatus("Copied selected text");
                };
            }

            if (_ignoreWhitespaceButton != null)
            {
                _ignoreWhitespaceButton.IsChecked = _ignoreWhitespace;
                _ignoreWhitespaceButton.IsCheckedChanged += (_, __) =>
                {
                    _ignoreWhitespace = _ignoreWhitespaceButton.IsChecked == true;
                    DiffPreferences.IgnoreWhitespace = _ignoreWhitespace;
                    Render();
                    UpdateStatus(_ignoreWhitespace ? "Ignoring whitespace" : "Showing whitespace changes");
                };
            }

            if (_ignoreNewlinesButton != null)
            {
                _ignoreNewlinesButton.IsChecked = _ignoreNewlines;
                _ignoreNewlinesButton.IsCheckedChanged += (_, __) =>
                {
                    _ignoreNewlines = _ignoreNewlinesButton.IsChecked == true;
                    DiffPreferences.IgnoreNewlines = _ignoreNewlines;
                    Render();
                    UpdateStatus(_ignoreNewlines ? "Ignoring EOL differences" : "Showing EOL differences");
                };
            }

            if (_wrapLinesButton != null)
            {
                _wrapLinesButton.IsChecked = _wrapLines;
                _wrapLinesButton.IsCheckedChanged += (_, __) =>
                {
                    _wrapLines = _wrapLinesButton.IsChecked == true;
                    DiffPreferences.WrapLines = _wrapLines;
                    if (_oldEditor != null)
                        _oldEditor.WordWrap = _wrapLines;
                    if (_newEditor != null)
                        _newEditor.WordWrap = _wrapLines;
                    UpdateStatus(_wrapLines ? "Line wrapping enabled" : "Line wrapping disabled");
                };
            }

            if (copyDiffButton != null)
            {
                copyDiffButton.Click += (_, __) =>
                {
                    CopyDiff();
                    UpdateStatus("Copied diff to clipboard");
                };
            }

            if (_lineAlignmentButton != null)
            {
                _lineAlignmentButton.IsChecked = _lineAlignmentEnabled;
                _lineAlignmentButton.IsCheckedChanged += (_, __) =>
                {
                    _lineAlignmentEnabled = _lineAlignmentButton.IsChecked == true;
                    Render();
                    UpdateStatus(_lineAlignmentEnabled ? "Line alignment enabled" : "Line alignment disabled");
                };
            }
        }

        private void UpdateSearchStatus()
        {
            if (_searchMatches.Count > 0)
            {
                UpdateStatus($"Found {_searchMatches.Count} matches • Match {_currentSearchIndex + 1} of {_searchMatches.Count}");
            }
            else if (!string.IsNullOrWhiteSpace(_searchBox?.Text))
            {
                UpdateStatus("No matches found");
            }
            else
            {
                UpdateStatus("Ready");
            }
        }

        private void SetupEditors()
        {
            if (_oldEditor is null || _newEditor is null)
                return;

            _logger.LogDebug("Setting up DiffViewer editors");

            // Enhanced editor setup
            var highlightDef = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
            _oldEditor.SyntaxHighlighting = highlightDef;
            _newEditor.SyntaxHighlighting = highlightDef;

            _oldEditor.IsReadOnly = true;
            _newEditor.IsReadOnly = true;
            _oldEditor.ShowLineNumbers = true;
            _newEditor.ShowLineNumbers = true;

            // Enhanced editor appearance
            _oldEditor.Options.EnableHyperlinks = false;
            _oldEditor.Options.EnableEmailHyperlinks = false;
            _newEditor.Options.EnableHyperlinks = false;
            _newEditor.Options.EnableEmailHyperlinks = false;

            // Add caret position tracking
            _oldEditor.TextArea.Caret.PositionChanged += (_, __) => UpdatePosition();
            _newEditor.TextArea.Caret.PositionChanged += (_, __) => UpdatePosition();

            // Add selection tracking
            _oldEditor.TextArea.SelectionChanged += (_, __) => UpdateSelection();
            _newEditor.TextArea.SelectionChanged += (_, __) => UpdateSelection();

            // Add focus tracking
            _oldEditor.GotFocus += (_, __) => UpdatePosition();
            _newEditor.GotFocus += (_, __) => UpdatePosition();

            _oldEditor.PointerPressed += EditorPointerPressed;
            _newEditor.PointerPressed += EditorPointerPressed;

            SetupScrollSync();
            Render();
        }

        public void Render()
        {
            _logger.LogDebug("DiffViewer.Render() called");
            if (_oldEditor is null || _newEditor is null)
            {
                _logger.LogWarning("DiffViewer.Render(): editors are null, aborting render");
                return;
            }

            UpdateStatus("Rendering diff...");

            ResetFoldingManagers();

            string oldTextRaw = OldText ?? string.Empty;
            string newTextRaw = NewText ?? string.Empty;
            string oldTextValue = SanitizeForDiff(oldTextRaw, _ignoreNewlines);
            string newTextValue = SanitizeForDiff(newTextRaw, _ignoreNewlines);

            _oldEditor.Document = new AvaloniaEdit.Document.TextDocument(oldTextValue);
            _newEditor.Document = new AvaloniaEdit.Document.TextDocument(newTextValue);
            _oldEditor.Text = oldTextValue;
            _newEditor.Text = newTextValue;

            _oldEditor.TextArea.TextView.LineTransformers.Clear();
            _newEditor.TextArea.TextView.LineTransformers.Clear();
            _oldEditor.TextArea.TextView.BackgroundRenderers.Clear();
            _newEditor.TextArea.TextView.BackgroundRenderers.Clear();

            // Enhanced diff processing with better feedback (async to avoid UI freeze)
            _ = ProcessDiffAndUpdateUIAsync(oldTextValue, newTextValue);
        }

        private void ProcessDiffAndUpdateUI(string oldTextValue, string newTextValue)
        {
            // Handle special cases for new/deleted files
            if (string.IsNullOrEmpty(oldTextValue) && !string.IsNullOrEmpty(newTextValue))
            {
                HandleNewFile(newTextValue);
                return;
            }

            if (!string.IsNullOrEmpty(oldTextValue) && string.IsNullOrEmpty(newTextValue))
            {
                HandleDeletedFile(oldTextValue);
                return;
            }

            // Normal diff processing
            var model = new InlineDiffBuilder(new Differ())
                            .BuildDiffModel(oldTextValue, newTextValue, _ignoreWhitespace);

            var lineMap = model.Lines
                .Select((l, i) => (Line: i + 1, Type: Map(l.Type)))
                .ToDictionary(t => t.Line, t => t.Type);

            _lineTypes = lineMap;
            ApplyDiffVisualization(lineMap);
            UpdateStatistics(lineMap);
            ApplySearchHighlighting();
            UpdateMetrics();

            if (_codeFoldingEnabled)
                ApplyFolding();
            else
                ClearFolding();
        }

        private async System.Threading.Tasks.Task ProcessDiffAndUpdateUIAsync(string oldTextValue, string newTextValue)
        {
            try
            {
                // Handle special cases for new/deleted files quickly on UI thread
                if (string.IsNullOrEmpty(oldTextValue) && !string.IsNullOrEmpty(newTextValue))
                {
                    HandleNewFile(newTextValue);
                    UpdateStatus("Diff rendered successfully");
                    return;
                }

                if (!string.IsNullOrEmpty(oldTextValue) && string.IsNullOrEmpty(newTextValue))
                {
                    HandleDeletedFile(oldTextValue);
                    UpdateStatus("Diff rendered successfully");
                    return;
                }

                bool ignoreWhitespace = _ignoreWhitespace; // capture for background

                // Build the diff model off the UI thread
                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    static bool IsMeaningful(string s)
                    {
                        if (string.IsNullOrEmpty(s)) return false;
                        foreach (var ch in s)
                        {
                            if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch))
                                return true;
                        }
                        return false;
                    }

                    var sbs = new DiffPlex.DiffBuilder.SideBySideDiffBuilder(new Differ())
                        .BuildDiffModel(oldTextValue, newTextValue, ignoreWhitespace);

                    var oldMap = new Dictionary<int, DiffLineType>();
                    var newMap = new Dictionary<int, DiffLineType>();
                    var oldWord = new Dictionary<int, List<(int start, int length)>>();
                    var newWord = new Dictionary<int, List<(int start, int length)>>();

                    int count = System.Math.Max(sbs.OldText.Lines.Count, sbs.NewText.Lines.Count);
                    for (int i = 0; i < count; i++)
                    {
                        var oldPiece = i < sbs.OldText.Lines.Count ? sbs.OldText.Lines[i] : null;
                        var newPiece = i < sbs.NewText.Lines.Count ? sbs.NewText.Lines[i] : null;

                        if (oldPiece != null)
                        {
                            int posOld = oldPiece.Position ?? (i + 1);
                            oldMap[posOld] = Map(oldPiece.Type);
                        }
                        if (newPiece != null)
                        {
                            int posNew = newPiece.Position ?? (i + 1);
                            newMap[posNew] = Map(newPiece.Type);
                        }

                        // Word-level spans for modified lines using token-aware diff (ignores pure formatting/newline changes)
                        if (oldPiece != null && newPiece != null && (oldPiece.Type == ChangeType.Modified || newPiece.Type == ChangeType.Modified))
                        {
                            int posOld = oldPiece.Position ?? (i + 1);
                            int posNew = newPiece.Position ?? (i + 1);
                            string oldLineText = oldPiece.Text ?? string.Empty;
                            string newLineText = newPiece.Text ?? string.Empty;

                            var (oldSp, newSp) = ComputeTokenDiffSpans(oldLineText, newLineText);

                            if (oldSp.Count > 0)
                            {
                                if (!oldWord.TryGetValue(posOld, out var list))
                                {
                                    list = new List<(int start, int length)>();
                                    oldWord[posOld] = list;
                                }
                                list.AddRange(oldSp);
                            }
                            if (newSp.Count > 0)
                            {
                                if (!newWord.TryGetValue(posNew, out var list))
                                {
                                    list = new List<(int start, int length)>();
                                    newWord[posNew] = list;
                                }
                                list.AddRange(newSp);
                            }

                            // If no meaningful token spans detected, downgrade Modified to Unchanged on that side
                            if (oldPiece.Type == ChangeType.Modified && (!oldWord.ContainsKey(posOld) || oldWord[posOld].Count == 0))
                                oldMap[posOld] = DiffLineType.Unchanged;
                            if (newPiece.Type == ChangeType.Modified && (!newWord.ContainsKey(posNew) || newWord[posNew].Count == 0))
                                newMap[posNew] = DiffLineType.Unchanged;
                        }
                    }

                    // Fallback: ensure maps cover all lines (unchanged lines default)
                    if (oldMap.Count == 0)
                    {
                        var oldLines = oldTextValue.Split('\n');
                        for (int i = 0; i < oldLines.Length; i++) oldMap[i + 1] = DiffLineType.Unchanged;
                    }
                    if (newMap.Count == 0)
                    {
                        var newLines = newTextValue.Split('\n');
                        for (int i = 0; i < newLines.Length; i++) newMap[i + 1] = DiffLineType.Unchanged;
                    }

                    return (oldMap, newMap, oldWord, newWord);
                }).ConfigureAwait(false);

                // Apply results on UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    var (oldMap, newMap, oldWord, newWord) = result;
                    // Store maps for folding/navigation
                    _oldLineTypes = oldMap;
                    _newLineTypes = newMap;
                    // For backward compatibility some features use _lineTypes; use new side
                    _lineTypes = newMap;

                    // Build line alignment mappings for better diff comparison
                    if (_lineAlignmentEnabled)
                    {
                        BuildLineAlignmentMappings(oldMap, newMap);
                    }
                    else
                    {
                        // Clear mappings when alignment is disabled
                        _lineMapping.Clear();
                        _reverseLineMapping.Clear();
                    }

                    ApplyDiffVisualization(oldMap, newMap, oldWord, newWord);

                    // Update statistics combined (meaningful modified already filtered)
                    int added = newMap.Count(kv => kv.Value == DiffLineType.Added);
                    int removed = oldMap.Count(kv => kv.Value == DiffLineType.Removed);
                    int modified = newMap.Count(kv => kv.Value == DiffLineType.Modified) + oldMap.Count(kv => kv.Value == DiffLineType.Modified);
                    if (_addedLinesText != null) _addedLinesText.Text = $"{added} added";
                    if (_removedLinesText != null) _removedLinesText.Text = $"{removed} removed";
                    if (_modifiedLinesText != null) _modifiedLinesText.Text = $"{modified} modified";

                    ApplySearchHighlighting();
                    UpdateMetrics();
                    if (_codeFoldingEnabled)
                        ApplyFolding();
                    else
                        ClearFolding();
                    UpdateStatus("Diff rendered successfully");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while rendering diff asynchronously");
                await Dispatcher.UIThread.InvokeAsync(() => UpdateStatus($"Error: {ex.Message}"));
            }
        }

        private void HandleNewFile(string newText)
        {
            var newLines = newText.Split('\n');
            _lineTypes = newLines
                .Select((_, i) => (Line: i + 1, Type: DiffLineType.Added))
                .ToDictionary(t => t.Line, t => t.Type);

            _changedLines = _lineTypes.Keys.OrderBy(k => k).ToList();
            UpdateStatistics(_lineTypes);
            ApplyDiffVisualization(_lineTypes);
        }

        private void HandleDeletedFile(string oldText)
        {
            var oldLines = oldText.Split('\n');
            _lineTypes = oldLines
                .Select((_, i) => (Line: i + 1, Type: DiffLineType.Removed))
                .ToDictionary(t => t.Line, t => t.Type);

            _changedLines = _lineTypes.Keys.OrderBy(k => k).ToList();
            UpdateStatistics(_lineTypes);
            ApplyDiffVisualization(_lineTypes);
        }

        private void ApplyDiffVisualization(Dictionary<int, DiffLineType> lineMap)
        {
            // Backward-compatible: apply same map to both editors (used for new/deleted file cases)
            var transformer = new DiffLineBackgroundTransformer(lineMap);
            _oldEditor?.TextArea.TextView.LineTransformers.Add(transformer);
            _newEditor?.TextArea.TextView.LineTransformers.Add(transformer);

            var marginRenderer = new LineStatusMarginRenderer(lineMap);
            _oldEditor?.TextArea.TextView.BackgroundRenderers.Add(marginRenderer);
            _newEditor?.TextArea.TextView.BackgroundRenderers.Add(marginRenderer);

            _changedLines = lineMap
                .Where(kv => kv.Value != DiffLineType.Unchanged)
                .Select(kv => kv.Key)
                .OrderBy(line => line)
                .ToList();
        }

        // New overload that supports independent maps and word-level spans for each editor
        private void ApplyDiffVisualization(
            Dictionary<int, DiffLineType> oldMap,
            Dictionary<int, DiffLineType> newMap,
            Dictionary<int, List<(int start, int length)>> oldWordSpans,
            Dictionary<int, List<(int start, int length)>> newWordSpans)
        {
            if (_oldEditor == null || _newEditor == null)
                return;

            // Clear any existing line transformers; keep other background renderers (comments, etc.) intact
            _oldEditor.TextArea.TextView.LineTransformers.Clear();
            _newEditor.TextArea.TextView.LineTransformers.Clear();

            var oldTransformer = new DiffLineBackgroundTransformer(oldMap);
            var newTransformer = new DiffLineBackgroundTransformer(newMap);
            _oldEditor.TextArea.TextView.LineTransformers.Add(oldTransformer);
            _newEditor.TextArea.TextView.LineTransformers.Add(newTransformer);

            // Word-level transformers (deleted parts on the old editor, inserted parts on the new editor)
            var oldWordTransformer = new WordDiffColorizingTransformer(oldWordSpans, isAdditionStyle: false);
            var newWordTransformer = new WordDiffColorizingTransformer(newWordSpans, isAdditionStyle: true);
            _oldEditor.TextArea.TextView.LineTransformers.Add(oldWordTransformer);
            _newEditor.TextArea.TextView.LineTransformers.Add(newWordTransformer);

            var oldMarginRenderer = new LineStatusMarginRenderer(oldMap);
            var newMarginRenderer = new LineStatusMarginRenderer(newMap);
            _oldEditor.TextArea.TextView.BackgroundRenderers.Add(oldMarginRenderer);
            _newEditor.TextArea.TextView.BackgroundRenderers.Add(newMarginRenderer);

            // Track changed lines for navigation using the new editor's perspective primarily
            _changedLines = newMap
                .Where(kv => kv.Value != DiffLineType.Unchanged)
                .Select(kv => kv.Key)
                .Union(oldMap.Where(kv => kv.Value != DiffLineType.Unchanged).Select(kv => kv.Key))
                .OrderBy(line => line)
                .ToList();
        }

        private void UpdateStatistics(Dictionary<int, DiffLineType> lineMap)
        {
            int addedLines = lineMap.Count(kv => kv.Value == DiffLineType.Added);
            int removedLines = lineMap.Count(kv => kv.Value == DiffLineType.Removed);
            int modifiedLines = lineMap.Count(kv => kv.Value == DiffLineType.Modified);

            if (_addedLinesText != null)
                _addedLinesText.Text = $"{addedLines} added";
            if (_removedLinesText != null)
                _removedLinesText.Text = $"{removedLines} removed";
            if (_modifiedLinesText != null)
                _modifiedLinesText.Text = $"{modifiedLines} modified";
        }

        private void ApplySearchHighlighting()
        {
            _searchMatches.Clear();
            _currentSearchIndex = -1;
            string searchQuery = _searchBox?.Text ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var searchSource = _newEditor?.Document.TextLength > 0 ? _newEditor!.Text : _oldEditor?.Text ?? string.Empty;
                var results = SearchService?.Search(searchQuery, searchSource) ?? Enumerable.Empty<SearchResult>();
                _searchMatches = results.Select(r => r.LineNumber).ToList();

                var searchTransformer = new SearchHighlightTransformer(searchQuery);
                _oldEditor?.TextArea.TextView.LineTransformers.Add(searchTransformer);
                _newEditor?.TextArea.TextView.LineTransformers.Add(searchTransformer);
            }
        }

        private void UpdateMetrics()
        {
            _metricsPanel?.Children.Clear();
            // Per-file metrics have been removed as requested
            // Only the overview section should show total metrics
        }

        private async void LoadThreadsAsync(FileDiff diff)
        {
            if (!FeatureFlagManager.InlineCommentsEnabled)
                return;

            _threadsByLine.Clear();

            if (!ConnectionSettingsStorage.TryLoad(out var settingsNullable) || settingsNullable is null)
                return;
            var settings = settingsNullable;

            try
            {
                var threads = await CommentsService.GetThreadsAsync(
                    settings.Organization,
                    settings.Project,
                    settings.Repository,
                    PullRequestId,
                    settings.PersonalAccessToken);

                foreach (var t in threads.Where(t => string.Equals(t.FilePath, diff.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _threadsByLine[t.LineNumber] = t;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load comment threads");
            }

            ApplyCommentMarkers();
        }

        private void ApplyCommentMarkers()
        {
            if (_oldEditor == null || _newEditor == null)
                return;

            RemoveCommentRenderers(_oldEditor);
            RemoveCommentRenderers(_newEditor);

            if (_threadsByLine.Count == 0)
                return;

            var renderer = new CommentThreadMarginRenderer(_threadsByLine.Keys);
            _oldEditor.TextArea.TextView.BackgroundRenderers.Add(renderer);
            _newEditor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        }

        private static void RemoveCommentRenderers(TextEditor editor)
        {
            for (int i = editor.TextArea.TextView.BackgroundRenderers.Count - 1; i >= 0; i--)
            {
                if (editor.TextArea.TextView.BackgroundRenderers[i] is CommentThreadMarginRenderer)
                {
                    editor.TextArea.TextView.BackgroundRenderers.RemoveAt(i);
                }
            }
        }

        private void EditorPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!FeatureFlagManager.InlineCommentsEnabled)
                return;

            if (sender is not TextEditor editor)
                return;

            var pos = e.GetPosition(editor.TextArea.TextView);
            if (pos.X > 20) // Only react to clicks in the gutter
                return;

            var loc = editor.TextArea.TextView.GetPosition(pos);
            if (loc == null)
                return;

            ShowThreadPopup(editor, loc.Value.Line);
            e.Handled = true;
        }

        private void ShowThreadPopup(TextEditor editor, int line)
        {
            _threadsByLine.TryGetValue(line, out var thread);

            var panel = BuildThreadPanel(thread, line);

            _commentPopup?.Close();
            _commentPopup = new Popup
            {
                PlacementTarget = editor,
                Placement = PlacementMode.Pointer,
                Child = panel
            };

            _commentPopup.Open();
        }

        private Control BuildThreadPanel(CommentThread? thread, int line)
        {
            var stack = new StackPanel { Spacing = 4, MaxWidth = 400 };

            if (thread != null)
            {
                foreach (var c in thread.Comments)
                {
                    var commentStack = new StackPanel { Spacing = 2 };
                    commentStack.Children.Add(new TextBlock
                    {
                        Text = $"{c.Author} • {c.PublishedDate:g}",
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold
                    });
                    commentStack.Children.Add(new MarkdownScrollViewer
                    {
                        Markdown = c.Content
                    });
                    stack.Children.Add(commentStack);
                }

                var resolveBtn = new Button
                {
                    Content = string.Equals(thread.Status, "closed", StringComparison.OrdinalIgnoreCase) ? "Unresolve" : "Resolve"
                };
                resolveBtn.Click += async (_, __) =>
                {
                    await ToggleResolveAsync(thread);
                    _commentPopup?.Close();
                };
                stack.Children.Add(resolveBtn);
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No comments",
                    FontStyle = FontStyle.Italic,
                    FontSize = 12
                });
            }

            var box = new TextBox { AcceptsReturn = true, Width = 300, Height = 60 };
            var btn = new Button { Content = "Post" };
            btn.Click += async (_, __) =>
            {
                await PostCommentAsync(thread, line, box.Text);
                _commentPopup?.Close();
            };

            stack.Children.Add(box);
            stack.Children.Add(btn);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Child = stack
            };
        }

        private async Task PostCommentAsync(CommentThread? thread, int line, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (!ConnectionSettingsStorage.TryLoad(out var settingsNullable) || settingsNullable is null || DataContext is not FileDiff diff)
                return;
            var settings = settingsNullable;

            try
            {
                if (thread == null)
                {
                    var newThread = await CommentsService.CreateThreadAsync(
                        settings.Organization,
                        settings.Project,
                        settings.Repository,
                        PullRequestId,
                        diff.FilePath,
                        line,
                        text,
                        settings.PersonalAccessToken);
                    _threadsByLine[line] = newThread;
                }
                else
                {
                    var lastId = thread.Comments.Last().Id;
                    var updated = await CommentsService.ReplyToThreadAsync(
                        settings.Organization,
                        settings.Project,
                        settings.Repository,
                        PullRequestId,
                        thread.ThreadId,
                        lastId,
                        text,
                        settings.PersonalAccessToken);
                    _threadsByLine[line] = updated;
                }

                ApplyCommentMarkers();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to post comment");
            }
        }

        private async Task ToggleResolveAsync(CommentThread thread)
        {
            if (!ConnectionSettingsStorage.TryLoad(out var settingsNullable) || settingsNullable is null)
                return;
            var settings = settingsNullable;

            bool newState = !string.Equals(thread.Status, "closed", StringComparison.OrdinalIgnoreCase);
            try
            {
                await CommentsService.UpdateThreadStatusAsync(
                    settings.Organization,
                    settings.Project,
                    settings.Repository,
                    PullRequestId,
                    thread.ThreadId,
                    newState,
                    settings.PersonalAccessToken);

                thread.Status = newState ? "closed" : "active";
                _threadsByLine[thread.LineNumber] = thread;
                ApplyCommentMarkers();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update thread status");
            }
        }

        private static DiffLineType Map(ChangeType ct) => ct switch
        {
            ChangeType.Inserted => DiffLineType.Added,
            ChangeType.Deleted => DiffLineType.Removed,
            ChangeType.Modified => DiffLineType.Modified,
            _ => DiffLineType.Unchanged
        };

        private void NavigateToNextChange()
        {
            if (_changedLines.Count == 0 || _oldEditor == null) return;

            _currentChangeIndex = (_currentChangeIndex + 1) % _changedLines.Count;
            int lineNumber = _changedLines[_currentChangeIndex];

            ScrollToLine(_oldEditor, lineNumber);
            if (_newEditor != null && ViewMode == DiffViewMode.SideBySide)
            {
                ScrollToLine(_newEditor, lineNumber);
            }
        }

        private void NavigateToPreviousChange()
        {
            if (_changedLines.Count == 0 || _oldEditor == null) return;

            _currentChangeIndex = (_currentChangeIndex - 1 + _changedLines.Count) % _changedLines.Count;
            int lineNumber = _changedLines[_currentChangeIndex];

            ScrollToLine(_oldEditor, lineNumber);
            if (_newEditor != null && ViewMode == DiffViewMode.SideBySide)
            {
                ScrollToLine(_newEditor, lineNumber);
            }
        }

        private void NavigateToNextSearchResult()
        {
            if (_searchMatches.Count == 0 || _newEditor == null)
                return;

            _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatches.Count;
            int line = _searchMatches[_currentSearchIndex];

            ScrollToLine(_newEditor, line);
            if (ViewMode == DiffViewMode.SideBySide && _oldEditor != null)
                ScrollToLine(_oldEditor, line);
        }

        private void NavigateToPreviousSearchResult()
        {
            if (_searchMatches.Count == 0 || _newEditor == null)
                return;

            _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
            int line = _searchMatches[_currentSearchIndex];

            ScrollToLine(_newEditor, line);
            if (ViewMode == DiffViewMode.SideBySide && _oldEditor != null)
                ScrollToLine(_oldEditor, line);
        }

        private void OpenInIDE()
        {
            if (DataContext is FileDiff diff)
            {
                int line = 1;
                if (_newEditor?.IsFocused == true)
                    line = _newEditor.TextArea.Caret.Line;
                else if (_oldEditor?.IsFocused == true)
                    line = _oldEditor.TextArea.Caret.Line;

                try
                {
                    IDEService?.OpenInIDE(diff.FilePath, line);
                }
                catch (Exception ex)
                {
                    NotificationService?.Notify("ide-error", new Notification
                    {
                        Title = "IDE Error",
                        Message = ex.Message,
                        Type = NotificationType.Error
                    });
                }
            }
        }

        private void ScrollToLine(TextEditor editor, int lineNumber)
        {
            if (lineNumber <= 0 || lineNumber > editor.Document.LineCount) return;

            var line = editor.Document.GetLineByNumber(lineNumber);
            editor.ScrollTo(line.LineNumber, 0);

            // Highlight the line temporarily
            editor.TextArea.TextView.LineTransformers.Add(
                new TemporaryHighlightTransformer(line.LineNumber));

            // Remove highlight after a delay
            var timer = new System.Threading.Timer(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var transformers = editor.TextArea.TextView.LineTransformers;
                    for (int i = transformers.Count - 1; i >= 0; i--)
                    {
                        if (transformers[i] is TemporaryHighlightTransformer)
                        {
                            transformers.RemoveAt(i);
                        }
                    }
                });
            }, null, 1500, System.Threading.Timeout.Infinite);
        }

        public void JumpToLine(int lineNumber)
        {
            if (_oldEditor != null)
                ScrollToLine(_oldEditor, lineNumber);
            if (_newEditor != null && ViewMode == DiffViewMode.SideBySide)
                ScrollToLine(_newEditor, lineNumber);
        }

        private void SetupScrollSync()
        {
            _oldEditor?.ApplyTemplate();
            _newEditor?.ApplyTemplate();

            _oldScrollViewer = FindDescendant<ScrollViewer>(_oldEditor);
            _newScrollViewer = FindDescendant<ScrollViewer>(_newEditor);

            if (_oldScrollViewer != null && _newScrollViewer != null)
            {
                _oldScrollViewer.ScrollChanged += (_, __) => SyncScroll(_oldScrollViewer, _newScrollViewer);
                _newScrollViewer.ScrollChanged += (_, __) => SyncScroll(_newScrollViewer, _oldScrollViewer);
            }
        }

        private void SyncScroll(ScrollViewer source, ScrollViewer target)
        {
            if (_syncScrollInProgress)
                return;

            _syncScrollInProgress = true;
            
            if (_lineAlignmentEnabled && _lineMapping.Count > 0)
            {
                // Use intelligent line alignment for scroll synchronization
                SyncScrollWithLineAlignment(source, target);
            }
            else
            {
                // Fall back to simple position matching
                target.Offset = target.Offset.WithY(source.Offset.Y);
            }
            
            _syncScrollInProgress = false;
        }

        /// <summary>
        /// Builds line alignment mappings between old and new editors for better diff comparison.
        /// This creates mappings that align corresponding changed lines for easier review.
        /// </summary>
        private void BuildLineAlignmentMappings(Dictionary<int, DiffLineType> oldMap, Dictionary<int, DiffLineType> newMap)
        {
            _lineMapping.Clear();
            _reverseLineMapping.Clear();

            _logger.LogDebug("Building line alignment mappings. Old lines: {OldCount}, New lines: {NewCount}", 
                oldMap.Count, newMap.Count);

            // Get lists of changed lines in each editor
            var oldChangedLines = oldMap.Where(kv => kv.Value != DiffLineType.Unchanged)
                                       .Select(kv => kv.Key)
                                       .OrderBy(line => line)
                                       .ToList();

            var newChangedLines = newMap.Where(kv => kv.Value != DiffLineType.Unchanged)
                                       .Select(kv => kv.Key)
                                       .OrderBy(line => line)
                                       .ToList();

            // Strategy 1: Align modified lines that correspond to each other
            AlignModifiedLines(oldMap, newMap);

            // Strategy 2: Align consecutive change blocks
            AlignChangeBlocks(oldChangedLines, newChangedLines, oldMap, newMap);

            // Strategy 3: Fill in gaps with proportional alignment for unchanged regions
            FillUnchangedRegions(oldMap, newMap);

            _logger.LogDebug("Line alignment completed. Created {MappingCount} mappings", _lineMapping.Count);
        }

        /// <summary>
        /// Aligns modified lines that likely correspond to each other based on position and content.
        /// </summary>
        private void AlignModifiedLines(Dictionary<int, DiffLineType> oldMap, Dictionary<int, DiffLineType> newMap)
        {
            var oldModified = oldMap.Where(kv => kv.Value == DiffLineType.Modified)
                                   .Select(kv => kv.Key)
                                   .OrderBy(line => line)
                                   .ToList();

            var newModified = newMap.Where(kv => kv.Value == DiffLineType.Modified)
                                   .Select(kv => kv.Key)
                                   .OrderBy(line => line)
                                   .ToList();

            // Simple 1:1 alignment of modified lines based on order
            int minCount = Math.Min(oldModified.Count, newModified.Count);
            for (int i = 0; i < minCount; i++)
            {
                int oldLine = oldModified[i];
                int newLine = newModified[i];
                
                _lineMapping[oldLine] = newLine;
                _reverseLineMapping[newLine] = oldLine;
            }
        }

        /// <summary>
        /// Aligns consecutive blocks of changes to improve visual correspondence.
        /// </summary>
        private void AlignChangeBlocks(List<int> oldChangedLines, List<int> newChangedLines, 
                                     Dictionary<int, DiffLineType> oldMap, Dictionary<int, DiffLineType> newMap)
        {
            if (oldChangedLines.Count == 0 || newChangedLines.Count == 0) return;

            // Group consecutive lines into blocks
            var oldBlocks = GroupConsecutiveLines(oldChangedLines);
            var newBlocks = GroupConsecutiveLines(newChangedLines);

            // Align blocks based on position and size
            for (int i = 0; i < Math.Min(oldBlocks.Count, newBlocks.Count); i++)
            {
                var oldBlock = oldBlocks[i];
                var newBlock = newBlocks[i];

                // Align the start of each block
                if (!_lineMapping.ContainsKey(oldBlock.start))
                {
                    _lineMapping[oldBlock.start] = newBlock.start;
                    _reverseLineMapping[newBlock.start] = oldBlock.start;
                }

                // If blocks are similar in size, align them proportionally
                if (Math.Abs(oldBlock.length - newBlock.length) <= 2)
                {
                    AlignBlocksProportionally(oldBlock, newBlock);
                }
            }
        }

        /// <summary>
        /// Groups consecutive line numbers into blocks for better alignment.
        /// </summary>
        private List<(int start, int length)> GroupConsecutiveLines(List<int> lines)
        {
            var blocks = new List<(int start, int length)>();
            if (lines.Count == 0) return blocks;

            int blockStart = lines[0];
            int blockLength = 1;

            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i] == lines[i - 1] + 1)
                {
                    // Consecutive line, extend current block
                    blockLength++;
                }
                else
                {
                    // Gap found, end current block and start new one
                    blocks.Add((blockStart, blockLength));
                    blockStart = lines[i];
                    blockLength = 1;
                }
            }

            // Add the final block
            blocks.Add((blockStart, blockLength));
            return blocks;
        }

        /// <summary>
        /// Aligns lines within blocks proportionally when they have similar sizes.
        /// </summary>
        private void AlignBlocksProportionally((int start, int length) oldBlock, (int start, int length) newBlock)
        {
            if (oldBlock.length <= 1 || newBlock.length <= 1) return;

            for (int i = 1; i < Math.Min(oldBlock.length, newBlock.length); i++)
            {
                int oldLine = oldBlock.start + i;
                int newLine = newBlock.start + i;

                if (!_lineMapping.ContainsKey(oldLine) && !_reverseLineMapping.ContainsKey(newLine))
                {
                    _lineMapping[oldLine] = newLine;
                    _reverseLineMapping[newLine] = oldLine;
                }
            }
        }

        /// <summary>
        /// Fills gaps in unchanged regions with proportional alignment to maintain context.
        /// </summary>
        private void FillUnchangedRegions(Dictionary<int, DiffLineType> oldMap, Dictionary<int, DiffLineType> newMap)
        {
            int oldLineCount = _oldEditor?.Document.LineCount ?? 0;
            int newLineCount = _newEditor?.Document.LineCount ?? 0;

            if (oldLineCount == 0 || newLineCount == 0) return;

            // Find ranges between mapped lines and fill them proportionally
            var mappedOldLines = _lineMapping.Keys.OrderBy(x => x).ToList();
            
            int lastOldLine = 0;
            int lastNewLine = 0;

            foreach (int currentOldLine in mappedOldLines)
            {
                int currentNewLine = _lineMapping[currentOldLine];

                // Fill the gap between lastOldLine and currentOldLine
                FillProportionalGap(lastOldLine + 1, currentOldLine - 1, lastNewLine + 1, currentNewLine - 1);

                lastOldLine = currentOldLine;
                lastNewLine = currentNewLine;
            }

            // Fill the gap from the last mapped line to the end
            FillProportionalGap(lastOldLine + 1, oldLineCount, lastNewLine + 1, newLineCount);
        }

        /// <summary>
        /// Fills a gap between two mapped regions with proportional alignment.
        /// </summary>
        private void FillProportionalGap(int oldStart, int oldEnd, int newStart, int newEnd)
        {
            if (oldStart > oldEnd || newStart > newEnd) return;

            int oldRange = oldEnd - oldStart + 1;
            int newRange = newEnd - newStart + 1;

            if (oldRange <= 0 || newRange <= 0) return;

            // Create proportional mappings within the gap
            for (int i = 0; i < oldRange; i++)
            {
                int oldLine = oldStart + i;
                int newLine = newStart + (int)Math.Round((double)i * newRange / oldRange);
                
                // Ensure newLine is within bounds
                newLine = Math.Min(newLine, newEnd);
                
                if (!_lineMapping.ContainsKey(oldLine) && !_reverseLineMapping.ContainsKey(newLine))
                {
                    _lineMapping[oldLine] = newLine;
                    _reverseLineMapping[newLine] = oldLine;
                }
            }
        }

        /// <summary>
        /// Enhanced scroll synchronization that uses line alignment mappings for better diff comparison.
        /// </summary>
        private void SyncScrollWithLineAlignment(ScrollViewer source, ScrollViewer target)
        {
            if (_oldEditor == null || _newEditor == null || _oldScrollViewer == null || _newScrollViewer == null)
            {
                // Fall back to simple sync if editors aren't ready
                target.Offset = target.Offset.WithY(source.Offset.Y);
                return;
            }

            try
            {
                // Calculate which line is currently at the top of the source editor
                double lineHeight = GetLineHeight(source == _oldScrollViewer ? _oldEditor : _newEditor);
                if (lineHeight <= 0)
                {
                    target.Offset = target.Offset.WithY(source.Offset.Y);
                    return;
                }

                int sourceTopLine = (int)Math.Round(source.Offset.Y / lineHeight) + 1;
                
                // Find the corresponding line in the target editor
                int targetLine = GetCorrespondingLine(sourceTopLine, source == _oldScrollViewer);
                
                // Calculate the target scroll position
                double targetY = Math.Max(0, (targetLine - 1) * lineHeight);
                
                target.Offset = target.Offset.WithY(targetY);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in line-aligned scroll sync, falling back to simple sync");
                target.Offset = target.Offset.WithY(source.Offset.Y);
            }
        }

        /// <summary>
        /// Gets the line height for a given editor.
        /// </summary>
        private double GetLineHeight(TextEditor editor)
        {
            try
            {
                if (editor.Document.LineCount > 0)
                {
                    var firstLine = editor.Document.GetLineByNumber(1);
                    var firstLinePosition = new TextViewPosition(1, 1);
                    var linePosition = editor.TextArea.TextView.GetVisualPosition(firstLinePosition, VisualYPosition.LineTop);
                    
                    var nextLinePosition = editor.Document.LineCount > 1 
                        ? editor.TextArea.TextView.GetVisualPosition(new TextViewPosition(2, 1), VisualYPosition.LineTop)
                        : new Point(0, linePosition.Y + 16); // fallback height

                    return Math.Max(1, nextLinePosition.Y - linePosition.Y);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not calculate line height, using default");
            }

            return 16; // Default line height fallback
        }

        /// <summary>
        /// Gets the corresponding line in the other editor using the line alignment mapping.
        /// </summary>
        private int GetCorrespondingLine(int sourceLine, bool isOldEditor)
        {
            Dictionary<int, int> mapping = isOldEditor ? _lineMapping : _reverseLineMapping;
            
            // Try direct mapping first
            if (mapping.TryGetValue(sourceLine, out int directMatch))
            {
                return directMatch;
            }

            // Find the closest mapped line
            var mappedLines = mapping.Keys.OrderBy(x => x).ToList();
            if (mappedLines.Count == 0)
            {
                // No mappings available, return the same line number
                return sourceLine;
            }

            // Find the nearest mapped lines before and after the source line
            int beforeLine = mappedLines.LastOrDefault(x => x < sourceLine);
            int afterLine = mappedLines.FirstOrDefault(x => x > sourceLine);

            if (beforeLine == 0 && afterLine == 0)
            {
                // All mapped lines are after the source line
                afterLine = mappedLines.First();
                int offset = afterLine - sourceLine;
                return Math.Max(1, mapping[afterLine] - offset);
            }
            else if (afterLine == 0)
            {
                // All mapped lines are before the source line
                int offset = sourceLine - beforeLine;
                return mapping[beforeLine] + offset;
            }
            else if (beforeLine == 0)
            {
                // All mapped lines are after the source line
                int offset = afterLine - sourceLine;
                return Math.Max(1, mapping[afterLine] - offset);
            }
            else
            {
                // Interpolate between the before and after lines
                double ratio = (double)(sourceLine - beforeLine) / (afterLine - beforeLine);
                int targetBefore = mapping[beforeLine];
                int targetAfter = mapping[afterLine];
                return (int)Math.Round(targetBefore + ratio * (targetAfter - targetBefore));
            }
        }

        private static T? FindDescendant<T>(Visual? root) where T : class
        {
            return root?.FindDescendantOfType<T>();
        }

        private void ResetFoldingManagers()
        {
            if (_oldFoldingManager != null)
            {
                FoldingManager.Uninstall(_oldFoldingManager);
                _oldFoldingManager = null;
            }
            if (_newFoldingManager != null)
            {
                FoldingManager.Uninstall(_newFoldingManager);
                _newFoldingManager = null;
            }
        }

        private void ApplyFolding()
        {
            _logger.LogDebug("ApplyFolding() called. _codeFoldingEnabled={CodeFoldingEnabled}", _codeFoldingEnabled);

            if (_oldEditor is null || _newEditor is null)
            {
                _logger.LogDebug("ApplyFolding(): editors are null, aborting");
                return;
            }

            _oldFoldingManager ??= FoldingManager.Install(_oldEditor.TextArea);
            _newFoldingManager ??= FoldingManager.Install(_newEditor.TextArea);

            // Ensure both line type maps are populated - if one is empty, use a unified approach
            if (_oldLineTypes.Count == 0 && _newLineTypes.Count > 0)
            {
                // If old line types are empty, create a default unchanged map for the old editor
                var oldLines = _oldEditor.Text.Split('\n');
                _oldLineTypes = oldLines.Select((_, i) => new { Line = i + 1, Type = DiffLineType.Unchanged })
                                      .ToDictionary(x => x.Line, x => x.Type);
            }
            
            if (_newLineTypes.Count == 0 && _oldLineTypes.Count > 0)
            {
                // If new line types are empty, create a default unchanged map for the new editor
                var newLines = _newEditor.Text.Split('\n');
                _newLineTypes = newLines.Select((_, i) => new { Line = i + 1, Type = DiffLineType.Unchanged })
                                      .ToDictionary(x => x.Line, x => x.Type);
            }

            var foldsOld = GetFoldRegionsAroundChangesForMap(_oldLineTypes, _oldEditor.Document.LineCount);
            var foldsNew = GetFoldRegionsAroundChangesForMap(_newLineTypes, _newEditor.Document.LineCount);
            var newFoldingsOld = new List<NewFolding>();
            var newFoldingsNew = new List<NewFolding>();

            foreach (var (start, end) in foldsOld)
            {
                if (start > _oldEditor.Document.LineCount) continue;
                int endOldLine = Math.Min(end, _oldEditor.Document.LineCount);
                if (start <= endOldLine)
                {
                    var startOld = _oldEditor.Document.GetLineByNumber(start).Offset;
                    var endOld = _oldEditor.Document.GetLineByNumber(endOldLine).EndOffset;
                    newFoldingsOld.Add(new NewFolding(startOld, endOld)
                    {
                        Name = "...",
                        DefaultClosed = true
                    });
                }
            }

            foreach (var (start, end) in foldsNew)
            {
                if (start > _newEditor.Document.LineCount) continue;
                int endNewLine = Math.Min(end, _newEditor.Document.LineCount);
                if (start <= endNewLine)
                {
                    var startNew = _newEditor.Document.GetLineByNumber(start).Offset;
                    var endNew = _newEditor.Document.GetLineByNumber(endNewLine).EndOffset;
                    newFoldingsNew.Add(new NewFolding(startNew, endNew)
                    {
                        Name = "...",
                        DefaultClosed = true
                    });
                }
            }

            _logger.LogDebug("ApplyFolding(): Created {OldFoldCount} folds for old editor, {NewFoldCount} folds for new editor",
                newFoldingsOld.Count, newFoldingsNew.Count);

            _oldFoldingManager.UpdateFoldings(newFoldingsOld, -1);
            _newFoldingManager.UpdateFoldings(newFoldingsNew, -1);

            _logger.LogDebug("ApplyFolding(): Successfully applied foldings to both editors");
        }

        private void ClearFolding()
        {
            _oldFoldingManager?.Clear();
            _newFoldingManager?.Clear();
        }

        private IEnumerable<(int Start, int End)> GetFoldRegionsAroundChangesForMap(Dictionary<int, DiffLineType> map, int lineCount, int context = 2)
        {
            if (_oldEditor is null || _newEditor is null)
                yield break;

            _logger.LogDebug("GetFoldRegionsAroundChangesForMap: lineCount={LineCount}, map.Count={MapCount}",
                lineCount, map.Count);

            if (lineCount == 0)
            {
                _logger.LogDebug("GetFoldRegionsAroundChangesForMap: No lines to process");
                yield break;
            }

            // Get all changed line numbers for the provided map
            var changedLines = new HashSet<int>();
            foreach (var kv in map)
            {
                if (kv.Value != DiffLineType.Unchanged)
                    changedLines.Add(kv.Key);
            }

            if (changedLines.Count == 0)
            {
                _logger.LogDebug("GetFoldRegionsAroundChangesForMap: No changes found, not folding anything");
                yield break;
            }

            // Calculate which lines should be visible (changed lines + context)
            var visibleLines = new HashSet<int>();
            foreach (int changedLine in changedLines)
            {
                for (int i = Math.Max(1, changedLine - context);
                     i <= Math.Min(lineCount, changedLine + context);
                     i++)
                {
                    visibleLines.Add(i);
                }
            }

            // Find consecutive ranges of hidden lines to fold
            int? foldStart = null;
            var foldRegions = new List<(int, int)>();

            for (int i = 1; i <= lineCount; i++)
            {
                bool shouldBeVisible = visibleLines.Contains(i);
                if (!shouldBeVisible)
                {
                    if (foldStart == null) foldStart = i;
                }
                else
                {
                    if (foldStart.HasValue)
                    {
                        if (i - 1 >= foldStart.Value)
                            foldRegions.Add((foldStart.Value, i - 1));
                        foldStart = null;
                    }
                }
            }

            if (foldStart.HasValue)
            {
                foldRegions.Add((foldStart.Value, lineCount));
            }

            _logger.LogDebug("GetFoldRegionsAroundChangesForMap: Generated {FoldCount} fold regions",
                foldRegions.Count);

            foreach (var region in foldRegions)
            {
                yield return region;
            }
        }

        private void CopySelectedText()
        {
            string? selectedText = null;

            // Get selected text from the active editor
            if (_oldEditor?.TextArea.Selection.Length > 0)
            {
                selectedText = _oldEditor.TextArea.Selection.GetText();
            }
            else if (_newEditor?.TextArea.Selection.Length > 0)
            {
                selectedText = _newEditor.TextArea.Selection.GetText();
            }

            if (!string.IsNullOrEmpty(selectedText))
            {
                try
                {
                    // await Application.Current.Clipboard.SetTextAsync(selectedText);

                    NotificationService?.Notify("clipboard", new Notification
                    {
                        Title = "Copied to Clipboard",
                        Message = $"{selectedText.Length} characters copied",
                        Type = NotificationType.Success
                    });
                }
                catch (Exception ex)
                {
                    NotificationService?.Notify("error", new Notification
                    {
                        Title = "Clipboard Error",
                        Message = ex.Message,
                        Type = NotificationType.Error
                    });
                }
            }
        }

        private void CopyDiff()
        {
            if (DataContext is not FileDiff diff || string.IsNullOrEmpty(diff.Diff))
                return;

            try
            {
                // Application.Current!.Clipboard?.SetTextAsync(diff.Diff);
                NotificationService?.Notify("clipboard", new Notification
                {
                    Title = "Copied Diff",
                    Message = $"{diff.Diff.Length} characters copied",
                    Type = NotificationType.Success
                });
            }
            catch (Exception ex)
            {
                NotificationService?.Notify("error", new Notification
                {
                    Title = "Clipboard Error",
                    Message = ex.Message,
                    Type = NotificationType.Error
                });
            }
        }

        // Removes BOM and hidden/zero-width characters, normalizes newlines and Unicode
        private static string SanitizeForDiff(string s, bool normalizeNewlines)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Normalize line endings to LF only when requested
            if (normalizeNewlines)
                s = s.Replace("\r\n", "\n").Replace("\r", "\n");

            // Strip BOM at start
            if (s.Length > 0 && s[0] == '\uFEFF')
                s = s.Substring(1);

            // Remove zero-width and directional marks common in source text
            // U+FEFF BOM, U+200B ZWSP, U+200C ZWNJ, U+200D ZWJ, U+2060 WORD JOINER,
            // U+200E LRM, U+200F RLM, U+202A..U+202E Bidi marks, U+2066..U+2069 isolates
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\uFEFF': // BOM
                    case '\u200B': // ZWSP
                    case '\u200C': // ZWNJ
                    case '\u200D': // ZWJ
                    case '\u2060': // WORD JOINER
                    case '\u200E': // LRM
                    case '\u200F': // RLM
                    case '\u202A': // LRE
                    case '\u202B': // RLE
                    case '\u202C': // PDF
                    case '\u202D': // LRO
                    case '\u202E': // RLO
                    case '\u2066': // LRI
                    case '\u2067': // RLI
                    case '\u2068': // FSI
                    case '\u2069': // PDI
                        continue; // skip hidden/formatting char
                }
                sb.Append(ch);
            }

            s = sb.ToString();

            // Normalize Unicode to NFC for stable comparisons
            s = s.Normalize(NormalizationForm.FormC);
            return s;
        }

        // Token structure for intra-line diffs
        private readonly struct Token
        {
            public readonly int Start;
            public readonly int Length;
            public readonly string Text;
            public Token(int start, int length, string text)
            {
                Start = start; Length = length; Text = text;
            }
        }

        // Tokenize a single line into meaningful code tokens (identifiers, numbers, dotted names, string literals)
        private static List<Token> Tokenize(string line)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(line)) return tokens;
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                // Identifier/number/dotted
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    int start = i;
                    i++;
                    while (i < line.Length)
                    {
                        char ch = line[i];
                        if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.') i++;
                        else break;
                    }
                    int len = i - start;
                    if (len > 0)
                        tokens.Add(new Token(start, len, line.Substring(start, len)));
                    continue;
                }
                // String literal
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    int start = i;
                    i++; // skip opening quote
                    while (i < line.Length)
                    {
                        char ch = line[i];
                        if (ch == '\\') // escape
                        {
                            if (i + 1 < line.Length) i += 2; else { i++; break; }
                        }
                        else if (ch == quote)
                        {
                            i++; // include closing quote
                            break;
                        }
                        else i++;
                    }
                    int len = i - start;
                    if (len > 0)
                        tokens.Add(new Token(start, len, line.Substring(start, len)));
                    continue;
                }
                // Otherwise delimiter/whitespace, skip
                i++;
            }
            return tokens;
        }

        // Compute token-level diff spans for old/new lines using LCS. Returns character spans to highlight.
        private static (List<(int start, int length)> oldSpans, List<(int start, int length)> newSpans) ComputeTokenDiffSpans(string oldLine, string newLine)
        {
            var oldTokens = Tokenize(oldLine);
            var newTokens = Tokenize(newLine);

            // Quick exit: identical token sequences => no spans
            if (oldTokens.Count == newTokens.Count)
            {
                bool equal = true;
                for (int k = 0; k < oldTokens.Count; k++)
                {
                    if (!string.Equals(oldTokens[k].Text, newTokens[k].Text, StringComparison.Ordinal))
                    { equal = false; break; }
                }
                if (equal) return (new List<(int, int)>(), new List<(int, int)>());
            }

            int n = oldTokens.Count;
            int m = newTokens.Count;
            // LCS DP
            int[,] dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    if (string.Equals(oldTokens[i].Text, newTokens[j].Text, StringComparison.Ordinal))
                        dp[i, j] = dp[i + 1, j + 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }
            // Backtrack to find matched indices
            var matchedOld = new bool[n];
            var matchedNew = new bool[m];
            {
                int i = 0, j = 0;
                while (i < n && j < m)
                {
                    if (string.Equals(oldTokens[i].Text, newTokens[j].Text, StringComparison.Ordinal))
                    {
                        matchedOld[i] = true; matchedNew[j] = true; i++; j++;
                    }
                    else if (dp[i + 1, j] >= dp[i, j + 1]) i++;
                    else j++;
                }
            }
            var oldSpans = new List<(int start, int length)>();
            var newSpans = new List<(int start, int length)>();
            // Any old tokens not matched are deletions => highlight on old side
            for (int i = 0; i < n; i++)
                if (!matchedOld[i]) oldSpans.Add((oldTokens[i].Start, oldTokens[i].Length));
            // Any new tokens not matched are insertions => highlight on new side
            for (int j = 0; j < m; j++)
                if (!matchedNew[j]) newSpans.Add((newTokens[j].Start, newTokens[j].Length));

            return (oldSpans, newSpans);
        }
    }

    // Highlights full-line backgrounds according to diff type
    public class DiffLineBackgroundTransformer : DocumentColorizingTransformer
    {
        private readonly Dictionary<int, DiffLineType> _lineTypes;
        public DiffLineBackgroundTransformer(Dictionary<int, DiffLineType> lineTypes)
            => _lineTypes = lineTypes;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (_lineTypes.TryGetValue(line.LineNumber, out var type))
            {
                ISolidColorBrush? backgroundBrush = null;
                ISolidColorBrush? foregroundBrush = null;

                switch (type)
                {
                    case DiffLineType.Added:
                        backgroundBrush = new SolidColorBrush(Color.FromRgb(220, 255, 228)); // SuccessLightBrush equivalent
                        foregroundBrush = new SolidColorBrush(Color.FromRgb(26, 127, 55)); // SuccessBrush equivalent
                        break;
                    case DiffLineType.Removed:
                        backgroundBrush = new SolidColorBrush(Color.FromRgb(255, 235, 233)); // DangerLightBrush equivalent
                        foregroundBrush = new SolidColorBrush(Color.FromRgb(207, 34, 46)); // DangerBrush equivalent
                        break;
                    case DiffLineType.Modified:
                        // Do not apply full-line background for modified lines; rely on margin + word-level spans
                        backgroundBrush = null;
                        foregroundBrush = new SolidColorBrush(Color.FromRgb(36, 41, 47)); // keep default text color
                        break;
                    default:
                        // Use default text color for unchanged lines
                        foregroundBrush = new SolidColorBrush(Color.FromRgb(36, 41, 47)); // TextPrimaryBrush equivalent
                        break;
                }

                if (backgroundBrush != null)
                {
                    ChangeLinePart(line.Offset, line.EndOffset, e =>
                        e.TextRunProperties.SetBackgroundBrush(backgroundBrush));
                }

                if (foregroundBrush != null)
                {
                    ChangeLinePart(line.Offset, line.EndOffset, e =>
                        e.TextRunProperties.SetForegroundBrush(foregroundBrush));
                }
            }
            else
            {
                // Ensure unchanged lines have proper text color
                var textBrush = new SolidColorBrush(Color.FromRgb(36, 41, 47)); // TextPrimaryBrush equivalent
                ChangeLinePart(line.Offset, line.EndOffset, e =>
                    e.TextRunProperties.SetForegroundBrush(textBrush));
            }
        }
    }

    public enum DiffLineType { Unchanged, Added, Removed, Modified }
}
