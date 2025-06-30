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
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.ViewModels;

public class PullRequestDetailsWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestDetailsWindowViewModel>();
    public ConnectionModels.PullRequestInfo PullRequest { get; }

    public ObservableCollection<ConnectionModels.PullRequestComment> Comments { get; }

    public ReactiveCommand<Unit, Unit> OpenInBrowserCommand { get; }

    public ObservableCollection<ReviewModels.FileDiff> FileDiffs { get; } = new();

    public PullRequestDetailsWindowViewModel(ConnectionModels.PullRequestInfo pullRequest,
        IEnumerable<ConnectionModels.PullRequestComment>? comments = null,
        IEnumerable<ReviewModels.FileDiff>? diffs = null)
    {
        PullRequest = pullRequest;
        Comments = comments != null
            ? new ObservableCollection<ConnectionModels.PullRequestComment>(comments)
            : new ObservableCollection<ConnectionModels.PullRequestComment>();
        if (diffs != null)
        {
            _logger.LogDebug("Processing {Count} diffs in PullRequestDetailsWindowViewModel constructor", diffs.Count());
            foreach (var d in diffs)
            {
                // Ensure there's at least some content for the diff viewer to display
                var fileDiff = d;
                _logger.LogDebug("Processing diff for {Path}", fileDiff.FilePath);
                _logger.LogDebug("  Original OldText length: {Length}", fileDiff.OldText?.Length ?? 0);
                _logger.LogDebug("  Original NewText length: {Length}", fileDiff.NewText?.Length ?? 0);
                _logger.LogDebug("  Original Diff length: {Length}", fileDiff.Diff?.Length ?? 0);

                // If both texts are empty but we have a diff string, create placeholder content
                if (string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText) && !string.IsNullOrEmpty(fileDiff.Diff))
                {
                    _logger.LogDebug("  Creating placeholder content for {Path}", fileDiff.FilePath);
                    // Parse the diff string to create old and new text representations
                    var (oldContent, newContent) = ParseDiffToContent(fileDiff.Diff);

                    // Check for special file statuses
                    bool isNewFile = oldContent.Contains("[FILE ADDED]");
                    bool isDeletedFile = newContent.Contains("[FILE DELETED]");

                    _logger.LogDebug("  Parsed diff content: isNewFile={IsNew}, isDeletedFile={IsDeleted}", isNewFile, isDeletedFile);
                    _logger.LogDebug("  oldContent length: {OldLength}, newContent length: {NewLength}", oldContent.Length, newContent.Length);

                    // Check for empty content and provide more meaningful placeholders
                    if (string.IsNullOrWhiteSpace(oldContent) && string.IsNullOrWhiteSpace(newContent))
                    {
                        // If ParseDiffToContent couldn't extract anything useful, try to determine the file type
                        if (fileDiff.Diff.Contains("new file mode") || fileDiff.Diff.Contains("/dev/null") && fileDiff.Diff.Contains("+++ b/"))
                        {
                            oldContent = "[FILE ADDED]\n";
                            // Extract file content from the diff by looking for lines starting with '+'
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
                            // Extract file content from the diff by looking for lines starting with '-'
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
                            // For modified files
                            oldContent = "[No original content could be extracted]\n";
                            newContent = "[No modified content could be extracted]\n";

                            // Try to show the actual diff content in the 'After' view
                            if (!string.IsNullOrEmpty(fileDiff.Diff))
                            {
                                newContent = fileDiff.Diff;
                            }
                        }
                    }

                    // Ensure we have at least some content for the diff viewer
                    string oldText = !string.IsNullOrEmpty(oldContent) ? oldContent : isNewFile ? "[FILE ADDED]\n" : "[Original content not available]";
                    string newText = !string.IsNullOrEmpty(newContent) ? newContent : isDeletedFile ? "[FILE DELETED]\n" : "[Modified content not available]";

                    // Add diff string to text content if it's still empty - helps when actual content can't be extracted
                    if ((string.IsNullOrWhiteSpace(oldText) || oldText.StartsWith("[")) && 
                        (string.IsNullOrWhiteSpace(newText) || newText.StartsWith("[")) && 
                        !string.IsNullOrEmpty(fileDiff.Diff))
                    {
                        // Add the raw diff to both sides to ensure something is displayed
                        string diffHeader = "Showing raw diff content:\n\n";
                        oldText = "[Original content]\n" + diffHeader + fileDiff.Diff;
                        newText = "[Modified content]\n" + diffHeader + fileDiff.Diff;
                    }

                    // Ensure the content has multiple lines for proper display
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
                    // If we have no content at all, provide a meaningful message
                    _logger.LogWarning("  WARNING: No content available for {Path}", fileDiff.FilePath);
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        "No diff content available.",
                        "[No original content available]\n \n ",  // Add extra lines for proper display
                        "[No new content available]\n \n "     // Add extra lines for proper display
                    );
                }
                // Ensure we always have at least minimal content
                else if (string.IsNullOrEmpty(fileDiff.OldText) && !string.IsNullOrEmpty(fileDiff.NewText))
                {
                    // This is likely a new file
                    _logger.LogDebug("  New file detected: {Path}", fileDiff.FilePath);

                    // For new files, we'll preserve the empty old text but add a marker
                    // so that the diff viewer can properly recognize it as a new file
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        fileDiff.Diff,
                        "[FILE ADDED]\n", // Marker for new files
                        fileDiff.NewText
                    );
                }
                else if (!string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText))
                {
                    // This is likely a deleted file
                    _logger.LogDebug("  Deleted file detected: {Path}", fileDiff.FilePath);

                    // For deleted files, we'll preserve the empty new text but add a marker
                    // so that the diff viewer can properly recognize it as a deleted file
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        fileDiff.Diff,
                        fileDiff.OldText,
                        "[FILE DELETED]\n" // Marker for deleted files
                    );
                }

                _logger.LogDebug("  Final OldText length: {Length}", fileDiff.OldText?.Length ?? 0);
                _logger.LogDebug("  Final NewText length: {Length}", fileDiff.NewText?.Length ?? 0);
                FileDiffs.Add(fileDiff);

                // Ensure UI updates
                Dispatcher.UIThread.Post(() => {}, DispatcherPriority.Background);
            }
        }

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
