using System;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Templates;
using AzurePrOps.Controls;
using AzurePrOps.ViewModels;
using AzurePrOps.ReviewLogic.Models;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.Views;

public partial class PullRequestDetailsWindow : Window
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestDetailsWindow>();
    public PullRequestDetailsWindow()
    {
        InitializeComponent();

        // Add enhanced logging after window is loaded to debug data binding
        this.Loaded += (s, e) =>
        {
            if (DataContext is PullRequestDetailsWindowViewModel viewModel)
            {
                _logger.LogInformation("PR Details Window loaded with {Count} file diffs", viewModel.FileDiffs.Count);
                foreach (var diff in viewModel.FileDiffs)
                {
                    _logger.LogDebug("FileDiff: {Path} - OldText {OldLength} bytes, NewText {NewLength} bytes, Diff {DiffLength} bytes, HasDiff {HasDiff}",
                        diff.FilePath,
                        diff.OldText?.Length ?? 0,
                        diff.NewText?.Length ?? 0,
                        diff.Diff?.Length ?? 0,
                        !string.IsNullOrEmpty(diff.Diff));
                }
            }
            else
            {
                _logger.LogWarning("PR Details Window loaded with NULL or invalid DataContext");
            }
        };
    }

    // The DiffViewer now updates itself when its DataContext changes,
    // so no additional initialization is required here.
}
