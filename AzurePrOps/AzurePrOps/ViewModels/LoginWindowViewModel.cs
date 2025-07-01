using ReactiveUI;
using System.Reactive;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;
using System;
using System.Reactive.Linq;
using System.Net.Http;
using System.Reactive.Disposables;

namespace AzurePrOps.ViewModels;

public class LoginWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly Func<LoginInfo, ViewModelBase?>? _navigateToMain;

    private ReactiveCommand<Unit, LoginInfo>? _loginCommand;
    public ReactiveCommand<Unit, LoginInfo> LoginCommand => _loginCommand ??= CreateLoginCommand();

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

    private void InitializeViewModel()
    {
        if (ConnectionSettingsStorage.TryLoad(out var loaded))
        {
            PersonalAccessToken = loaded!.PersonalAccessToken;
        }
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

                // Store successful PAT for future use
                await ConnectionSettingsStorage.SaveAsync(new ConnectionSettings(
                    Organization: "", // Default empty values
                    Project: "",
                    Repository: "",
                    PersonalAccessToken: PersonalAccessToken,
                    ReviewerId: reviewerId,
                    UseGitDiff: false));

                var loginInfo = new LoginInfo(reviewerId, PersonalAccessToken);

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