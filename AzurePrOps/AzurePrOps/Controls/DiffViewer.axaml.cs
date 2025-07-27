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
using Markdown.Avalonia;
using System.IO;

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
        public INotificationService NotificationService { get; set; } = default!;
        public IMetricsService MetricsService { get; set; } = default!;
        public ISuggestionService SuggestionService { get; set; } = default!;
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
        private List<int> _changedLines = new();
        private int _currentChangeIndex = -1;
        private bool _codeFoldingEnabled = true;
        private bool _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
        private bool _wrapLines = DiffPreferences.WrapLines;
        private ToggleButton? _ignoreWhitespaceButton;
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
            NotificationService = new AvaloniaNotificationService();
            MetricsService = new SimpleMetricsService();
            SuggestionService = new SimpleSuggestionService();
            FoldingService = new IndentationFoldingService();
            SearchService = new SimpleSearchService();
            CommentsService = new CommentsService(new AzureDevOpsClient());
            string editor = ConnectionSettingsStorage.TryLoad(out var s)
                ? s!.EditorCommand
                : EditorDetector.GetDefaultEditor();
            IDEService = new IDEIntegrationService(editor);

            // Apply persisted preferences
            _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
            _wrapLines = DiffPreferences.WrapLines;
            DiffPreferences.PreferencesChanged += OnPreferencesChanged;

            InitializeComponent();
            Loaded += (_, __) => SetupEditors();
            Unloaded += (_, __) => DiffPreferences.PreferencesChanged -= OnPreferencesChanged;
        }

        private void OnPreferencesChanged(object? sender, EventArgs e)
        {
            _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
            _wrapLines = DiffPreferences.WrapLines;

            if (_ignoreWhitespaceButton != null)
                _ignoreWhitespaceButton.IsChecked = _ignoreWhitespace;
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
            _wrapLinesButton = this.FindControl<ToggleButton>("PART_WrapLinesButton");
            var copyDiffButton = this.FindControl<Button>("PART_CopyDiffButton");

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

            string oldTextValue = OldText ?? "";
            string newTextValue = NewText ?? "";

            _oldEditor.Document = new AvaloniaEdit.Document.TextDocument(oldTextValue);
            _newEditor.Document = new AvaloniaEdit.Document.TextDocument(newTextValue);
            _oldEditor.Text = oldTextValue;
            _newEditor.Text = newTextValue;

            _oldEditor.TextArea.TextView.LineTransformers.Clear();
            _newEditor.TextArea.TextView.LineTransformers.Clear();
            _oldEditor.TextArea.TextView.BackgroundRenderers.Clear();
            _newEditor.TextArea.TextView.BackgroundRenderers.Clear();

            // Enhanced diff processing with better feedback
            ProcessDiffAndUpdateUI(oldTextValue, newTextValue);

            UpdateStatus("Diff rendered successfully");
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
            foreach (var m in MetricsService?.GetMetrics("<repo>") ?? Enumerable.Empty<MetricData>())
            {
                _metricsPanel?.Children.Add(new TextBlock
                {
                    Text = $"{m.Name}: {m.Value}",
                    FontSize = 12,
                    Margin = new Thickness(8, 0)
                });
            }
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
            target.Offset = target.Offset.WithY(source.Offset.Y);
            _syncScrollInProgress = false;
        }

        private static T? FindDescendant<T>(Visual? root) where T : class
        {
            return root?.FindDescendantOfType<T>();
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

            _logger.LogDebug("GetFoldRegionsAroundChanges: lineCount={LineCount}, _lineTypes.Count={LineTypesCount}",
                lineCount, _lineTypes.Count);

            if (lineCount == 0)
            {
                _logger.LogDebug("GetFoldRegionsAroundChanges: No lines to process");
                yield break;
            }

            // Get all changed line numbers
            var changedLines = new HashSet<int>();
            for (int i = 1; i <= lineCount; i++)
            {
                var type = _lineTypes.TryGetValue(i, out var t) ? t : DiffLineType.Unchanged;
                if (type != DiffLineType.Unchanged)
                {
                    changedLines.Add(i);
                }
            }

            _logger.LogDebug("GetFoldRegionsAroundChanges: Found {ChangedCount} changed lines: [{ChangedLines}]",
                changedLines.Count, string.Join(",", changedLines.OrderBy(x => x)));

            // If no changes, don't fold anything
            if (changedLines.Count == 0)
            {
                _logger.LogDebug("GetFoldRegionsAroundChanges: No changes found, not folding anything");
                yield break;
            }

            // Calculate which lines should be visible (changed lines + context)
            var visibleLines = new HashSet<int>();
            foreach (int changedLine in changedLines)
            {
                // Add the changed line and context around it
                for (int i = Math.Max(1, changedLine - context);
                     i <= Math.Min(lineCount, changedLine + context);
                     i++)
                {
                    visibleLines.Add(i);
                }
            }

            _logger.LogDebug("GetFoldRegionsAroundChanges: {VisibleCount} visible lines with context {Context}: [{VisibleLines}]",
                visibleLines.Count, context, string.Join(",", visibleLines.OrderBy(x => x)));

            // Find consecutive ranges of hidden lines to fold
            int? foldStart = null;
            var foldRegions = new List<(int, int)>();

            for (int i = 1; i <= lineCount; i++)
            {
                bool shouldBeVisible = visibleLines.Contains(i);

                if (!shouldBeVisible)
                {
                    // Start a new fold region if we haven't started one
                    if (foldStart == null)
                        foldStart = i;
                }
                else
                {
                    // End the current fold region if we have one
                    if (foldStart.HasValue)
                    {
                        // Only fold if we have at least 1 line to fold
                        if (i - 1 >= foldStart.Value)
                        {
                            foldRegions.Add((foldStart.Value, i - 1));
                        }
                        foldStart = null;
                    }
                }
            }

            // Handle case where fold region extends to end of file
            if (foldStart.HasValue)
            {
                foldRegions.Add((foldStart.Value, lineCount));
            }

            _logger.LogDebug("GetFoldRegionsAroundChanges: Generated {FoldCount} fold regions: [{FoldRegions}]",
                foldRegions.Count, string.Join(", ", foldRegions.Select(r => $"{r.Item1}-{r.Item2}")));

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
                        backgroundBrush = new SolidColorBrush(Color.FromRgb(255, 248, 197)); // WarningLightBrush equivalent
                        foregroundBrush = new SolidColorBrush(Color.FromRgb(191, 135, 0)); // WarningBrush equivalent
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
