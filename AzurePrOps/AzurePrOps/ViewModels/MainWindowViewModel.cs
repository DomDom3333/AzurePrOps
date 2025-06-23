using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly Models.ConnectionSettings _settings;

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

    public MainWindowViewModel(Models.ConnectionSettings settings)
    {
        _settings = settings;

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var prs = await _client.GetPullRequestsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                _settings.PersonalAccessToken);

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

            var comments = await _client.GetPullRequestCommentsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.PersonalAccessToken);

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

            await _client.ApprovePullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                _settings.PersonalAccessToken);
        });

        PostCommentCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null || string.IsNullOrWhiteSpace(NewCommentText))
                return;

            await _client.PostPullRequestCommentAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                NewCommentText,
                _settings.PersonalAccessToken);
            NewCommentText = string.Empty;
        });
    }
}
