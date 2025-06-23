using Avalonia;
using System;
using Avalonia.Controls.ApplicationLifetimes;
using System.Reactive.Linq;
using Avalonia.Markup.Xaml;
using AzurePrOps.ViewModels;
using AzurePrOps.Views;

namespace AzurePrOps;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginVm = new LoginWindowViewModel();
            var loginWindow = new LoginWindow { DataContext = loginVm };

            loginVm.ConnectCommand.Subscribe(settings =>
            {
                var mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(settings),
                };
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                loginWindow.Close();
            });

            desktop.MainWindow = loginWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}