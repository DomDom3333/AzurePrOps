using System;
using Avalonia.Interactivity;
using Avalonia.Controls;
using AzurePrOps.ViewModels;
using AzurePrOps.AzureConnection.Models;

namespace AzurePrOps.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RefreshCommand.Execute().Subscribe();
        }
    }

    private void PullRequests_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (sender is ListBox listBox && listBox.SelectedItem is PullRequestInfo pr)
        {
            vm.SelectedPullRequest = pr;
        }

        vm.ViewDetailsCommand.Execute().Subscribe();
    }
}