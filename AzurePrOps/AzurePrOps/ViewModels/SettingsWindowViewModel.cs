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
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Reactive.Linq;
using System.IO;
using System.Collections.Generic;

namespace AzurePrOps.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly string _personalAccessToken;
    private readonly string _reviewerId;
    private readonly ConnectionSettings _initialSettings;
    
    public Views.SettingsWindow? DialogWindow { get; set; }

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

    private bool _lifecycleActionsEnabled = FeatureFlagManager.LifecycleActionsEnabled;
    public bool LifecycleActionsEnabled
    {
        get => _lifecycleActionsEnabled;
        set => this.RaiseAndSetIfChanged(ref _lifecycleActionsEnabled, value);
    }

    public bool ExpandAllDiffsOnOpen
    {
        get => DiffPreferences.ExpandAllOnOpen;
        set
        {
            if (DiffPreferences.ExpandAllOnOpen != value)
            {
                DiffPreferences.ExpandAllOnOpen = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Additional diff preferences properties
    public bool IgnoreWhitespace
    {
        get => DiffPreferences.IgnoreWhitespace;
        set
        {
            if (DiffPreferences.IgnoreWhitespace != value)
            {
                DiffPreferences.IgnoreWhitespace = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public bool WrapLines
    {
        get => DiffPreferences.WrapLines;
        set
        {
            if (DiffPreferences.WrapLines != value)
            {
                DiffPreferences.WrapLines = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public bool IgnoreNewlines
    {
        get => DiffPreferences.IgnoreNewlines;
        set
        {
            if (DiffPreferences.IgnoreNewlines != value)
            {
                DiffPreferences.IgnoreNewlines = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Editor options
    public ObservableCollection<EditorOption> EditorOptions { get; } = new();

    private EditorOption? _selectedEditorOption;
    public EditorOption? SelectedEditorOption
    {
        get => _selectedEditorOption;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEditorOption, value);
            this.RaisePropertyChanged(nameof(ShowCustomEditorPath));
        }
    }

    public bool ShowCustomEditorPath => _selectedEditorOption?.Command == "custom";

    private string _customEditorPath = string.Empty;
    public string CustomEditorPath
    {
        get => _customEditorPath;
        set => this.RaiseAndSetIfChanged(ref _customEditorPath, value);
    }

    // Interface settings
    public bool AutoRefreshEnabled
    {
        get => UIPreferences.AutoRefreshEnabled;
        set
        {
            if (UIPreferences.AutoRefreshEnabled != value)
            {
                UIPreferences.AutoRefreshEnabled = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public int SelectedThemeIndex
    {
        get => UIPreferences.SelectedThemeIndex;
        set
        {
            if (UIPreferences.SelectedThemeIndex != value)
            {
                UIPreferences.SelectedThemeIndex = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public int RefreshIntervalSeconds
    {
        get => UIPreferences.RefreshIntervalSeconds;
        set
        {
            if (UIPreferences.RefreshIntervalSeconds != value)
            {
                UIPreferences.RefreshIntervalSeconds = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public bool ShowNotifications
    {
        get => UIPreferences.ShowNotifications;
        set
        {
            if (UIPreferences.ShowNotifications != value)
            {
                UIPreferences.ShowNotifications = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public bool MinimizeToTray
    {
        get => UIPreferences.MinimizeToTray;
        set
        {
            if (UIPreferences.MinimizeToTray != value)
            {
                UIPreferences.MinimizeToTray = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Legacy editor properties for backward compatibility
    public ObservableCollection<string> Editors { get; } = new();

    private string _selectedEditor = string.Empty;
    public string SelectedEditor
    {
        get => _selectedEditor;
        set => this.RaiseAndSetIfChanged(ref _selectedEditor, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> BrowseEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public SettingsWindowViewModel(ConnectionSettings currentSettings)
    {
        _initialSettings = currentSettings;
        _personalAccessToken = currentSettings.PersonalAccessToken;
        _reviewerId = currentSettings.ReviewerId;
        _useGitDiff = currentSettings.UseGitDiff;
        
        // Initialize legacy editor list for backward compatibility
        foreach (var e in EditorDetector.GetAvailableEditors())
            Editors.Add(e);
        _selectedEditor = string.IsNullOrWhiteSpace(currentSettings.EditorCommand)
            ? EditorDetector.GetDefaultEditor()
            : currentSettings.EditorCommand;

        // Initialize editor options with user-friendly names
        InitializeEditorOptions();

        // Initialize commands
        BrowseEditorCommand = ReactiveCommand.Create(BrowseForEditor);
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        CancelCommand = ReactiveCommand.Create(() =>
        {
            DialogWindow?.Close();
        });

        SaveCommand = ReactiveCommand.Create(() =>
        {
            // Determine the editor command to save
            var editorCommand = SelectedEditor; // Default fallback
            if (_selectedEditorOption != null)
            {
                if (_selectedEditorOption.Command == "custom")
                {
                    editorCommand = !string.IsNullOrWhiteSpace(_customEditorPath) ? _customEditorPath : SelectedEditor;
                }
                else
                {
                    editorCommand = _selectedEditorOption.Command;
                }
            }

            var settings = new ConnectionSettings(
                SelectedOrganization?.Name ?? string.Empty,
                SelectedProject?.Name ?? string.Empty,
                SelectedRepository?.Id ?? string.Empty,
                _personalAccessToken,
                _reviewerId,
                editorCommand,
                UseGitDiff);
            ConnectionSettingsStorage.Save(settings);
            FeatureFlagManager.InlineCommentsEnabled = InlineCommentsEnabled;
            FeatureFlagManager.LifecycleActionsEnabled = LifecycleActionsEnabled;
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

    private void InitializeEditorOptions()
    {
        // Add available editors with user-friendly names
        var editorMappings = new Dictionary<string, string>
        {
            { "code", "Visual Studio Code" },
            { "code-insiders", "VS Code Insiders" },
            { "rider", "JetBrains Rider" },
            { "rider64", "JetBrains Rider (64-bit)" },
            { "subl", "Sublime Text" },
            { "notepad++", "Notepad++" },
            { "notepad", "Notepad" },
            { "devenv", "Visual Studio" },
            { "idea", "IntelliJ IDEA" },
            { "idea64", "IntelliJ IDEA (64-bit)" },
            { "studio", "Android Studio" },
            { "studio64", "Android Studio (64-bit)" },
            { "gedit", "GEdit" },
            { "vim", "Vim" },
            { "vi", "Vi" },
            { "nano", "Nano" },
            { "emacs", "Emacs" }
        };

        foreach (var editor in EditorDetector.GetAvailableEditors())
        {
            var displayName = editorMappings.TryGetValue(editor, out var name) ? name : editor;
            EditorOptions.Add(new EditorOption(displayName, editor));
        }

        // Add custom option
        EditorOptions.Add(new EditorOption("Custom...", "custom"));

        // Set selected editor
        var currentEditor = _selectedEditor;
        _selectedEditorOption = EditorOptions.FirstOrDefault(e => e.Command == currentEditor) ??
                               EditorOptions.FirstOrDefault();
        this.RaisePropertyChanged(nameof(SelectedEditorOption));
        this.RaisePropertyChanged(nameof(ShowCustomEditorPath));
    }

    private async void BrowseForEditor()
    {
        try
        {
            var storageProvider = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.StorageProvider
                : null;

            if (storageProvider == null) return;

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Editor Executable",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Executable Files")
                    {
                        Patterns = OperatingSystem.IsWindows() 
                            ? new[] { "*.exe", "*.bat", "*.cmd" }
                            : new[] { "*" }
                    },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count > 0)
            {
                CustomEditorPath = files[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            // Handle error silently or show notification
            System.Diagnostics.Debug.WriteLine($"Error browsing for editor: {ex.Message}");
        }
    }

    private void ResetToDefaults()
    {
        // Reset diff preferences
        DiffPreferences.IgnoreWhitespace = false;
        DiffPreferences.WrapLines = false;
        DiffPreferences.IgnoreNewlines = false;
        DiffPreferences.ExpandAllOnOpen = true;

        // Reset interface settings
        UIPreferences.AutoRefreshEnabled = true;
        UIPreferences.SelectedThemeIndex = 0; // System theme
        UIPreferences.RefreshIntervalSeconds = 60;
        UIPreferences.ShowNotifications = true;
        UIPreferences.MinimizeToTray = false;

        // Reset editor to default
        var defaultEditor = EditorDetector.GetDefaultEditor();
        _selectedEditorOption = EditorOptions.FirstOrDefault(e => e.Command == defaultEditor) ??
                               EditorOptions.FirstOrDefault();
        this.RaisePropertyChanged(nameof(SelectedEditorOption));
        this.RaisePropertyChanged(nameof(ShowCustomEditorPath));

        CustomEditorPath = string.Empty;

        // Raise property changed for all diff preferences
        this.RaisePropertyChanged(nameof(IgnoreWhitespace));
        this.RaisePropertyChanged(nameof(WrapLines));
        this.RaisePropertyChanged(nameof(IgnoreNewlines));
        this.RaisePropertyChanged(nameof(ExpandAllDiffsOnOpen));
    }
}
