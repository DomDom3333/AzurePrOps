using System;
using System.Diagnostics;
using System.Reactive;
using Avalonia.Threading;
using ReactiveUI;
using ConnectionModels = AzurePrOps.AzureConnection.Models;
using ReviewModels = AzurePrOps.ReviewLogic.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AzurePrOps.ViewModels;

public class PullRequestDetailsWindowViewModel : ViewModelBase
{
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
            foreach (var d in diffs)
            {
                // Ensure there's at least some content for the diff viewer to display
                var fileDiff = d;

                // If both texts are empty but we have a diff string, create placeholder content
                if (string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText) && !string.IsNullOrEmpty(fileDiff.Diff))
                {
                    // Create a simple representation of the diff as text
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        fileDiff.Diff,
                        "[Original content not available]",
                        fileDiff.Diff // Use the diff as the new text to at least show something
                    );
                }

                FileDiffs.Add(fileDiff);
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
                    Console.WriteLine($"Invalid URL format: {sanitizedUrl}");
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
                Console.WriteLine($"Failed to open browser: {ex.Message}");
            }
        });
    }
}
