using ReactiveUI;
using System.Reactive;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;
using System;
using System.Reactive.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Diagnostics;

namespace AzurePrOps.ViewModels;

public class LoginWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly Func<LoginInfo, ViewModelBase?>? _navigateToMain;

    private ReactiveCommand<Unit, LoginInfo>? _loginCommand;
    public ReactiveCommand<Unit, LoginInfo> LoginCommand => _loginCommand ??= CreateLoginCommand();

    private ReactiveCommand<Unit, Unit>? _openPatHelpCommand;
    public ReactiveCommand<Unit, Unit> OpenPatHelpCommand => _openPatHelpCommand ??= ReactiveCommand.Create(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate") { UseShellExecute = true });
        }
        catch { /* Ignore errors opening browser */ }
    });

    private ReactiveCommand<Unit, Unit>? _openAzureDevOpsCommand;
    public ReactiveCommand<Unit, Unit> OpenAzureDevOpsCommand => _openAzureDevOpsCommand ??= ReactiveCommand.Create(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://dev.azure.com") { UseShellExecute = true });
        }
        catch { /* Ignore errors opening browser */ }
    });

    private ReactiveCommand<Unit, Unit>? _togglePatVisibilityCommand;
    public ReactiveCommand<Unit, Unit> TogglePatVisibilityCommand => _togglePatVisibilityCommand ??= ReactiveCommand.Create(() =>
    {
        IsPatVisible = !IsPatVisible;
    });

    private ReactiveCommand<Unit, Unit>? _validatePatCommand;
    public ReactiveCommand<Unit, Unit> ValidatePatCommand => _validatePatCommand ??= CreateValidatePatCommand();

    private ReactiveCommand<Unit, Unit>? _loadSavedCredentialsCommand;
    public ReactiveCommand<Unit, Unit> LoadSavedCredentialsCommand => _loadSavedCredentialsCommand ??= ReactiveCommand.Create(() =>
    {
        LoadSavedCredentials();
    });

    private ReactiveCommand<Unit, Unit>? _clearCredentialsCommand;
    public ReactiveCommand<Unit, Unit> ClearCredentialsCommand => _clearCredentialsCommand ??= ReactiveCommand.Create(() =>
    {
        ClearAllCredentials();
    });

    public LoginWindowViewModel(Func<LoginInfo, ViewModelBase?>? navigateToMain = null)
    {
        _navigateToMain = navigateToMain;

        InitializeViewModel();
    }

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    private string _personalAccessToken = string.Empty;
    public string PersonalAccessToken
    {
        get => _personalAccessToken;
        set => this.RaiseAndSetIfChanged(ref _personalAccessToken, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _isLoading = false;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string _loadingMessage = string.Empty;
    public string LoadingMessage
    {
        get => _loadingMessage;
        set => this.RaiseAndSetIfChanged(ref _loadingMessage, value);
    }

    private bool _isValidated = false;
    public bool IsValidated
    {
        get => _isValidated;
        set => this.RaiseAndSetIfChanged(ref _isValidated, value);
    }

    private bool _rememberCredentials = true;
    public bool RememberCredentials
    {
        get => _rememberCredentials;
        set => this.RaiseAndSetIfChanged(ref _rememberCredentials, value);
    }

    private bool _isPatVisible = false;
    public bool IsPatVisible
    {
        get => _isPatVisible;
        set => this.RaiseAndSetIfChanged(ref _isPatVisible, value);
    }

    public string PatVisibilityToggleText => IsPatVisible ? "🙈 Hide" : "👁️ Show";

    public bool CanLogin => !string.IsNullOrWhiteSpace(PersonalAccessToken) && !IsLoading;

    private void InitializeViewModel()
    {
        // Check if we already have a secure token stored
        var existingToken = ConnectionSettingsStorage.GetPersonalAccessToken();
        if (!string.IsNullOrEmpty(existingToken))
        {
            PersonalAccessToken = existingToken;
            IsValidated = true;
        }

        // Set up property change notifications
        this.WhenAnyValue(x => x.IsPatVisible)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(PatVisibilityToggleText)));

        this.WhenAnyValue(x => x.PersonalAccessToken, x => x.IsLoading)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CanLogin)));
    }

    private void LoadSavedCredentials()
    {
        var existingToken = ConnectionSettingsStorage.GetPersonalAccessToken();
        if (!string.IsNullOrEmpty(existingToken))
        {
            PersonalAccessToken = existingToken;
            IsValidated = true;
            ErrorMessage = string.Empty;
        }
    }

    private void ClearAllCredentials()
    {
        PersonalAccessToken = string.Empty;
        Email = string.Empty;
        ErrorMessage = string.Empty;
        IsValidated = false;
        
        // Clear stored credentials
        ConnectionSettingsStorage.ClearSecureCredentials();
    }

    private ReactiveCommand<Unit, Unit> CreateValidatePatCommand()
    {
        return ReactiveCommand.CreateFromTask(async () =>
        {
            if (string.IsNullOrWhiteSpace(PersonalAccessToken))
            {
                ErrorMessage = "Please enter a Personal Access Token";
                return;
            }

            try
            {
                IsLoading = true;
                LoadingMessage = "Validating Personal Access Token...";
                ErrorMessage = string.Empty;

                var reviewerId = await _client.GetUserIdAsync(PersonalAccessToken);
                if (!string.IsNullOrEmpty(reviewerId))
                {
                    IsValidated = true;
                    ErrorMessage = string.Empty;
                }
                else
                {
                    IsValidated = false;
                    ErrorMessage = "Token validation failed. Please check your PAT and permissions.";
                }
            }
            catch (UnauthorizedAccessException)
            {
                IsValidated = false;
                ErrorMessage = "Access denied. Please verify your PAT has the required permissions.";
            }
            catch (Exception ex)
            {
                IsValidated = false;
                ErrorMessage = $"Validation failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        });
    }

    private ReactiveCommand<Unit, LoginInfo> CreateLoginCommand()
    {
        var command = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                ErrorMessage = string.Empty; // Clear previous errors

                // Add validation for PAT format
                if (string.IsNullOrWhiteSpace(PersonalAccessToken))
                {
                    ErrorMessage = "Personal Access Token cannot be empty";
                    throw new ArgumentException(ErrorMessage);
                }

                var reviewerId = await _client.GetUserIdAsync(PersonalAccessToken);
                if (string.IsNullOrEmpty(reviewerId))
                {
                    ErrorMessage = "Failed to retrieve user ID. The PAT might not have sufficient permissions.";
                    throw new InvalidOperationException(ErrorMessage);
                }

                // Store the PAT token securely
                if (!ConnectionSettingsStorage.SavePersonalAccessToken(PersonalAccessToken, reviewerId))
                {
                    ErrorMessage = "Failed to securely store the Personal Access Token";
                    throw new InvalidOperationException(ErrorMessage);
                }

                // Store connection settings without the PAT token
                await ConnectionSettingsStorage.SaveAsync(new ConnectionSettings(
                    Organization: "", // Default empty values
                    Project: "",
                    Repository: "",
                    ReviewerId: reviewerId,
                    EditorCommand: EditorDetector.GetDefaultEditor(),
                    UseGitDiff: true)
                {
                    HasSecureToken = true
                });

                var loginInfo = new LoginInfo(reviewerId);

                // Show project selection window first
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var projectSelectionViewModel = new ProjectSelectionWindowViewModel(PersonalAccessToken, reviewerId);
                    var projectSelectionWindow = new Views.ProjectSelectionWindow
                    {
                        DataContext = projectSelectionViewModel
                    };

                    // Load organizations asynchronously
                    _ = projectSelectionViewModel.LoadAsync();

                    // Handle the connection command result
                    projectSelectionWindow.Closed += (_, _) =>
                    {
                        if (projectSelectionViewModel.ConnectionSettings != null && _navigateToMain != null)
                        {
                            var mainViewModel = _navigateToMain(loginInfo);
                            if (mainViewModel != null)
                            {
                                var mainWindow = new Views.MainWindow
                                {
                                    DataContext = mainViewModel
                                };

                                desktop.MainWindow = mainWindow;
                                mainWindow.Show();
                            }
                        }
                    };

                    var oldWindow = desktop.MainWindow;
                    desktop.MainWindow = projectSelectionWindow;
                    projectSelectionWindow.Show();
                    oldWindow?.Close();
                }

                return loginInfo;
            }
            catch (UnauthorizedAccessException ex)
            {
                ErrorMessage = ex.Message + "\n\nPlease verify that:\n" +
                              "1. Your PAT has 'Code (Read)' scope at minimum\n" +
                              "2. Your PAT is not expired\n" +
                              "3. Your PAT was created for the correct organization";
                throw;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                ErrorMessage = "Authentication failed. Please check your Personal Access Token and ensure it has the required permissions.";
                throw;
            }
            catch (HttpRequestException ex)
            {
                ErrorMessage = $"Network error: {ex.Message}\n\nPlease check your internet connection or if Azure DevOps is experiencing an outage.";
                throw;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"An unexpected error occurred: {ex.Message}";
                throw;
            }
        });

        // Subscribe to ThrownExceptions to handle errors gracefully
        command.ThrownExceptions.Subscribe(ex =>
        {
            // Error is already handled above by setting ErrorMessage
            // This subscription prevents the unhandled exception
        });

        return command;
    }
}