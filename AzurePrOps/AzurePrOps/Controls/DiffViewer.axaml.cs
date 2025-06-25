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
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.ReviewLogic.Services;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace AzurePrOps.Controls
{
    public enum DiffViewMode { Unified, SideBySide }

    public partial class DiffViewer : UserControl
    {
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

        // Keep a reference to the current diff so we can inspect the raw patch
        private FileDiff? _currentDiff;

        // Diff tracking
        private Dictionary<int, DiffLineType> _lineTypes = new();
        private List<int> _changedLines = new();
        private int _currentChangeIndex = -1;
        private bool _codeFoldingEnabled = false;

        static DiffViewer()
        {
            ViewModeProperty.Changed.AddClassHandler<DiffViewer>((x, e) => {
                Console.WriteLine($"DiffViewer ViewMode changed: {e.OldValue} -> {e.NewValue}");
                x.Render();
            });
            OldTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) => {
                Console.WriteLine($"DiffViewer OldText changed: Length = {(e.NewValue as string)?.Length ?? 0}");
                x.Render();
            });
            NewTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) => {
                Console.WriteLine($"DiffViewer NewText changed: Length = {(e.NewValue as string)?.Length ?? 0}");
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
            IDEService         = new IDEIntegrationService();

            InitializeComponent();
            Loaded += (_, __) => SetupEditors();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            Console.WriteLine($"OnDataContextChanged: {DataContext?.GetType().Name ?? "null"}");

            if (DataContext is FileDiff diff)
            {
                // Assign bound texts when the DataContext changes so the viewer
                // renders even if the control is created before data is set.
                OldText = diff.OldText ?? string.Empty;
                NewText = diff.NewText ?? string.Empty;
                _currentDiff = diff;
                Console.WriteLine($"DataContext changed to FileDiff: {diff.FilePath}");

                // Render again once the control is fully loaded to ensure the
                // text appears even if DataContext was set before loading
                Dispatcher.UIThread.Post(() =>
                {
                    Console.WriteLine("Post-DataContextChanged render");
                    Render();
                }, DispatcherPriority.Loaded);
            }
            else
            {
                // Clear content when the DataContext is unset or of the wrong type
                OldText = string.Empty;
                NewText = string.Empty;
                _currentDiff = null;
                Console.WriteLine("DataContext cleared or invalid");
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
            var codeFoldingButton = this.FindControl<Button>("PART_CodeFoldingButton");
            var copyButton = this.FindControl<Button>("PART_CopyButton");

            // Wire up events
            if (_searchBox != null)
            {
                _searchBox.KeyUp += (_, __) => Render();
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

            if (codeFoldingButton != null)
            {
                codeFoldingButton.Click += (_, __) => ToggleCodeFolding();
            }

            if (copyButton != null)
            {
                copyButton.Click += (_, __) => CopySelectedText();
            }
        }

        private void SetupEditors()
        {
            if (_oldEditor is null || _newEditor is null)
                return;

            Console.WriteLine("Setting up DiffViewer editors");

            // Use AvaloniaEdit's built-in C# highlighting (no TextMate)
            var highlightDef = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
            _oldEditor.SyntaxHighlighting = highlightDef;
            _newEditor.SyntaxHighlighting = highlightDef;

            _oldEditor.IsReadOnly = true;
            _newEditor.IsReadOnly = true;
            _oldEditor.ShowLineNumbers = true;
            _newEditor.ShowLineNumbers = true;

            // Explicitly set editor foreground/background to match the app theme
            var fgBrush = (IBrush?)Application.Current?.FindResource("PrimaryBrush");
            var bgBrush = (IBrush?)Application.Current?.FindResource("CardBackgroundBrush");
            if (fgBrush != null && bgBrush != null)
            {
                _oldEditor.Foreground = fgBrush;
                _oldEditor.Background = bgBrush;
                _newEditor.Foreground = fgBrush;
                _newEditor.Background = bgBrush;
                Console.WriteLine($"OldEditor colors - FG: {fgBrush}, BG: {bgBrush}");
                Console.WriteLine($"NewEditor colors - FG: {fgBrush}, BG: {bgBrush}");

                if (fgBrush is ISolidColorBrush fgSolid && bgBrush is ISolidColorBrush bgSolid)
                {
                    bool sameColor = fgSolid.Color.ToUInt32() == bgSolid.Color.ToUInt32();
                    Console.WriteLine($"Foreground matches background? {sameColor}");
                    Console.WriteLine($"OldEditor alpha: {fgSolid.Color.A}, BG alpha: {bgSolid.Color.A}");
                }
            }
            else
            {
                Console.WriteLine("Theme brushes missing - using defaults");
                _oldEditor.Foreground = Brushes.White;
                _oldEditor.Background = Brushes.Black;
                _newEditor.Foreground = Brushes.White;
                _newEditor.Background = Brushes.Black;
            }

            // Set explicit height for better visibility
            _oldEditor.MinHeight = 250;
            _newEditor.MinHeight = 250;

            // Render initial diff
            Render();

            // Trigger a second render on the UI thread once layout has completed
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine("Post-SetupEditors render");
                Render();
            }, DispatcherPriority.Loaded);
        }

        public void Render()
        {
            Console.WriteLine($"DiffViewer.Render() called");
            if (_oldEditor is null || _newEditor is null)
            {
                Console.WriteLine($"DiffViewer.Render(): editors are null, aborting render");
                return;
            }

            // Load texts
            Console.WriteLine($"DiffViewer.Render(): OldText length={OldText?.Length ?? 0}, NewText length={NewText?.Length ?? 0}");

            // Make sure we explicitly set the Document's text, not just the editor's Text property
            string oldTextValue = NormalizeLineEndings(OldText ?? "");
            string newTextValue = NormalizeLineEndings(NewText ?? "");

            // Force text display by setting document text directly
            if (_oldEditor.Document == null)
                _oldEditor.Document = new AvaloniaEdit.Document.TextDocument();
            if (_newEditor.Document == null)
                _newEditor.Document = new AvaloniaEdit.Document.TextDocument();

            _oldEditor.Document.Text = oldTextValue;
            _newEditor.Document.Text = newTextValue;

            // Also set the Text property for consistency
            _oldEditor.Text = oldTextValue;
            _newEditor.Text = newTextValue;

            // Force redraw after setting text to ensure content becomes visible
            _oldEditor.InvalidateVisual();
            _newEditor.InvalidateVisual();
            _oldEditor.TextArea.TextView.InvalidateVisual();
            _newEditor.TextArea.TextView.InvalidateVisual();
            _oldEditor.TextArea.TextView.EnsureVisualLines();
            _newEditor.TextArea.TextView.EnsureVisualLines();
            Console.WriteLine($"Editors invalidated for redraw");
            Console.WriteLine($"VisualLines valid? old: {_oldEditor.TextArea.TextView.VisualLinesValid}, new: {_newEditor.TextArea.TextView.VisualLinesValid}");
            Console.WriteLine($"OldEditor size: {_oldEditor.Bounds.Width}x{_oldEditor.Bounds.Height}");
            Console.WriteLine($"NewEditor size: {_newEditor.Bounds.Width}x{_newEditor.Bounds.Height}");
            Console.WriteLine($"Visual line count old: {_oldEditor.TextArea.TextView.VisualLines.Count}, new: {_newEditor.TextArea.TextView.VisualLines.Count}");
            Console.WriteLine($"OldEditor visible: {_oldEditor.IsVisible}, effective: {_oldEditor.IsEffectivelyVisible}, opacity: {_oldEditor.Opacity}");
            Console.WriteLine($"NewEditor visible: {_newEditor.IsVisible}, effective: {_newEditor.IsEffectivelyVisible}, opacity: {_newEditor.Opacity}");
            if (_oldEditor.Foreground is ISolidColorBrush oldFg && _oldEditor.Background is ISolidColorBrush oldBg)
                Console.WriteLine($"OldEditor colors numeric - FG: {oldFg.Color}, BG: {oldBg.Color}");
            if (_newEditor.Foreground is ISolidColorBrush newFg && _newEditor.Background is ISolidColorBrush newBg)
                Console.WriteLine($"NewEditor colors numeric - FG: {newFg.Color}, BG: {newBg.Color}");

            Console.WriteLine($"Set document text - Old: {oldTextValue.Length} bytes, New: {newTextValue.Length} bytes");
            Console.WriteLine($"OldEditor.Document length now: {_oldEditor.Document.TextLength}");
            Console.WriteLine($"NewEditor.Document length now: {_newEditor.Document.TextLength}");
            Console.WriteLine($"OldEditor line count: {_oldEditor.Document.LineCount}");
            Console.WriteLine($"NewEditor line count: {_newEditor.Document.LineCount}");
            Console.WriteLine($"Old text preview: '{oldTextValue.Substring(0, Math.Min(100, oldTextValue.Length))}'");
            Console.WriteLine($"New text preview: '{newTextValue.Substring(0, Math.Min(100, newTextValue.Length))}'");
            Console.WriteLine($"Document first lines - old: '{_oldEditor.Document.GetText(0, Math.Min(100, _oldEditor.Document.TextLength))}'");
            Console.WriteLine($"Document first lines - new: '{_newEditor.Document.GetText(0, Math.Min(100, _newEditor.Document.TextLength))}'");

            // Clear previous transformers
            _oldEditor.TextArea.TextView.LineTransformers.Clear();
            _newEditor.TextArea.TextView.LineTransformers.Clear();

            // Clear previous background renderers
            _oldEditor.TextArea.TextView.BackgroundRenderers.Clear();
            _newEditor.TextArea.TextView.BackgroundRenderers.Clear();

            // Compute unified diff
            Console.WriteLine($"Building diff model with OldText ({OldText?.Length ?? 0} bytes) and NewText ({NewText?.Length ?? 0} bytes)");

            // Add explicit handling for empty string cases
            string oldTextForDiff = OldText ?? "";
            string newTextForDiff = NewText ?? "";

            // Handle special case for new files (empty old text)
            if (string.IsNullOrEmpty(oldTextForDiff) && !string.IsNullOrEmpty(newTextForDiff))
            {
                Console.WriteLine("Special case: New file detected (empty old text)");
                // For a new file, all lines should be marked as added
                var newLines = newTextForDiff.Split('\n');
                _lineTypes = newLines
                    .Select((_, i) => (Line: i + 1, Type: DiffLineType.Added))
                    .ToDictionary(t => t.Line, t => t.Type);

                Console.WriteLine($"Created manual _lineTypes with {_lineTypes.Count} lines, all marked as added");

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
        Console.WriteLine($"Detected special file marker. IsDeleted={isDeletedFile}, IsNew={isNewFile}");
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
                Console.WriteLine("Special case: Deleted file detected (empty new text)");
                // For a deleted file, all lines should be marked as removed
                var oldLines = oldTextForDiff.Split('\n');
                _lineTypes = oldLines
                    .Select((_, i) => (Line: i + 1, Type: DiffLineType.Removed))
                    .ToDictionary(t => t.Line, t => t.Type);

                Console.WriteLine($"Created manual _lineTypes with {_lineTypes.Count} lines, all marked as removed");

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
                            .BuildDiffModel(oldTextForDiff, newTextForDiff);

            // Check if texts are identical - compare actual content instead of just length
            bool textsAreIdentical = string.Equals(oldTextForDiff, newTextForDiff, StringComparison.Ordinal);
            if (!textsAreIdentical && oldTextForDiff.Length == newTextForDiff.Length)
            {
                Console.WriteLine("Equal length texts with different content detected!");
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
                Console.WriteLine("Edge case: Empty diff model with non-empty content, creating manual mapping");
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
                        Console.WriteLine("Equal length but different content detected - creating manual diff");

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

                Console.WriteLine($"Created manual lineTypes with {_lineTypes.Count} lines");
                return;
            }

            Console.WriteLine($"Built diff model with {model.Lines.Count} lines and {lineMap.Count(kv => kv.Value != DiffLineType.Unchanged)} changes");

            if (_currentDiff != null && !string.IsNullOrEmpty(_currentDiff.Diff))
            {
                var patchLines = _currentDiff.Diff.Split('\n');
                int addedFromPatch = patchLines.Count(l => l.StartsWith("+") && !l.StartsWith("+++"));
                int removedFromPatch = patchLines.Count(l => l.StartsWith("-") && !l.StartsWith("---"));
                Console.WriteLine($"Patch stats - added: {addedFromPatch}, removed: {removedFromPatch}");
            }

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
                Console.WriteLine("Special case for stats: New file detected");
                addedLines = NewText.Split('\n').Length;
                removedLines = 0;
                modifiedLines = 0;
            }

            // Special case for deleted files (if there's only old content)
            if (!string.IsNullOrWhiteSpace(OldText) && string.IsNullOrWhiteSpace(NewText))
            {
                Console.WriteLine("Special case for stats: Deleted file detected");
                addedLines = 0;
                removedLines = OldText.Split('\n').Length;
                modifiedLines = 0;
            }

            // Log line counts for debugging
            Console.WriteLine($"DiffViewer stats: {addedLines} added, {removedLines} removed, {modifiedLines} modified");

            // Log first displayed line for extra visibility
            var firstOld = _oldEditor.Document.Lines.Count > 0 ? _oldEditor.Document.GetText(_oldEditor.Document.Lines.First()) : "<none>";
            var firstNew = _newEditor.Document.Lines.Count > 0 ? _newEditor.Document.GetText(_newEditor.Document.Lines.First()) : "<none>";
            Console.WriteLine($"First lines - old: '{firstOld}', new: '{firstNew}'");

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

            // Metrics panel
            _metricsPanel?.Children.Clear();
            foreach (var m in MetricsService?.GetMetrics("<repo>") ?? Enumerable.Empty<MetricData>())
            {
                _metricsPanel?.Children.Add(new TextBlock { Text = $"{m.Name}: {m.Value}" });
            }

            // NotificationService?.Notify("render", new Notification
            // {
            //     Title = "Render Complete",
            //     Message = $"Diff rendered at {DateTime.Now:T}",
            //     Type = NotificationType.Info
            // });
            AuditService?.RecordAction(new AuditRecord
            {
                Timestamp = DateTime.Now,
                User = Environment.UserName,
                Action = "Rendered diff",
                FilePath = "<current>"
            });
        }

        private static DiffLineType Map(ChangeType ct) => ct switch
        {
            ChangeType.Inserted => DiffLineType.Added,
            ChangeType.Deleted  => DiffLineType.Removed,
            ChangeType.Modified => DiffLineType.Modified,
            _                   => DiffLineType.Unchanged
        };

        private static string NormalizeLineEndings(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n');

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

        private void ToggleCodeFolding()
        {
            _codeFoldingEnabled = !_codeFoldingEnabled;

    // Make sure we set the documents directly
    if (!string.IsNullOrEmpty(OldText))
    {
        _oldEditor.Text = OldText;
        _oldEditor.Document.Text = OldText;
    }

    if (!string.IsNullOrEmpty(NewText))
    {
        _newEditor.Text = NewText;
        _newEditor.Document.Text = NewText;
    }

            if (_oldEditor != null && _newEditor != null)
            {
                if (_codeFoldingEnabled)
                {
                    // Apply code folding to both editors
                    var folds = FoldingService?.GetFoldRegions(OldText ?? "") ?? Enumerable.Empty<FoldRegion>();

                    // TODO: Apply folding to the editors
                    // This would require integrating with AvaloniaEdit's folding manager
                }
                else
                {
                    // Remove all folding
                    // TODO: Remove folding from editors
                }

                Render(); // Refresh the view
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
                    // TODO: Add clipboard support when available
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
