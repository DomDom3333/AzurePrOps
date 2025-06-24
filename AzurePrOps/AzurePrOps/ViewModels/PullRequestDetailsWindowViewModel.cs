using System;
using System.Diagnostics;
using System.Reactive;
using Avalonia.Threading;
using ReactiveUI;
using AzurePrOps.AzureConnection.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AzurePrOps.ViewModels;

public class PullRequestDetailsWindowViewModel : ViewModelBase
{
    public PullRequestInfo PullRequest { get; }

    public ObservableCollection<PullRequestComment> Comments { get; }

    public ReactiveCommand<Unit, Unit> OpenInBrowserCommand { get; }

    public ObservableCollection<FileDiff> FileDiffs { get; } = new();

    public PullRequestDetailsWindowViewModel(PullRequestInfo pullRequest,
        IEnumerable<PullRequestComment>? comments = null,
        IEnumerable<FileDiff>? diffs = null)
    {
        PullRequest = pullRequest;
        Comments = comments != null
            ? new ObservableCollection<PullRequestComment>(comments)
            : new ObservableCollection<PullRequestComment>();
        if (diffs != null)
        {
            foreach (var d in diffs)
                FileDiffs.Add(d);
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
