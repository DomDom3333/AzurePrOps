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

namespace AzurePrOps.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly string _personalAccessToken;
    private readonly string _reviewerId;
    private readonly ConnectionSettings _initialSettings;

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

    public ReactiveCommand<Unit, ConnectionSettings> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

    private bool _useGitDiff;
    public bool UseGitDiff
    {
        get => _useGitDiff;
        set => this.RaiseAndSetIfChanged(ref _useGitDiff, value);
    }

    private bool _inlineCommentsEnabled = FeatureFlagManager.InlineCommentsEnabled;
    public bool InlineCommentsEnabled
    {
        get => _inlineCommentsEnabled;
        set => this.RaiseAndSetIfChanged(ref _inlineCommentsEnabled, value);
    }

    public ObservableCollection<string> Editors { get; } = new();

    private string _selectedEditor = string.Empty;
    public string SelectedEditor
    {
        get => _selectedEditor;
        set => this.RaiseAndSetIfChanged(ref _selectedEditor, value);
    }

    public SettingsWindowViewModel(ConnectionSettings currentSettings)
    {
        _initialSettings = currentSettings;
        _personalAccessToken = currentSettings.PersonalAccessToken;
        _reviewerId = currentSettings.ReviewerId;
        _useGitDiff = currentSettings.UseGitDiff;
        foreach (var e in EditorDetector.GetAvailableEditors())
            Editors.Add(e);
        _selectedEditor = string.IsNullOrWhiteSpace(currentSettings.EditorCommand)
            ? EditorDetector.GetDefaultEditor()
            : currentSettings.EditorCommand;

        SaveCommand = ReactiveCommand.Create(() =>
        {
            var settings = new ConnectionSettings(
                SelectedOrganization?.Name ?? string.Empty,
                SelectedProject?.Name ?? string.Empty,
                SelectedRepository?.Id ?? string.Empty,
                _personalAccessToken,
                _reviewerId,
                SelectedEditor,
                UseGitDiff);
            ConnectionSettingsStorage.Save(settings);
            FeatureFlagManager.InlineCommentsEnabled = InlineCommentsEnabled;
            ConnectionSettings = settings;

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new Views.MainWindow
                {
                    DataContext = new MainWindowViewModel(settings)
                };
                var oldWindow = desktop.MainWindow;
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                oldWindow?.Close();
            }

            return settings;
        });

        LogoutCommand = ReactiveCommand.Create(() =>
        {
            ConnectionSettingsStorage.Delete();
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var loginWindow = new Views.LoginWindow
                {
                    DataContext = new LoginWindowViewModel(loginInfo =>
                        ConnectionSettingsStorage.TryLoad(out var settings) ? new MainWindowViewModel(settings!) : null)
                };
                var oldWindow = desktop.MainWindow;
                desktop.MainWindow = loginWindow;
                loginWindow.Show();
                oldWindow?.Close();
            }
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
            SelectedOrganization = Organizations.FirstOrDefault(o => o.Name == _initialSettings.Organization) ??
                                  Organizations.FirstOrDefault();

            if (Organizations.Count == 0)
            {
                ErrorMessage = "No organizations found. Please check your Azure DevOps account and permissions.";
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = "Unable to connect to Azure DevOps: Invalid credentials. Please check your Personal Access Token.";
            throw new InvalidOperationException(ErrorMessage, ex);
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Unable to connect to Azure DevOps: {ex.Message}";
            throw new InvalidOperationException(ErrorMessage, ex);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            throw new InvalidOperationException(ErrorMessage, ex);
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
            SelectedProject = Projects.FirstOrDefault(p => p.Name == _initialSettings.Project) ??
                              Projects.FirstOrDefault();
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
            SelectedRepository = Repositories.FirstOrDefault(r => r.Id == _initialSettings.Repository) ??
                                Repositories.FirstOrDefault();
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
