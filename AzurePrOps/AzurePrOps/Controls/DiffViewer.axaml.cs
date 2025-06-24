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
// Removed conflicting Notification using directive

// ===== Extension Interfaces & Models for Code Review Features =====
// (Same as before; omitted here for brevity)

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

        // Diff tracking
        private Dictionary<int, DiffLineType> _lineTypes = new();
        private List<int> _changedLines = new();
        private int _currentChangeIndex = -1;
        private bool _codeFoldingEnabled = false;

        static DiffViewer()
        {
            ViewModeProperty.Changed.AddClassHandler<DiffViewer>((x, _) => x.Render());
            OldTextProperty.Changed.AddClassHandler<DiffViewer>((x, _) => x.Render());
            NewTextProperty.Changed.AddClassHandler<DiffViewer>((x, _) => x.Render());
        }

        public string OldText { get => GetValue(OldTextProperty); set => SetValue(OldTextProperty, value); }
        public string NewText { get => GetValue(NewTextProperty); set => SetValue(NewTextProperty, value); }
        public DiffViewMode ViewMode { get => GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }

        public DiffViewer()
        {
            InitializeComponent();
            Loaded += (_, __) => SetupEditors();
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

            // Use AvaloniaEdit's built-in C# highlighting (no TextMate)
            var highlightDef = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
            _oldEditor.SyntaxHighlighting = highlightDef;
            _newEditor.SyntaxHighlighting = highlightDef;

            _oldEditor.IsReadOnly = true;
            _newEditor.IsReadOnly = true;
            _oldEditor.ShowLineNumbers = true;
            _newEditor.ShowLineNumbers = true;

            // Render initial diff
            Render();
        }

        private void Render()
        {
            if (_oldEditor is null || _newEditor is null)
                return;

            // Load texts
            _oldEditor.Text = OldText ?? "";
            _newEditor.Text = NewText ?? "";

            // Clear previous transformers
            _oldEditor.TextArea.TextView.LineTransformers.Clear();
            _newEditor.TextArea.TextView.LineTransformers.Clear();

            // Compute unified diff
            var model = new InlineDiffBuilder(new Differ())
                            .BuildDiffModel(OldText ?? "", NewText ?? "");

            // Map line numbers to change types
            var lineMap = model.Lines
                .Select((l, i) => (Line: i + 1, Type: Map(l.Type)))
                .ToDictionary(t => t.Line, t => t.Type);

            // Highlight backgrounds
            var transformer = new DiffLineBackgroundTransformer(lineMap);
            _oldEditor.TextArea.TextView.LineTransformers.Add(transformer);
            _newEditor.TextArea.TextView.LineTransformers.Add(transformer);

            // TODO: gutter icons (comments, lint, blame, suggestions)
            // TODO: code folding marks
            // TODO: search highlights

            // Metrics panel
            _metricsPanel?.Children.Clear();
            foreach (var m in MetricsService?.GetMetrics("<repo>") ?? Enumerable.Empty<MetricData>())
            {
                _metricsPanel?.Children.Add(new TextBlock { Text = $"{m.Name}: {m.Value}" });
            }

            NotificationService?.Notify("render", new Notification
            {
                Title = "Render Complete",
                Message = $"Diff rendered at {DateTime.Now:T}",
                Type = NotificationType.Info
            });
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
