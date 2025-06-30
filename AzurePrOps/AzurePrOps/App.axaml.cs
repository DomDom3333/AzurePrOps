using System;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AzurePrOps.Models;
using ReactiveUI;
using AzurePrOps.ViewModels;
using AzurePrOps.Views;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

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

            // Show error dialog
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var errorWindow = new ErrorWindow
                {
                    DataContext = new ErrorWindowViewModel
                    {
                        ErrorMessage = $"An error occurred: {ex.Message}\n\nPlease check your connection settings and try again."
                    }
                };

                errorWindow.ShowDialog(desktop.MainWindow);
            }
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (ConnectionSettingsStorage.TryLoad(out var loaded))
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(loaded)
                };
            }
            else
            {
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