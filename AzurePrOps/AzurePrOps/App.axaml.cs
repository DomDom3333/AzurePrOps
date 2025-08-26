using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AzurePrOps.Models;
using ReactiveUI;
using AzurePrOps.ViewModels;
using AzurePrOps.Views;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using AzurePrOps.Infrastructure;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.ReviewLogic.Services;
using AzurePrOps.Services;

namespace AzurePrOps;

public partial class App : Application
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<App>();
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Set up ReactiveUI global error handler
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
        {
            // Log the exception
            _logger.LogError(ex, "Unhandled ReactiveUI exception");

            // Check if this is an authentication-related error
            var authService = ServiceRegistry.Resolve<AuthenticationService>();
            if (ex is UnauthorizedAccessException || 
                (ex is System.Net.Http.HttpRequestException httpEx && httpEx.Message.Contains("401")))
            {
                authService?.HandlePatValidationError(ex, "Global exception handler");
                return;
            }

            // Show error dialog for other exceptions
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var errorWindow = new ErrorWindow
                {
                    DataContext = new ErrorWindowViewModel
                    {
                        ErrorMessage = $"An error occurred: {ex.Message}\n\nPlease check your connection settings and try again."
                    }
                };

                if (desktop.MainWindow != null)
                    errorWindow.ShowDialog(desktop.MainWindow);
            }
        });

        // Load feature flags so they are available throughout the app
        FeatureFlagManager.Load();

        // Initialize theme management
        ThemeManager.Initialize();

        // Register shared services in the composition root
        var devOpsClient = new AzureDevOpsClient();
        var authService = new AuthenticationService();
        
        ServiceRegistry.Register<IAzureDevOpsClient>(devOpsClient);
        ServiceRegistry.Register<ICommentsService>(new CommentsService(devOpsClient));
        ServiceRegistry.Register<AuthenticationService>(authService);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Check if we have valid connection settings and a valid PAT
            if (ConnectionSettingsStorage.TryLoad(out var loaded) && loaded!.HasSecureToken)
            {
                // Validate PAT asynchronously and redirect if needed
                _ = Task.Run(async () =>
                {
                    var isValid = await authService.ValidateAndRedirectIfNeededAsync();
                    if (isValid)
                    {
                        // PAT is valid, proceed with main window
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (desktop.MainWindow is LoginWindow)
                            {
                                // If we're currently on login window, switch to main window
                                desktop.MainWindow = new MainWindow
                                {
                                    DataContext = new MainWindowViewModel(loaded!)
                                };
                                desktop.MainWindow.Show();
                            }
                        });
                    }
                });

                // Start with main window, but it will be replaced if PAT validation fails
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(loaded!)
                };
            }
            else
            {
                // No settings or no secure token, show login window
                desktop.MainWindow = new LoginWindow
                {
                    DataContext = new LoginWindowViewModel(loginInfo => 
                        // We'll use the connection settings saved by the project selection window
                        ConnectionSettingsStorage.TryLoad(out var settings) 
                            ? new MainWindowViewModel(settings!)
                            : null)
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}