using System;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Templates;
using AzurePrOps.Controls;
using AzurePrOps.ViewModels;
using AzurePrOps.ReviewLogic.Models;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using System.Collections.Generic;
using Avalonia.VisualTree;

namespace AzurePrOps.Views;

public partial class PullRequestDetailsWindow : Window
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestDetailsWindow>();
    private readonly Dictionary<string, DiffViewer> _diffViewerMap = new();
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

    private void DiffViewer_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is DiffViewer dv && dv.DataContext is FileDiff diff)
        {
            _diffViewerMap[diff.FilePath] = dv;

            if (DataContext is PullRequestDetailsWindowViewModel vm)
            {
                dv.PullRequestId = vm.PullRequestId;
            }
        }
    }

    private void ThreadSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not CommentThread thread)
            return;

        if (_diffViewerMap.TryGetValue(thread.FilePath, out var viewer))
        {
            var expander = viewer.FindAncestorOfType<Expander>();
            if (expander != null)
                expander.IsExpanded = true;

            viewer.BringIntoView();
            viewer.JumpToLine(thread.LineNumber);
        }
    }

    // The DiffViewer now updates itself when its DataContext changes,
    // so no additional initialization is required here.
}
