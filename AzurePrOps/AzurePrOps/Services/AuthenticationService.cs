using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using AzurePrOps.ViewModels;
using AzurePrOps.Views;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.Services;

public class AuthenticationService
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<AuthenticationService>();
    private readonly AzureDevOpsClient _client = new();
    private static bool _isLoginWindowOpen = false;
    private static readonly object _loginLock = new object();

    /// <summary>
    /// Validates the current PAT and redirects to login if invalid or missing
    /// </summary>
    /// <returns>True if PAT is valid, false if redirected to login</returns>
    public async Task<bool> ValidateAndRedirectIfNeededAsync()
    {
        var pat = ConnectionSettingsStorage.GetPersonalAccessToken();
        
        if (string.IsNullOrEmpty(pat))
        {
            _logger.LogWarning("No Personal Access Token found, redirecting to login");
            RedirectToLogin();
            return false;
        }

        try
        {
            // Validate the PAT by attempting to get user information
            var userId = await _client.GetUserIdAsync(pat);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("PAT validation failed - invalid token, redirecting to login");
                RedirectToLogin();
                return false;
            }

            _logger.LogDebug("PAT validation successful");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "PAT authentication failed, redirecting to login");
            RedirectToLogin();
            return false;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("401"))
        {
            _logger.LogWarning(ex, "PAT authentication failed with 401, redirecting to login");
            RedirectToLogin();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during PAT validation, redirecting to login");
            RedirectToLogin();
            return false;
        }
    }

    /// <summary>
    /// Validates the current PAT synchronously
    /// </summary>
    /// <returns>True if PAT exists and appears valid, false otherwise</returns>
    public bool ValidatePatExists()
    {
        var pat = ConnectionSettingsStorage.GetPersonalAccessToken();
        return !string.IsNullOrEmpty(pat);
    }

    /// <summary>
    /// Redirects the user to the login screen
    /// </summary>
    public void RedirectToLogin()
    {
        lock (_loginLock)
        {
            // Check if login window is already open
            if (_isLoginWindowOpen)
            {
                _logger.LogInformation("Login window is already open, skipping redirect");
                return;
            }

            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    _logger.LogInformation("Redirecting to login screen");
                    _isLoginWindowOpen = true;
                    
                    // Ensure UI operations happen on the UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var loginWindow = new LoginWindow
                            {
                                DataContext = new LoginWindowViewModel(loginInfo =>
                                {
                                    // Reset the flag when login is successful
                                    _isLoginWindowOpen = false;
                                    // After successful login, try to reload with saved connection settings
                                    return ConnectionSettingsStorage.TryLoad(out var settings)
                                        ? new MainWindowViewModel(settings!)
                                        : null;
                                })
                            };

                            // Handle window closing to reset flag
                            loginWindow.Closed += (sender, args) =>
                            {
                                _isLoginWindowOpen = false;
                            };

                            var oldWindow = desktop.MainWindow;
                            desktop.MainWindow = loginWindow;
                            loginWindow.Show();
                            oldWindow?.Close();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error creating login window on UI thread");
                            _isLoginWindowOpen = false; // Reset flag on error
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error redirecting to login screen");
                _isLoginWindowOpen = false; // Reset flag on error
            }
        }
    }

    /// <summary>
    /// Handles PAT validation errors by showing appropriate error messages and redirecting to login
    /// </summary>
    /// <param name="exception">The exception that occurred during PAT validation</param>
    /// <param name="context">Additional context about where the error occurred</param>
    public void HandlePatValidationError(Exception exception, string context = "")
    {
        // Check if login window is already open before handling the error
        lock (_loginLock)
        {
            if (_isLoginWindowOpen)
            {
                _logger.LogInformation("Login window is already open, skipping PAT validation error handling");
                return;
            }
        }

        string errorMessage = exception switch
        {
            UnauthorizedAccessException => "Your Personal Access Token has expired or is invalid. Please log in again.",
            HttpRequestException httpEx when httpEx.Message.Contains("401") => "Authentication failed. Your Personal Access Token may have expired. Please log in again.",
            HttpRequestException httpEx when httpEx.Message.Contains("403") => "Access denied. Your Personal Access Token may not have sufficient permissions. Please log in again.",
            _ => "An authentication error occurred. Please log in again."
        };

        _logger.LogWarning(exception, "PAT validation error in context: {Context}", context);

        // Show error dialog if possible before redirecting
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            try
            {
                var errorWindow = new ErrorWindow
                {
                    DataContext = new ErrorWindowViewModel
                    {
                        ErrorMessage = errorMessage
                    }
                };

                errorWindow.ShowDialog(desktop.MainWindow).ContinueWith(_ => RedirectToLogin());
            }
            catch
            {
                // If showing error dialog fails, just redirect to login
                RedirectToLogin();
            }
        }
        else
        {
            RedirectToLogin();
        }
    }
}
