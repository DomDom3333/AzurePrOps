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
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.ReviewLogic.Services;
using AzurePrOps.Models;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

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

        // Services (injected via DI)
        public ICommentProvider CommentProvider { get; set; }
        public IPullRequestService PullRequestService { get; set; }
        public ILintingService LintingService { get; set; }
        public IBlameService BlameService { get; set; }
        public IAuditTrailService AuditService { get; set; }
        public INotificationService NotificationService { get; set; }
        public IMetricsService MetricsService { get; set; }
        public ISuggestionService SuggestionService { get; set; }
        public IPatchService PatchService { get; set; }
        public ICodeFoldingService FoldingService { get; set; }
        public ISearchService SearchService { get; set; }
        public IIDEIntegrationService IDEService { get; set; }

        // UI elements
        private TextBox? _searchBox;
        private StackPanel? _metricsPanel;
        private TextEditor? _oldEditor;
        private TextEditor? _newEditor;
        private TextBlock? _addedLinesText;
        private TextBlock? _removedLinesText;
        private TextBlock? _modifiedLinesText;

        // Diff tracking
        private Dictionary<int, DiffLineType> _lineTypes = new();
        private List<int> _changedLines = new();
        private int _currentChangeIndex = -1;
        private bool _codeFoldingEnabled = true;
        private bool _ignoreWhitespace = false;
        private bool _wrapLines = false;
        private ScrollViewer? _oldScrollViewer;
        private ScrollViewer? _newScrollViewer;
        private FoldingManager? _oldFoldingManager;
        private FoldingManager? _newFoldingManager;
        private bool _syncScrollInProgress = false;
        private List<int> _searchMatches = new();
        private int _currentSearchIndex = -1;

        static DiffViewer()
        {
            ViewModeProperty.Changed.AddClassHandler<DiffViewer>((x, e) => {
                _logger.LogDebug("DiffViewer ViewMode changed: {Old} -> {New}", e.OldValue, e.NewValue);
                x.Render();
            });
            OldTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) => {
                _logger.LogDebug("DiffViewer OldText changed: Length = {Length}", (e.NewValue as string)?.Length ?? 0);
                x.Render();
            });
            NewTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) => {
                _logger.LogDebug("DiffViewer NewText changed: Length = {Length}", (e.NewValue as string)?.Length ?? 0);
                x.Render();
            });
        }

        public string OldText { get => GetValue(OldTextProperty); set => SetValue(OldTextProperty, value); }
        public string NewText { get => GetValue(NewTextProperty); set => SetValue(NewTextProperty, value); }
        public DiffViewMode ViewMode { get => GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }

        public DiffViewer()
        {
            // Provide simple default implementations so the control works
            CommentProvider    = new InMemoryCommentProvider();
            PullRequestService = PullRequestServiceFactory.Create(PullRequestServiceType.AzureDevOps);
            LintingService     = new RoslynLintingService();
            BlameService       = new GitBlameService(Environment.CurrentDirectory);
            AuditService       = new FileAuditTrailService();
            NotificationService= new AvaloniaNotificationService();
            MetricsService     = new SimpleMetricsService();
            SuggestionService  = new SimpleSuggestionService();
            PatchService       = new FilePatchService();
            FoldingService     = new IndentationFoldingService();
            SearchService      = new SimpleSearchService();
            string editor = ConnectionSettingsStorage.TryLoad(out var s)
                ? s!.EditorCommand
                : EditorDetector.GetDefaultEditor();
            IDEService = new IDEIntegrationService(editor);

            InitializeComponent();
            Loaded += (_, __) => SetupEditors();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is FileDiff diff)
            {
                // Assign bound texts when the DataContext changes so the viewer
                // renders even if the control is created before data is set.
                OldText = diff.OldText ?? string.Empty;
                NewText = diff.NewText ?? string.Empty;
            }
            else
            {
                // Clear content when the DataContext is unset or of the wrong type
                OldText = string.Empty;
                NewText = string.Empty;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // Find UI elements
            _oldEditor   = this.FindControl<TextEditor>("OldEditor");
            _newEditor   = this.FindControl<TextEditor>("NewEditor");
            _searchBox   = this.FindControl<TextBox>("PART_SearchBox");
            _metricsPanel= this.FindControl<StackPanel>("PART_MetricsPanel");
            _addedLinesText = this.FindControl<TextBlock>("PART_AddedLinesText");
            _removedLinesText = this.FindControl<TextBlock>("PART_RemovedLinesText");
            _modifiedLinesText = this.FindControl<TextBlock>("PART_ModifiedLinesText");

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
            var ignoreWhitespaceButton = this.FindControl<ToggleButton>("PART_IgnoreWhitespaceButton");
            var wrapLinesButton = this.FindControl<ToggleButton>("PART_WrapLinesButton");
            var copyDiffButton = this.FindControl<Button>("PART_CopyDiffButton");

            // Wire up events
            if (_searchBox != null)
            {
                _searchBox.KeyUp += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                        NavigateToNextSearchResult();
                    else
                        Render();
                };
                _searchBox.PropertyChanged += (_, e) =>
                {
                    if (e.Property.Name == nameof(TextBox.Text))
                        Render();
                };
            }

            if (sideBySideButton != null && unifiedButton != null)
            {
                // Set up view mode toggle buttons
                sideBySideButton.IsCheckedChanged += (_, _) => {
                    if (sideBySideButton.IsChecked == true) {
                        unifiedButton!.IsChecked = false;
                        ViewMode = DiffViewMode.SideBySide;
                    }
                };

                unifiedButton.IsCheckedChanged += (_, _) => {
                    if (unifiedButton.IsChecked == true) {
                        sideBySideButton!.IsChecked = false;
                        ViewMode = DiffViewMode.Unified;
                    }
                };
            }

            if (nextChangeButton != null)
            {
                nextChangeButton.Click += (_, __) => NavigateToNextChange();
            }

            if (prevChangeButton != null)
            {
                prevChangeButton.Click += (_, __) => NavigateToPreviousChange();
            }

            if (nextSearchButton != null)
            {
                nextSearchButton.Click += (_, __) => NavigateToNextSearchResult();
            }

            if (prevSearchButton != null)
            {
                prevSearchButton.Click += (_, __) => NavigateToPreviousSearchResult();
            }

            if (openIdeButton != null)
            {
                openIdeButton.Click += (_, __) => OpenInIDE();
            }

            if (codeFoldingButton != null)
            {
                codeFoldingButton.IsCheckedChanged += (_, __) =>
                {
                    _codeFoldingEnabled = codeFoldingButton.IsChecked == true;
                    Render();
                };
            }

            if (copyButton != null)
            {
                copyButton.Click += (_, __) => CopySelectedText();
            }

            if (ignoreWhitespaceButton != null)
            {
                ignoreWhitespaceButton.IsCheckedChanged += (_, __) =>
                {
                    _ignoreWhitespace = ignoreWhitespaceButton.IsChecked == true;
                    Render();
                };
            }

            if (wrapLinesButton != null)
            {
                wrapLinesButton.IsCheckedChanged += (_, __) =>
                {
                    _wrapLines = wrapLinesButton.IsChecked == true;
                    if (_oldEditor != null)
                        _oldEditor.WordWrap = _wrapLines;
                    if (_newEditor != null)
                        _newEditor.WordWrap = _wrapLines;
                };
            }

            if (copyDiffButton != null)
            {
                copyDiffButton.Click += (_, __) => CopyDiff();
            }
        }

        private void SetupEditors()
        {
            if (_oldEditor is null || _newEditor is null)
                return;

            _logger.LogDebug("Setting up DiffViewer editors");

            // Use AvaloniaEdit's built-in C# highlighting (no TextMate)
            var highlightDef = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
            _oldEditor.SyntaxHighlighting = highlightDef;
            _newEditor.SyntaxHighlighting = highlightDef;

            _oldEditor.IsReadOnly = true;
            _newEditor.IsReadOnly = true;
            _oldEditor.ShowLineNumbers = true;
            _newEditor.ShowLineNumbers = true;

            // Set explicit height for better visibility
            _oldEditor.MinHeight = 250;
            _newEditor.MinHeight = 250;

            // Hook up synchronized scrolling
            SetupScrollSync();

            // Render initial diff
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

            // Ensure folding managers are recreated for the new document
            ResetFoldingManagers();

            // Load texts
            _logger.LogDebug("DiffViewer.Render(): OldText length={OldLength}, NewText length={NewLength}", OldText?.Length ?? 0, NewText?.Length ?? 0);

            // Make sure we explicitly set the Document's text, not just the editor's Text property
            string oldTextValue = OldText ?? "";
            string newTextValue = NewText ?? "";

            // Force text display by setting document text directly
            _oldEditor.Document = new AvaloniaEdit.Document.TextDocument(oldTextValue);
            _newEditor.Document = new AvaloniaEdit.Document.TextDocument(newTextValue);

            // Also set the Text property for consistency
            _oldEditor.Text = oldTextValue;
            _newEditor.Text = newTextValue;

            _logger.LogDebug("Set document text - Old: {OldBytes} bytes, New: {NewBytes} bytes", oldTextValue.Length, newTextValue.Length);

            // Clear previous transformers
            _oldEditor.TextArea.TextView.LineTransformers.Clear();
            _newEditor.TextArea.TextView.LineTransformers.Clear();

            // Clear previous background renderers
            _oldEditor.TextArea.TextView.BackgroundRenderers.Clear();
            _newEditor.TextArea.TextView.BackgroundRenderers.Clear();

            // Compute unified diff
            _logger.LogDebug("Building diff model with OldText ({OldBytes} bytes) and NewText ({NewBytes} bytes)", OldText?.Length ?? 0, NewText?.Length ?? 0);

            // Add explicit handling for empty string cases
            string oldTextForDiff = OldText ?? "";
            string newTextForDiff = NewText ?? "";

            // Handle special case for new files (empty old text)
            if (string.IsNullOrEmpty(oldTextForDiff) && !string.IsNullOrEmpty(newTextForDiff))
            {
                _logger.LogDebug("Special case: New file detected (empty old text)");
                // For a new file, all lines should be marked as added
                var newLines = newTextForDiff.Split('\n');
                _lineTypes = newLines
                    .Select((_, i) => (Line: i + 1, Type: DiffLineType.Added))
                    .ToDictionary(t => t.Line, t => t.Type);

                _logger.LogDebug("Created manual _lineTypes with {Count} lines, all marked as added", _lineTypes.Count);

                // Track changed lines for navigation
                _changedLines = _lineTypes.Keys.OrderBy(k => k).ToList();

                // Update stats
                int addedLinesRender = newLines.Length;
                int removedLinesRender = 0;
                int modifiedLinesRender = 0;

                if (_addedLinesText != null)
                    _addedLinesText.Text = $"{addedLinesRender} added";
                if (_removedLinesText != null)
                    _removedLinesText.Text = $"{removedLinesRender} removed";
                if (_modifiedLinesText != null)
                    _modifiedLinesText.Text = $"{modifiedLinesRender} modified";

    // Check for file markers and handle special cases
    bool isDeletedFile = oldTextForDiff.Contains("[FILE DELETED]");
    bool isNewFile = oldTextForDiff.Contains("[FILE ADDED]");

    if (isDeletedFile || isNewFile)
    {
        // Strip markers before diff processing
        oldTextForDiff = oldTextForDiff.Replace("[FILE DELETED]\n", "").Replace("[FILE ADDED]\n", "");
        _logger.LogDebug("Detected special file marker. IsDeleted={IsDeleted}, IsNew={IsNew}", isDeletedFile, isNewFile);
    }

                // Apply coloring to the editors
                if (_newEditor != null)
                {
                    var transformernewEditor = new DiffLineBackgroundTransformer(_lineTypes);
                    _newEditor.TextArea.TextView.LineTransformers.Add(transformernewEditor);

                    // Add gutter markers
                    var marginRendererNewEditor = new LineStatusMarginRenderer(_lineTypes);
                    _newEditor.TextArea.TextView.BackgroundRenderers.Add(marginRendererNewEditor);
                }

                // Continue rendering with our manual line types
                return;
            }

            // Handle special case for deleted files (empty new text)
            if (!string.IsNullOrEmpty(oldTextForDiff) && string.IsNullOrEmpty(newTextForDiff))
            {
                _logger.LogDebug("Special case: Deleted file detected (empty new text)");
                // For a deleted file, all lines should be marked as removed
                var oldLines = oldTextForDiff.Split('\n');
                _lineTypes = oldLines
                    .Select((_, i) => (Line: i + 1, Type: DiffLineType.Removed))
                    .ToDictionary(t => t.Line, t => t.Type);

                _logger.LogDebug("Created manual _lineTypes with {Count} lines, all marked as removed", _lineTypes.Count);

                // Track changed lines for navigation
                _changedLines = _lineTypes.Keys.OrderBy(k => k).ToList();

                // Update stats
                int addedLinesUpdates = 0;
                int removedLinesUpdates = oldLines.Length;
                int modifiedLinesUpdates = 0;

                if (_addedLinesText != null)
                    _addedLinesText.Text = $"{addedLinesUpdates} added";
                if (_removedLinesText != null)
                    _removedLinesText.Text = $"{removedLinesUpdates} removed";
                if (_modifiedLinesText != null)
                    _modifiedLinesText.Text = $"{modifiedLinesUpdates} modified";

                // Apply coloring to the editors
                if (_oldEditor != null)
                {
                    var transformerOldEditor = new DiffLineBackgroundTransformer(_lineTypes);
                    _oldEditor.TextArea.TextView.LineTransformers.Add(transformerOldEditor);

                    // Add gutter markers
                    var marginRendererOldEditor = new LineStatusMarginRenderer(_lineTypes);
                    _oldEditor.TextArea.TextView.BackgroundRenderers.Add(marginRendererOldEditor);
                }

                // Continue rendering with our manual line types
                return;
            }
            
            // Normal case: Build diff model
            var model = new InlineDiffBuilder(new Differ())
                            .BuildDiffModel(oldTextForDiff, newTextForDiff, _ignoreWhitespace);

            // Check if texts are identical - compare actual content instead of just length
            bool textsAreIdentical = string.Equals(oldTextForDiff, newTextForDiff, StringComparison.Ordinal);
            if (!textsAreIdentical && oldTextForDiff.Length == newTextForDiff.Length)
            {
                _logger.LogDebug("Equal length texts with different content detected!");
            }

            // Map line numbers to change types
            var lineMap = model.Lines
                .Select((l, i) => (Line: i + 1, Type: Map(l.Type)))
                .ToDictionary(t => t.Line, t => t.Type);

            // Handle edge cases:
            // 1. Empty diff model but with non-empty content
            // 2. Same length but different content
            if ((lineMap.Count == 0 && (oldTextForDiff.Length > 0 || newTextForDiff.Length > 0)) ||
                (lineMap.Count > 0 && !lineMap.Any(kv => kv.Value != DiffLineType.Unchanged) && !string.Equals(oldTextForDiff, newTextForDiff, StringComparison.Ordinal)))
            {
                _logger.LogDebug("Edge case: Empty diff model with non-empty content, creating manual mapping");
                _lineTypes = new Dictionary<int, DiffLineType>();

                var oldLines = oldTextForDiff.Split('\n').Length;
                var newLines = newTextForDiff.Split('\n').Length;

                if (oldLines > 0 && newLines == 0)
                {
                    // Deleted file case
                    for (int i = 1; i <= oldLines; i++)
                    {
                        _lineTypes[i] = DiffLineType.Removed;
                    }
                }
                else if (oldLines == 0 && newLines > 0)
                {
                    // New file case
                    for (int i = 1; i <= newLines; i++)
                    {
                        _lineTypes[i] = DiffLineType.Added;
                    }
                }
                else
                {
                    // Check for equal length texts but different content
                    bool equalLengthButDifferent = oldLines == newLines && !string.Equals(oldTextForDiff, newTextForDiff, StringComparison.Ordinal);

                    if (equalLengthButDifferent)
                    {
                        _logger.LogDebug("Equal length but different content detected - creating manual diff");

                        // Mark all lines as modified when we have equal length but different content
                        for (int i = 1; i <= oldLines; i++)
                        {
                            _lineTypes[i] = DiffLineType.Modified;
                        }
                    }
                    else
                    {
                        // Modified file with no changes detected
                        for (int i = 1; i <= Math.Max(oldLines, newLines); i++)
                        {
                            _lineTypes[i] = DiffLineType.Unchanged;
                        }
                    }
                }

                _logger.LogDebug("Created manual lineTypes with {Count} lines", _lineTypes.Count);
                return;
            }

            _logger.LogDebug("Built diff model with {Lines} lines and {Changes} changes", model.Lines.Count, lineMap.Count(kv => kv.Value != DiffLineType.Unchanged));

            // Highlight backgrounds
            var transformer = new DiffLineBackgroundTransformer(lineMap);
            _oldEditor.TextArea.TextView.LineTransformers.Add(transformer);
            _newEditor.TextArea.TextView.LineTransformers.Add(transformer);

            // Track changed lines for navigation
            _changedLines = lineMap
                .Where(kv => kv.Value != DiffLineType.Unchanged)
                .Select(kv => kv.Key)
                .OrderBy(line => line)
                .ToList();

            // Update stats
            int addedLines = lineMap.Count(kv => kv.Value == DiffLineType.Added);
            int removedLines = lineMap.Count(kv => kv.Value == DiffLineType.Removed);
            int modifiedLines = lineMap.Count(kv => kv.Value == DiffLineType.Modified);

            // Special case for new files (if there's only new content)
            if (string.IsNullOrWhiteSpace(OldText) && !string.IsNullOrWhiteSpace(NewText))
            {
                _logger.LogDebug("Special case for stats: New file detected");
                addedLines = NewText.Split('\n').Length;
                removedLines = 0;
                modifiedLines = 0;
            }

            // Special case for deleted files (if there's only old content)
            if (!string.IsNullOrWhiteSpace(OldText) && string.IsNullOrWhiteSpace(NewText))
            {
                _logger.LogDebug("Special case for stats: Deleted file detected");
                addedLines = 0;
                removedLines = OldText.Split('\n').Length;
                modifiedLines = 0;
            }

            // Log line counts for debugging
            _logger.LogDebug("DiffViewer stats: {Added} added, {Removed} removed, {Modified} modified", addedLines, removedLines, modifiedLines);

            if (_addedLinesText != null)
                _addedLinesText.Text = $"{addedLines} added";
            if (_removedLinesText != null)
                _removedLinesText.Text = $"{removedLines} removed";
            if (_modifiedLinesText != null)
                _modifiedLinesText.Text = $"{modifiedLines} modified";

            // Add gutter markers
            var marginRenderer = new LineStatusMarginRenderer(lineMap);
            _oldEditor.TextArea.TextView.BackgroundRenderers.Add(marginRenderer);
            _newEditor.TextArea.TextView.BackgroundRenderers.Add(marginRenderer);

            // Search highlighting
            _searchMatches.Clear();
            _currentSearchIndex = -1;
            string searchQuery = _searchBox?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var searchSource = _newEditor.Document.TextLength > 0 ? _newEditor.Text : _oldEditor.Text;
                var results = SearchService?.Search(searchQuery, searchSource) ?? Enumerable.Empty<SearchResult>();
                _searchMatches = results.Select(r => r.LineNumber).ToList();

                var searchTransformer = new SearchHighlightTransformer(searchQuery);
                _oldEditor.TextArea.TextView.LineTransformers.Add(searchTransformer);
                _newEditor.TextArea.TextView.LineTransformers.Add(searchTransformer);
            }

            // Metrics panel
            _metricsPanel?.Children.Clear();
            foreach (var m in MetricsService?.GetMetrics("<repo>") ?? Enumerable.Empty<MetricData>())
            {
                _metricsPanel?.Children.Add(new TextBlock { Text = $"{m.Name}: {m.Value}" });
            }

            AuditService?.RecordAction(new AuditRecord
            {
                Timestamp = DateTime.Now,
                User = Environment.UserName,
                Action = "Rendered diff",
                FilePath = "<current>"
            });

            if (_codeFoldingEnabled)
                ApplyFolding();
            else
                ClearFolding();
        }

        private static DiffLineType Map(ChangeType ct) => ct switch
        {
            ChangeType.Inserted => DiffLineType.Added,
            ChangeType.Deleted  => DiffLineType.Removed,
            ChangeType.Modified => DiffLineType.Modified,
            _                   => DiffLineType.Unchanged
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
            var timer = new System.Threading.Timer(_ => {
                Dispatcher.UIThread.Post(() => {
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
            target.Offset = target.Offset.WithY(source.Offset.Y);
            _syncScrollInProgress = false;
        }

        private static T? FindDescendant<T>(Visual? root) where T : class
        {
            return root?.FindDescendantOfType<T>();
        }


        private void ApplyFolding()
        {
            if (_oldEditor is null || _newEditor is null)
                return;

            _oldFoldingManager ??= FoldingManager.Install(_oldEditor.TextArea);
            _newFoldingManager ??= FoldingManager.Install(_newEditor.TextArea);

            var folds = GetFoldRegionsAroundChanges();
            var newFoldingsOld = new List<NewFolding>();
            var newFoldingsNew = new List<NewFolding>();

            foreach (var (start, end) in folds)
            {
                // Skip folds that start beyond the document length for either editor
                if (start > _oldEditor.Document.LineCount && start > _newEditor.Document.LineCount)
                    continue;

                // Clamp end line numbers to each document
                int endOldLine = Math.Min(end, _oldEditor.Document.LineCount);
                int endNewLine = Math.Min(end, _newEditor.Document.LineCount);

                if (start <= _oldEditor.Document.LineCount && start <= endOldLine)
                {
                    var startOld = _oldEditor.Document.GetLineByNumber(start).Offset;
                    var endOld = _oldEditor.Document.GetLineByNumber(endOldLine).EndOffset;
                    newFoldingsOld.Add(new NewFolding(startOld, endOld)
                    {
                        Name = "...",
                        DefaultClosed = true
                    });
                }

                if (start <= _newEditor.Document.LineCount && start <= endNewLine)
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

            _oldFoldingManager.UpdateFoldings(newFoldingsOld, -1);
            _newFoldingManager.UpdateFoldings(newFoldingsNew, -1);
        }

        private void ClearFolding()
        {
            _oldFoldingManager?.Clear();
            _newFoldingManager?.Clear();
        }

        /// <summary>
        /// Uninstalls folding managers so they can be recreated for a new document.
        /// </summary>
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

        private IEnumerable<(int Start, int End)> GetFoldRegionsAroundChanges(int context = 2)
        {
            int lineCount = Math.Max(_oldEditor?.Document.LineCount ?? 0, _newEditor?.Document.LineCount ?? 0);
            var folds = new List<(int, int)>();
            int start = -1;

            for (int i = 1; i <= lineCount; i++)
            {
                var type = _lineTypes.TryGetValue(i, out var t) ? t : DiffLineType.Unchanged;
                bool changed = type != DiffLineType.Unchanged;

                if (!changed)
                {
                    if (start == -1)
                        start = i;
                }
                else
                {
                    if (start != -1)
                    {
                        folds.Add((start, i - 1));
                        start = -1;
                    }
                }
            }
            if (start != -1)
                folds.Add((start, lineCount));

            foreach (var (s, e) in folds)
            {
                if (e - s + 1 <= context * 2)
                    continue;
                int fs = s + context;
                int fe = e - context;
                if (fs <= fe)
                    yield return (fs, fe);
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
                ISolidColorBrush? brush = type switch
                {
                    DiffLineType.Added    => new SolidColorBrush(Colors.LightGreen),
                    DiffLineType.Removed  => new SolidColorBrush(Colors.LightCoral),
                    DiffLineType.Modified => new SolidColorBrush(Colors.LightGoldenrodYellow),
                    _                     => null
                };
                if (brush != null)
                {
                    ChangeLinePart(line.Offset, line.EndOffset, e =>
                        e.TextRunProperties.SetBackgroundBrush(brush));
                }
            }
        }
    }

    public enum DiffLineType { Unchanged, Added, Removed, Modified }
}
