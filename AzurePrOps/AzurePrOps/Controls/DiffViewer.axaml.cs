using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Layout;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

// ===== Extension Interfaces & Models for Code Review Features =====
public interface ICommentProvider
{
    IEnumerable<ReviewComment> GetComments(string filePath, int lineNumber);
    void AddComment(string filePath, int lineNumber, string author, string text);
}
public interface IPullRequestService
{
    (string oldText, string newText) LoadDiff(string repository, int pullRequestId);
}
public interface ILintingService
{
    IEnumerable<LintIssue> Analyze(string text);
}
public interface IBlameService
{
    BlameInfo GetBlame(string filePath, int lineNumber);
}
public interface IAuditTrailService
{
    IEnumerable<AuditRecord> GetHistory(string filePath);
    void RecordAction(AuditRecord record);
}
public interface INotificationService
{
    void Notify(string channel, Notification notification);
}
public interface IMetricsService
{
    IEnumerable<MetricData> GetMetrics(string repository);
}
public interface ISuggestionService
{
    IEnumerable<Suggestion> GetAIHints(string code);
}
public interface IPatchService
{
    PatchResult ApplyPatch(string filePath, string patch);
    void AcceptChange(int changeId);
    void RejectChange(int changeId);
    byte[] DownloadDiff(string filePath);
}
public interface ICodeFoldingService
{
    IEnumerable<FoldRegion> GetFoldRegions(string code);
}
public interface ISearchService
{
    IEnumerable<SearchResult> Search(string query, string code);
}
public interface IIDEIntegrationService
{
    void OpenInIDE(string filePath, int lineNumber);
}

// Data models
public class ReviewComment { public int Id { get; set; } public string Author { get; set; } public int LineNumber { get; set; } public string Text { get; set; } public IEnumerable<ReviewComment> Replies { get; set; } }
public class LintIssue { public int LineNumber { get; set; } public string RuleId { get; set; } public string Message { get; set; } public string Severity { get; set; } }
public class BlameInfo { public int LineNumber { get; set; } public string Author { get; set; } public DateTime Date { get; set; } }
public class AuditRecord { public DateTime Timestamp { get; set; } public string User { get; set; } public string Action { get; set; } public string FilePath { get; set; } }
public class Notification { public string Title { get; set; } public string Message { get; set; } }
public class MetricData { public string Name { get; set; } public double Value { get; set; } }
public class Suggestion { public int LineNumber { get; set; } public string Hint { get; set; } }
public class PatchResult { public bool Success { get; set; } public string[] Messages { get; set; } }
public class FoldRegion { public int StartLine { get; set; } public int EndLine { get; set; } }
public class SearchResult { public int LineNumber { get; set; } public string Context { get; set; } }

namespace AzurePrOps.Controls
{
    public enum DiffViewMode { Unified, SideBySide }
    public partial class DiffViewer : UserControl
    {
        // Diff text
        public static readonly StyledProperty<string> OldTextProperty = AvaloniaProperty.Register<DiffViewer, string>(nameof(OldText));
        public static readonly StyledProperty<string> NewTextProperty = AvaloniaProperty.Register<DiffViewer, string>(nameof(NewText));
        public static readonly StyledProperty<DiffViewMode> ViewModeProperty = AvaloniaProperty.Register<DiffViewer, DiffViewMode>(nameof(ViewMode), DiffViewMode.Unified);

        // Services
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

        private Panel? _contentPanel;
        private TextBox? _searchBox;
        private StackPanel? _metricsPanel;

