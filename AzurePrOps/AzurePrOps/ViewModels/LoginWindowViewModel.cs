using ReactiveUI;
using System.Reactive;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;
using System;
using System.Reactive.Linq;
using System.Net.Http;

namespace AzurePrOps.ViewModels;

public class LoginWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();

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

    public ReactiveCommand<Unit, LoginInfo> LoginCommand { get; }

    public LoginWindowViewModel()
    {
        if (ConnectionSettingsStorage.TryLoad(out var loaded))
        {
            PersonalAccessToken = loaded!.PersonalAccessToken;
        }

        LoginCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                ErrorMessage = string.Empty; // Clear previous errors
                var reviewerId = await _client.GetUserIdAsync(PersonalAccessToken);
                return new LoginInfo(reviewerId, PersonalAccessToken);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                ErrorMessage = "Authentication failed. Please check your Personal Access Token.";
                throw; // Re-throw to maintain the reactive pipeline behavior
            }
            catch (HttpRequestException ex)
            {
                ErrorMessage = $"Network error: {ex.Message}";
                throw;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"An unexpected error occurred: {ex.Message}";
                throw;
            }
        });

        // Subscribe to ThrownExceptions to handle errors gracefully
        LoginCommand.ThrownExceptions.Subscribe(ex =>
        {
            // Error is already handled above by setting ErrorMessage
            // This subscription prevents the unhandled exception
        });
    }
}