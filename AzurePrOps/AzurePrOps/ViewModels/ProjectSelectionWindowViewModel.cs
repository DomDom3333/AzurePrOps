using System;
using ReactiveUI;
using System.Collections.ObjectModel;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using System.Reactive;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.ViewModels;

public class ProjectSelectionWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<ProjectSelectionWindowViewModel>();
    private readonly AzureDevOpsClient _client = new();
    private readonly string _personalAccessToken;
    private readonly string _reviewerId;

    public ObservableCollection<NamedItem> Organizations { get; } = new();
    public ObservableCollection<NamedItem> Projects { get; } = new();
    public ObservableCollection<NamedItem> Repositories { get; } = new();

    private NamedItem? _selectedOrganization;
    public NamedItem? SelectedOrganization
    {
        get => _selectedOrganization;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedOrganization, value);
            _ = LoadProjectsAsync();
        }
    }

    private NamedItem? _selectedProject;
    public NamedItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedProject, value);
            _ = LoadRepositoriesAsync();
        }
    }

    private NamedItem? _selectedRepository;
    public NamedItem? SelectedRepository
    {
        get => _selectedRepository;
        set => this.RaiseAndSetIfChanged(ref _selectedRepository, value);
    }

    private ConnectionSettings? _connectionSettings;
    public ConnectionSettings? ConnectionSettings
    {
        get => _connectionSettings;
        private set => this.RaiseAndSetIfChanged(ref _connectionSettings, value);
    }

    public ReactiveCommand<Unit, ConnectionSettings> ConnectCommand { get; }

    public ProjectSelectionWindowViewModel(string personalAccessToken, string reviewerId)
    {
        _personalAccessToken = personalAccessToken;
        _reviewerId = reviewerId;

        ConnectCommand = ReactiveCommand.Create(() =>
        {
            var settings = new ConnectionSettings(
                SelectedOrganization?.Name ?? string.Empty,
                SelectedProject?.Name ?? string.Empty,
                SelectedRepository?.Id ?? string.Empty,
                _reviewerId,
                EditorDetector.GetDefaultEditor(),
                true); // UseGitDiff parameter in correct position
            ConnectionSettingsStorage.Save(settings);
            ConnectionSettings = settings;

            // Close the window
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Close();
            }

            return settings;
        });
    }

    private bool _isLoadingOrganizations;
    public bool IsLoadingOrganizations
    {
        get => _isLoadingOrganizations;
        set => this.RaiseAndSetIfChanged(ref _isLoadingOrganizations, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoadingOrganizations = true;
            ErrorMessage = string.Empty;

            Organizations.Clear();
            var userId = _reviewerId;
            var orgs = await _client.GetOrganizationsAsync(userId, _personalAccessToken);
            foreach (var o in orgs)
                Organizations.Add(o);
            SelectedOrganization = Organizations.FirstOrDefault();

            if (Organizations.Count == 0)
            {
                ErrorMessage = "No organizations found. Please check your Azure DevOps account and permissions.";
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = "Unable to connect to Azure DevOps: Invalid credentials. Please check your Personal Access Token.";
            _logger.LogWarning(ex, "Authentication failed in ProjectSelectionWindowViewModel");
            // Don't throw - let the UI handle the error message
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Unable to connect to Azure DevOps: {ex.Message}";
            _logger.LogWarning(ex, "HTTP request failed in ProjectSelectionWindowViewModel");
            // Don't throw - let the UI handle the error message
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            _logger.LogError(ex, "Unexpected error in ProjectSelectionWindowViewModel");
            // Don't throw - let the UI handle the error message
        }
        finally
        {
            IsLoadingOrganizations = false;
        }
    }

    private bool _isLoadingProjects;
    public bool IsLoadingProjects
    {
        get => _isLoadingProjects;
        set => this.RaiseAndSetIfChanged(ref _isLoadingProjects, value);
    }

    private bool _isLoadingRepositories;
    public bool IsLoadingRepositories
    {
        get => _isLoadingRepositories;
        set => this.RaiseAndSetIfChanged(ref _isLoadingRepositories, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private async Task LoadProjectsAsync()
    {
        try
        {
            IsLoadingProjects = true;
            ErrorMessage = string.Empty;

            Projects.Clear();
            Repositories.Clear();
            if (SelectedOrganization == null)
                return;

            var projects = await _client.GetProjectsAsync(SelectedOrganization.Name, _personalAccessToken);
            foreach (var p in projects)
                Projects.Add(p);
            SelectedProject = Projects.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load projects: {ex.Message}";
        }
        finally
        {
            IsLoadingProjects = false;
        }
    }

    private async Task LoadRepositoriesAsync()
    {
        try
        {
            IsLoadingRepositories = true;
            ErrorMessage = string.Empty;

            Repositories.Clear();
            if (SelectedOrganization == null || SelectedProject == null)
                return;

            var repos = await _client.GetRepositoriesAsync(SelectedOrganization.Name, SelectedProject.Name, _personalAccessToken);
            foreach (var r in repos)
                Repositories.Add(r);
            SelectedRepository = Repositories.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load repositories: {ex.Message}";
        }
        finally
        {
            IsLoadingRepositories = false;
        }
    }
}
