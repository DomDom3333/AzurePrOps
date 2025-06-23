using System;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using AzurePrOps.Views;

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

    private async Task ShowErrorMessage(string message)
    {
        // Create and show an error window with the message
        var errorViewModel = new ErrorWindowViewModel
        {
            ErrorMessage = message
        };

        // Use the appropriate way to show the error window based on your application architecture
        // This is a simple implementation, you might need to adjust based on your UI framework
        await Dispatcher.UIThread.InvokeAsync(() => 
        {
            var errorWindow = new ErrorWindow
            {
                DataContext = errorViewModel
            };
            errorWindow.Show();
        });
    }

    public MainWindowViewModel(Models.ConnectionSettings settings)
    {
        _settings = settings;

        // Add error handling mechanism
        _client.SetErrorHandler((message) => ShowErrorMessage(message).GetAwaiter().GetResult());

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
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
            }
            catch (Exception ex)
            {
                // Handle the error appropriately
                // Show the error message in a way that's compatible with the application UI
                await ShowErrorMessage($"Failed to refresh pull requests: {ex.Message}");
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