        static DiffViewer()
        {
            ViewModeProperty.Changed.AddClassHandler<DiffViewer>((x, e) => x.Render());
            OldTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) => x.Render());
            NewTextProperty.Changed.AddClassHandler<DiffViewer>((x, e) => x.Render());
        }

        public string OldText { get => GetValue(OldTextProperty); set => SetValue(OldTextProperty, value); }
        public string NewText { get => GetValue(NewTextProperty); set => SetValue(NewTextProperty, value); }
        public DiffViewMode ViewMode { get => GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }

        public DiffViewer()
        {
            InitializeComponent();
            // Ensure initial rendering in case bound properties were set
            // before InitializeComponent assigned the visual elements.
            Render();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _contentPanel = this.Find<Panel>("PART_ContentPanel");
            _searchBox = this.Find<TextBox>("PART_SearchBox");
            _metricsPanel = this.Find<StackPanel>("PART_MetricsPanel");
            if (_searchBox != null)
                _searchBox.KeyUp += (s, e) => Render();
        }

        /// <summary>
        /// Loads a pull request diff via IPullRequestService and triggers render.
        /// </summary>
        public void LoadPullRequest(string repository, int pullRequestId)
        {
            if (PullRequestService != null)
            {
                var diff = PullRequestService.LoadDiff(repository, pullRequestId);
                OldText = diff.oldText;
                NewText = diff.newText;
            }
        }

        private void Render()
        {
            if (_contentPanel == null)
                return;

            _contentPanel.Children.Clear();
            _metricsPanel?.Children.Clear();

            // Prepare diff and annotations
            var lines = ViewMode == DiffViewMode.Unified
                ? DiffService.CalculateUnified(OldText, NewText).ToList()
                : null;
            var side = ViewMode == DiffViewMode.SideBySide
                ? DiffService.CalculateSideBySide(OldText, NewText)
                : null;

            var lint = LintingService?.Analyze(NewText).ToList() ?? new List<LintIssue>();
            var comments = CommentProvider?.GetComments(string.Empty, 0).ToList() ?? new List<ReviewComment>();
            var suggestions = SuggestionService?.GetAIHints(NewText).ToList() ?? new List<Suggestion>();
            var folds = FoldingService?.GetFoldRegions(NewText).ToList() ?? new List<FoldRegion>();
            var searchTerm = _searchBox?.Text;
            var search = string.IsNullOrEmpty(searchTerm)
                ? new List<SearchResult>()
                : SearchService?.Search(searchTerm, NewText).ToList() ?? new List<SearchResult>();

            // Render main view
            if (ViewMode == DiffViewMode.Unified && lines != null)
                RenderUnified(lines, lint, comments, suggestions, folds, search);
            else if (side != null)
                RenderSideBySide(side.OldText.Lines, side.NewText.Lines, lint, comments, suggestions, folds, search);

            // Render metrics
            var metrics = MetricsService?.GetMetrics("<repo>") ?? Enumerable.Empty<MetricData>();
            foreach (var m in metrics)
                _metricsPanel?.Children.Add(new TextBlock { Text = $"{m.Name}: {m.Value}" });

            NotificationService?.Notify("render", new Notification { Title = "Render Complete", Message = "Diff and metrics rendered." });
            AuditService?.RecordAction(new AuditRecord { Timestamp = DateTime.Now, User = Environment.UserName, Action = "Rendered diff", FilePath = "<current>" });
        }

        private void RenderUnified(List<DiffLine> lines,
            List<LintIssue> lint, List<ReviewComment> comments,
            List<Suggestion> suggestions, List<FoldRegion> folds,
            List<SearchResult> search)
        {
            int lineNo = 1;
            foreach (var line in lines)
            {
                // Fold region
                var fold = folds.FirstOrDefault(f => f.StartLine == lineNo);
                if (fold != null)
                {
                    var btn = new Button { Content = $"+{fold.EndLine - fold.StartLine + 1} lines…" };
                    btn.Click += (s, e) => Render();
                    _contentPanel.Children.Add(btn);
                    lineNo = fold.EndLine + 1;
                    continue;
                }

                // Build gutter DockPanel
                var row = new DockPanel { LastChildFill = true };
                var gutter = new StackPanel { Width = 60, Orientation = Orientation.Horizontal };

                // Blame info
                var blame = BlameService?.GetBlame(string.Empty, lineNo);
                if (blame != null)
                {
                    var blameBlock = new TextBlock { Text = blame.Author };
                    ToolTip.SetTip(blameBlock, blame.Date.ToShortDateString());
                    gutter.Children.Add(blameBlock);
                }

                // Lint
                lint.Where(i => i.LineNumber == lineNo).ToList().ForEach(issue =>
                {
                    var warn = new TextBlock { Text = "⚠" };
                    ToolTip.SetTip(warn, issue.Message);
                    gutter.Children.Add(warn);
                });

                // Comments
                comments.Where(c => c.LineNumber == lineNo).ToList().ForEach(comment =>
                {
                    var btn = new Button { Content = "💬" };
                    ToolTip.SetTip(btn, comment.Text);
                    btn.Click += (s, e) =>
                    {
                        CommentProvider?.AddComment(string.Empty, lineNo, Environment.UserName, "New comment");
                        Render();
                    };
                    gutter.Children.Add(btn);
                });

                // AI Suggestions
                suggestions.Where(su => su.LineNumber == lineNo).ToList().ForEach(sug =>
                {
                    var btn = new Button { Content = "🤖" };
                    ToolTip.SetTip(btn, sug.Hint);
                    btn.Click += (s, e) =>
                    {
                        var result = PatchService?.ApplyPatch(string.Empty, sug.Hint);
                        NotificationService?.Notify("patch", new Notification { Title = "Patch Applied", Message = string.Join(',', result?.Messages ?? Array.Empty<string>()) });
                        Render();
                    };
                    gutter.Children.Add(btn);
                });

                // Accept/Reject for modifications
                if (line.Type == DiffLineType.Modified)
                {
                    var accept = new Button { Content = "✔" };
                    accept.Click += (s, e) => { PatchService?.AcceptChange(lineNo); Render(); };
                    var reject = new Button { Content = "✖" };
                    reject.Click += (s, e) => { PatchService?.RejectChange(lineNo); Render(); };
                    gutter.Children.Add(accept);
                    gutter.Children.Add(reject);
                }

                DockPanel.SetDock(gutter, Dock.Left);
                row.Children.Add(gutter);

                // Line text
                var tb = new TextBlock { FontFamily = FontFamily.Parse("Consolas"), Text = line.Text };
                switch (line.Type)
                {
                    case DiffLineType.Added: tb.Background = Brushes.LightGreen; break;
                    case DiffLineType.Removed: tb.Background = Brushes.LightCoral; break;
                    case DiffLineType.Modified: tb.Background = Brushes.LightGoldenrodYellow; break;
                }
                if (search.Any(sr => sr.LineNumber == lineNo)) tb.Background = Brushes.Yellow;
                row.Children.Add(tb);
                _contentPanel.Children.Add(row);

                lineNo++;
            }
        }

        private void RenderSideBySide(IReadOnlyList<DiffPiece> leftLines,
            IReadOnlyList<DiffPiece> rightLines, List<LintIssue> lint,
            List<ReviewComment> comments, List<Suggestion> suggestions,
            List<FoldRegion> folds, List<SearchResult> search)
        {
            int max = Math.Max(leftLines.Count, rightLines.Count);
            for (int i = 0; i < max; i++)
            {
                var row = new DockPanel { LastChildFill = true };
                int lineNo = i + 1;

                // Left gutter
                var leftGutter = new StackPanel { Width = 60, Orientation = Orientation.Horizontal };
                lint.Where(it => it.LineNumber == lineNo).ToList().ForEach(issue =>
                    leftGutter.Children.Add(new TextBlock { Text = "⚠" }));
                comments.Where(c => c.LineNumber == lineNo).ToList().ForEach(c =>
                    leftGutter.Children.Add(new Button { Content = "💬" }));
                DockPanel.SetDock(leftGutter, Dock.Left);
                row.Children.Add(leftGutter);

                // Left text
                var leftTb = new TextBlock { FontFamily = FontFamily.Parse("Consolas"), Text = i < leftLines.Count ? leftLines[i].Text : string.Empty };
                row.Children.Add(leftTb);

                // Right text
                var rightTb = new TextBlock { FontFamily = FontFamily.Parse("Consolas"), Text = i < rightLines.Count ? rightLines[i].Text : string.Empty };
                DockPanel.SetDock(rightTb, Dock.Right);
                row.Children.Add(rightTb);

                // Right gutter
                var rightGutter = new StackPanel { Width = 60, Orientation = Orientation.Horizontal };
                suggestions.Where(su => su.LineNumber == lineNo).ToList().ForEach(su =>
                    rightGutter.Children.Add(new Button { Content = "🤖" }));
                DockPanel.SetDock(rightGutter, Dock.Right);
                row.Children.Add(rightGutter);

                if (search.Any(sr => sr.LineNumber == lineNo)) row.Background = Brushes.Yellow;
                _contentPanel.Children.Add(row);
            }
        }
    }

    public static class DiffService
    {
        private static readonly Differ _differ = new();
        private static readonly InlineDiffBuilder _inlineBuilder = new(_differ);
        private static readonly SideBySideDiffBuilder _sideBySideBuilder = new(_differ);

        public static IEnumerable<DiffLine> CalculateUnified(string oldText, string newText)
        {
            var model = _inlineBuilder.BuildDiffModel(oldText ?? string.Empty, newText ?? string.Empty);
            return model.Lines.Select(line => new DiffLine(
                line.Type switch
                {
                    ChangeType.Unchanged => DiffLineType.Unchanged,
                    ChangeType.Deleted => DiffLineType.Removed,
                    ChangeType.Inserted => DiffLineType.Added,
                    ChangeType.Modified => DiffLineType.Modified,
                    _ => DiffLineType.Unchanged
                },
                line.Text));
        }

        public static SideBySideDiffModel CalculateSideBySide(string oldText, string newText)
        {
            return _sideBySideBuilder.BuildDiffModel(oldText ?? string.Empty, newText ?? string.Empty);
        }
    }

    public class DiffLine { public DiffLineType Type { get; } public string Text { get; } public DiffLine(DiffLineType type, string text) { Type = type; Text = text; } }
    public enum DiffLineType { Unchanged, Added, Removed, Modified }
}
