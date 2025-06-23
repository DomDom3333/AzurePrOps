using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();

    public ObservableCollection<PullRequestInfo> PullRequests { get; } = new();
    public ObservableCollection<PullRequestComment> Comments { get; } = new();

    private string _newCommentText = string.Empty;
    public string NewCommentText
    {
        get => _newCommentText;
        set => this.RaiseAndSetIfChanged(ref _newCommentText, value);
    }

    private PullRequestInfo? _selectedPullRequest;
    public PullRequestInfo? SelectedPullRequest
    {
        get => _selectedPullRequest;
        set => this.RaiseAndSetIfChanged(ref _selectedPullRequest, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadCommentsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> PostCommentCommand { get; }

    public MainWindowViewModel()
    {
        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // TODO: replace placeholders with real values or configuration
            const string org = "your-org";
            const string project = "your-project";
            const string repo = "your-repo";
            const string pat = "your-pat";

            var prs = await _client.GetPullRequestsAsync(org, project, repo, pat);

            PullRequests.Clear();
            foreach (var pr in prs.OrderByDescending(p => p.Created))
            {
                PullRequests.Add(pr);
            }
        });

        LoadCommentsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            const string org = "your-org";
            const string project = "your-project";
            const string repo = "your-repo";
            const string pat = "your-pat";

            var comments = await _client.GetPullRequestCommentsAsync(org, project, repo, SelectedPullRequest.Id, pat);

            Comments.Clear();
            foreach (var c in comments)
            {
                Comments.Add(c);
            }
        });

        ApproveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            const string org = "your-org";
            const string project = "your-project";
            const string repo = "your-repo";
            const string pat = "your-pat";
            const string reviewerId = "your-reviewer-id";

            await _client.ApprovePullRequestAsync(org, project, repo, SelectedPullRequest.Id, reviewerId, pat);
        });

        PostCommentCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null || string.IsNullOrWhiteSpace(NewCommentText))
                return;

            const string org = "your-org";
            const string project = "your-project";
            const string repo = "your-repo";
            const string pat = "your-pat";

            await _client.PostPullRequestCommentAsync(org, project, repo, SelectedPullRequest.Id, NewCommentText, pat);
            NewCommentText = string.Empty;
        });
    }
}
