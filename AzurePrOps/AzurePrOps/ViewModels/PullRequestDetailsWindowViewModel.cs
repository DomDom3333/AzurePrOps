using System;
using System.Diagnostics;
using System.Reactive;
using Avalonia.Threading;
using ReactiveUI;
using ConnectionModels = AzurePrOps.AzureConnection.Models;
using ReviewModels = AzurePrOps.ReviewLogic.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using AzurePrOps.Views;
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.ReviewLogic.Services;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;

namespace AzurePrOps.ViewModels;

public class PullRequestDetailsWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestDetailsWindowViewModel>();
    private readonly AzureDevOpsClient _client = new();
    private readonly IPullRequestService _pullRequestService;
    private readonly ConnectionSettings _settings;
    private readonly ICommentsService _commentsService;
    public ConnectionModels.PullRequestInfo PullRequest { get; }
    public int PullRequestId => PullRequest.Id;

    public ObservableCollection<ConnectionModels.PullRequestComment> Comments { get; }
    public ObservableCollection<CommentThread> Threads { get; } = new();

    private bool _showUnresolvedOnly = true;
    public bool ShowUnresolvedOnly
    {
        get => _showUnresolvedOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showUnresolvedOnly, value);
            this.RaisePropertyChanged(nameof(FilteredThreads));
        }
    }

    public IEnumerable<CommentThread> FilteredThreads =>
        ShowUnresolvedOnly ? Threads.Where(t => !string.Equals(t.Status, "closed", StringComparison.OrdinalIgnoreCase)) : Threads;

    public ReactiveCommand<Unit, Unit> OpenInBrowserCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowInsightsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowCommentsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDiffSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshDiffsCommand { get; }
    public ReactiveCommand<Unit, Unit> CompleteCommand { get; }
    public ReactiveCommand<Unit, Unit> AbandonCommand { get; }
    public ReactiveCommand<Unit, Unit> PostCommentCommand { get; }

    private string _newCommentText = string.Empty;
    public string NewCommentText
    {
        get => _newCommentText;
        set => this.RaiseAndSetIfChanged(ref _newCommentText, value);
    }

    public ObservableCollection<ReviewModels.FileDiff> FileDiffs { get; } = new();

    public PullRequestDetailsWindowViewModel(
        ConnectionModels.PullRequestInfo pullRequest,
        IPullRequestService pullRequestService,
        ConnectionSettings settings,
        IEnumerable<ConnectionModels.PullRequestComment>? comments = null,
        IEnumerable<ReviewModels.FileDiff>? diffs = null,
        ICommentsService? commentsService = null)
    {
        PullRequest = pullRequest;
        _pullRequestService = pullRequestService;
        _settings = settings;
        _commentsService = commentsService ?? new CommentsService(new AzureDevOpsClient());
        Comments = comments != null
            ? new ObservableCollection<ConnectionModels.PullRequestComment>(comments)
            : new ObservableCollection<ConnectionModels.PullRequestComment>();
        if (diffs != null)
        {
            LoadDiffs(diffs);
        }

        _ = LoadThreadsAsync();

        OpenInBrowserCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrWhiteSpace(PullRequest.WebUrl))
                return;
            try
            {
                // Use the validated WebUrl property instead of the raw Url
                string sanitizedUrl = PullRequest.WebUrl.Trim();
                if (!Uri.IsWellFormedUriString(sanitizedUrl, UriKind.Absolute))
                {
                    _logger.LogWarning("Invalid URL format: {Url}", sanitizedUrl);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = sanitizedUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // Swallow exceptions to avoid crashing the app
                _logger.LogWarning(ex, "Failed to open browser");
            }
        });

        ShowCommentsCommand = ReactiveCommand.Create(() =>
        {
            string title = $"Comments for PR #{PullRequest.Id}";
            var vm = new CommentsWindowViewModel(title, Comments);
            var window = new CommentsWindow { DataContext = vm };
            window.Show();
        });

        ShowInsightsCommand = ReactiveCommand.Create(() =>
        {
            var metrics = new List<MetricData>
            {
                new MetricData { Name = "Files Changed", Value = FileDiffs.Count },
                new MetricData { Name = "Comments",     Value = Comments.Count }
            };

            int added = 0;
            int removed = 0;
            foreach (var diff in FileDiffs)
            {
                if (string.IsNullOrWhiteSpace(diff.Diff))
                    continue;

                foreach (var line in diff.Diff.Split('\n'))
                {
                    if (line.StartsWith("+") && !line.StartsWith("+++ "))
                        added++;
                    else if (line.StartsWith("-") && !line.StartsWith("--- "))
                        removed++;
                }
            }

            metrics.Add(new MetricData { Name = "Lines Added",   Value = added });
            metrics.Add(new MetricData { Name = "Lines Removed", Value = removed });

            string title = $"Insights for PR #{PullRequest.Id}";
            var vm = new InsightsWindowViewModel(title, metrics);
            var window = new InsightsWindow { DataContext = vm };
            window.Show();
        });

        OpenDiffSettingsCommand = ReactiveCommand.Create(() =>
        {
            var vm = new DiffSettingsWindowViewModel();
            var window = new DiffSettingsWindow { DataContext = vm };
            window.Show();
        });

        RefreshDiffsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var diffs = await _pullRequestService.GetPullRequestDiffAsync(
                    _settings.Organization,
                    _settings.Project,
                    _settings.Repository,
                    PullRequest.Id,
                    _settings.PersonalAccessToken,
                    null,
                    null);

                LoadDiffs(diffs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh diffs");
            }
        });

        CompleteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var options = new ReviewModels.MergeOptions(false, false, string.Empty);
            await _client.CompletePullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                options,
                _settings.PersonalAccessToken);
        });

        AbandonCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _client.AbandonPullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                _settings.PersonalAccessToken);
        });

        PostCommentCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewCommentText))
                return;

            await _client.PostPullRequestCommentAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                NewCommentText,
                _settings.PersonalAccessToken);

            NewCommentText = string.Empty;
        });
    }

    private void LoadDiffs(IEnumerable<ReviewModels.FileDiff> diffs)
    {
        FileDiffs.Clear();
        _logger.LogDebug("Processing {Count} diffs", diffs.Count());
        foreach (var d in diffs)
        {
            var fileDiff = d;
            _logger.LogDebug("Processing diff for {Path}", fileDiff.FilePath);
            _logger.LogDebug("  Original OldText length: {Length}", fileDiff.OldText?.Length ?? 0);
            _logger.LogDebug("  Original NewText length: {Length}", fileDiff.NewText?.Length ?? 0);
            _logger.LogDebug("  Original Diff length: {Length}", fileDiff.Diff?.Length ?? 0);

            if (string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText) && !string.IsNullOrEmpty(fileDiff.Diff))
            {
                _logger.LogDebug("  Creating placeholder content for {Path}", fileDiff.FilePath);
                var (oldContent, newContent) = ParseDiffToContent(fileDiff.Diff);

                bool isNewFile = oldContent.Contains("[FILE ADDED]");
                bool isDeletedFile = newContent.Contains("[FILE DELETED]");

                _logger.LogDebug("  Parsed diff content: isNewFile={IsNew}, isDeletedFile={IsDeleted}", isNewFile, isDeletedFile);
                _logger.LogDebug("  oldContent length: {OldLength}, newContent length: {NewLength}", oldContent.Length, newContent.Length);

                if (string.IsNullOrWhiteSpace(oldContent) && string.IsNullOrWhiteSpace(newContent))
                {
                    if (fileDiff.Diff.Contains("new file mode") || fileDiff.Diff.Contains("/dev/null") && fileDiff.Diff.Contains("+++ b/"))
                    {
                        oldContent = "[FILE ADDED]\n";
                        var addedLines = new List<string>();
                        foreach (var line in fileDiff.Diff.Split('\n'))
                        {
                            if (line.StartsWith("+") && !line.StartsWith("+++ "))
                            {
                                addedLines.Add(line.Substring(1));
                            }
                        }
                        newContent = string.Join("\n", addedLines);
                        if (string.IsNullOrWhiteSpace(newContent))
                        {
                            newContent = "[Added file content not available]\n";
                        }
                    }
                    else if (fileDiff.Diff.Contains("deleted file mode") || fileDiff.Diff.Contains("--- a/") && fileDiff.Diff.Contains("+++ /dev/null"))
                    {
                        var removedLines = new List<string>();
                        foreach (var line in fileDiff.Diff.Split('\n'))
                        {
                            if (line.StartsWith("-") && !line.StartsWith("--- "))
                            {
                                removedLines.Add(line.Substring(1));
                            }
                        }
                        oldContent = string.Join("\n", removedLines);
                        if (string.IsNullOrWhiteSpace(oldContent))
                        {
                            oldContent = "[Deleted file content not available]\n";
                        }
                        newContent = "[FILE DELETED]\n";
                    }
                    else
                    {
                        oldContent = "[No original content could be extracted]\n";
                        newContent = "[No modified content could be extracted]\n";

                        if (!string.IsNullOrEmpty(fileDiff.Diff))
                        {
                            newContent = fileDiff.Diff;
                        }
                    }
                }

                string oldText = !string.IsNullOrEmpty(oldContent) ? oldContent : isNewFile ? "[FILE ADDED]\n" : "[Original content not available]";
                string newText = !string.IsNullOrEmpty(newContent) ? newContent : isDeletedFile ? "[FILE DELETED]\n" : "[Modified content not available]";

                if ((string.IsNullOrWhiteSpace(oldText) || oldText.StartsWith("[")) &&
                    (string.IsNullOrWhiteSpace(newText) || newText.StartsWith("[")) &&
                    !string.IsNullOrEmpty(fileDiff.Diff))
                {
                    string diffHeader = "Showing raw diff content:\n\n";
                    oldText = "[Original content]\n" + diffHeader + fileDiff.Diff;
                    newText = "[Modified content]\n" + diffHeader + fileDiff.Diff;
                }

                if (!oldText.Contains('\n')) oldText += "\n \n ";
                if (!newText.Contains('\n')) newText += "\n \n ";

                fileDiff = new ReviewModels.FileDiff(
                    fileDiff.FilePath,
                    fileDiff.Diff,
                    oldText,
                    newText
                );
            }
            else if (string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText))
            {
                _logger.LogWarning("  WARNING: No content available for {Path}", fileDiff.FilePath);
                fileDiff = new ReviewModels.FileDiff(
                    fileDiff.FilePath,
                    "No diff content available.",
                    "[No original content available]\n \n ",
                    "[No new content available]\n \n "
                );
            }
            else if (string.IsNullOrEmpty(fileDiff.OldText) && !string.IsNullOrEmpty(fileDiff.NewText))
            {
                _logger.LogDebug("  New file detected: {Path}", fileDiff.FilePath);
                fileDiff = new ReviewModels.FileDiff(
                    fileDiff.FilePath,
                    fileDiff.Diff ?? string.Empty,
                    "[FILE ADDED]\n",
                    fileDiff.NewText
                );
            }
            else if (!string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText))
            {
                _logger.LogDebug("  Deleted file detected: {Path}", fileDiff.FilePath);
                fileDiff = new ReviewModels.FileDiff(
                    fileDiff.FilePath,
                    fileDiff.Diff ?? string.Empty,
                    fileDiff.OldText,
                    "[FILE DELETED]\n"
                );
            }

            _logger.LogDebug("  Final OldText length: {Length}", fileDiff.OldText?.Length ?? 0);
            _logger.LogDebug("  Final NewText length: {Length}", fileDiff.NewText?.Length ?? 0);
            FileDiffs.Add(fileDiff);

            Dispatcher.UIThread.Post(() => { }, DispatcherPriority.Background);
        }
    }

    private async Task LoadThreadsAsync()
    {
        try
        {
            var threads = await _commentsService.GetThreadsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                _settings.PersonalAccessToken);

            Threads.Clear();
            foreach (var t in threads)
                Threads.Add(t);

            this.RaisePropertyChanged(nameof(FilteredThreads));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load threads");
        }
    }

            // Helper method to parse a unified diff format into old and new content
            public (string oldContent, string newContent) ParseDiffToContent(string diffText)
            {
        if (string.IsNullOrEmpty(diffText))
            return (string.Empty, string.Empty);

        var oldContentLines = new List<string>();
        var newContentLines = new List<string>();

            // Check for new or deleted file indicators in the diff
            bool isNewFile = diffText.Contains("new file mode") || diffText.Contains("/dev/null") && diffText.Contains("+++ b/");
            bool isDeletedFile = diffText.Contains("deleted file mode") || diffText.Contains("--- a/") && diffText.Contains("+++ /dev/null");

            _logger.LogDebug("ParseDiffToContent - isNewFile: {IsNew}, isDeletedFile: {IsDeleted}", isNewFile, isDeletedFile);
            _logger.LogDebug("Diff text length: {Length} bytes", diffText.Length);

            // Log the first few lines of the diff for debugging
            var diffStart = string.Join("\n", diffText.Split('\n').Take(10));
            _logger.LogDebug("Diff starts with:\n{Start}", diffStart);

            // Count how many '+' and '-' lines we have
            int addedLineCount = diffText.Split('\n').Count(l => l.StartsWith("+") && !l.StartsWith("+++ "));
            int removedLineCount = diffText.Split('\n').Count(l => l.StartsWith("-") && !l.StartsWith("--- "));
            _logger.LogDebug("Diff contains {Added} added lines and {Removed} removed lines", addedLineCount, removedLineCount);

        // Skip header lines that start with "diff", "index", "---", "+++", etc.
        var lines = diffText.Split('\n');
        bool inContent = false;

        foreach (var line in lines)
        {
            // Skip diff metadata until we get to a hunk header
            if (!inContent)
            {
                if (line.StartsWith("@@ "))
                    inContent = true;
                continue;
            }

            if (line.StartsWith("+") && !line.StartsWith("+++ "))
            {
                // Added line - goes only to new content
                newContentLines.Add(line.Substring(1));
            }
            else if (line.StartsWith("-") && !line.StartsWith("--- "))
            {
                // Removed line - goes only to old content
                oldContentLines.Add(line.Substring(1));
            }
            else if (!line.StartsWith("@@ "))
            {
                // Context line - goes to both old and new content
                if (line.StartsWith(" "))
                {
                    oldContentLines.Add(line.Substring(1));
                    newContentLines.Add(line.Substring(1));
                }
                else
                {
                    oldContentLines.Add(line);
                    newContentLines.Add(line);
                }
            }
        }

        string oldContent = string.Join("\n", oldContentLines);
        string newContent = string.Join("\n", newContentLines);

        // Add file status indicators if needed
        if (isNewFile && string.IsNullOrWhiteSpace(oldContent))
        {
            oldContent = "[FILE ADDED]\n";
            _logger.LogDebug("Added [FILE ADDED] marker to oldContent");
        }
        else if (isDeletedFile && string.IsNullOrWhiteSpace(newContent))
        {
            newContent = "[FILE DELETED]\n";
            _logger.LogDebug("Added [FILE DELETED] marker to newContent");
        }

        // If we couldn't extract any content but have the raw diff, show the raw diff
        if (string.IsNullOrWhiteSpace(oldContent) && string.IsNullOrWhiteSpace(newContent) && !string.IsNullOrWhiteSpace(diffText))
        {
            _logger.LogDebug("Using raw diff as content since no content could be extracted");
            if (isNewFile)
            {
                oldContent = "[FILE ADDED]\n";
                newContent = diffText; // Show the raw diff in the new content area
            }
            else if (isDeletedFile)
            {
                oldContent = diffText; // Show the raw diff in the old content area
                newContent = "[FILE DELETED]\n";
            }
            else
            {
                // For modified files where we couldn't extract content properly
                oldContent = "[Original content not available]\n\nRaw diff:\n" + diffText;
                newContent = "[Modified content not available]\n\nRaw diff:\n" + diffText;
            }
        }

        // Ensure we have reasonable content lengths for display
        if (oldContent.Length < 5 && !isNewFile)
        {
            oldContent = "[No significant content available for original version]\n";
        }

        if (newContent.Length < 5 && !isDeletedFile)
        {
            newContent = "[No significant content available for new version]\n";
        }

        return (oldContent, newContent);
    }
}
