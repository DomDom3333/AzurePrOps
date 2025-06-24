using System;
using Avalonia.Controls;

using Avalonia.Controls.Templates;
using AzurePrOps.Controls;
using AzurePrOps.ViewModels;

namespace AzurePrOps.Views;

public partial class PullRequestDetailsWindow : Window
{
    public PullRequestDetailsWindow()
    {
        InitializeComponent();

        // Add logging after window is loaded to debug data binding
        this.Loaded += (s, e) =>
        {
            if (DataContext is PullRequestDetailsWindowViewModel viewModel)
            {
                foreach (var diff in viewModel.FileDiffs)
                {
                    Console.WriteLine($"FileDiff: {diff.FilePath}, OldText Length: {diff.OldText?.Length ?? 0}, NewText Length: {diff.NewText?.Length ?? 0}");
                }
            }
        };
    }
}
