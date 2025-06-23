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

    public PullRequestDetailsWindowViewModel(PullRequestInfo pullRequest, IEnumerable<PullRequestComment>? comments = null)
    {
        PullRequest = pullRequest;
        Comments = comments != null
            ? new ObservableCollection<PullRequestComment>(comments)
            : new ObservableCollection<PullRequestComment>();

        OpenInBrowserCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrWhiteSpace(PullRequest.Url))
                return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PullRequest.Url,
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
