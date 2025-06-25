using System;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Templates;
using AzurePrOps.Controls;
using AzurePrOps.ViewModels;
using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.Views;

public partial class PullRequestDetailsWindow : Window
{
    public PullRequestDetailsWindow()
    {
        InitializeComponent();

        // Add enhanced logging after window is loaded to debug data binding
        this.Loaded += (s, e) =>
        {
            if (DataContext is PullRequestDetailsWindowViewModel viewModel)
            {
                Console.WriteLine($"PR Details Window loaded with {viewModel.FileDiffs.Count} file diffs");
                foreach (var diff in viewModel.FileDiffs)
                {
                    Console.WriteLine($"FileDiff: {diff.FilePath}");
                    Console.WriteLine($"  OldText Length: {diff.OldText?.Length ?? 0}");
                    Console.WriteLine($"  NewText Length: {diff.NewText?.Length ?? 0}");
                    Console.WriteLine($"  Diff Length: {diff.Diff?.Length ?? 0}");
                    Console.WriteLine($"  HasDiff: {!string.IsNullOrEmpty(diff.Diff)}");
                }
            }
            else
            {
                Console.WriteLine("PR Details Window loaded with NULL or invalid DataContext");
            }
        };
    }

    // The DiffViewer now updates itself when its DataContext changes,
    // so no additional initialization is required here.
}
