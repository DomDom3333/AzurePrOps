using System;
using System.Diagnostics;
using System.Reactive;
using Avalonia.Threading;
using ReactiveUI;
using AzurePrOps.AzureConnection.Models;

namespace AzurePrOps.ViewModels;

public class PullRequestDetailsWindowViewModel : ViewModelBase
{
    public PullRequestInfo PullRequest { get; }

    public ReactiveCommand<Unit, Unit> OpenInBrowserCommand { get; }

    public PullRequestDetailsWindowViewModel(PullRequestInfo pullRequest)
    {
        PullRequest = pullRequest;
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
