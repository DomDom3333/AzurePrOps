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
using AzurePrOps.Models;
using System.Threading.Tasks;
using System.Linq;

namespace AzurePrOps.Views;

public partial class PullRequestDetailsWindow : Window
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestDetailsWindow>();
    private readonly Dictionary<string, DiffViewer> _diffViewerMap = new();
    public PullRequestDetailsWindow()
    {
        InitializeComponent();

        // Add enhanced logging after window is loaded to debug data binding
        this.Loaded += async (s, e) =>
        {
            if (DataContext is PullRequestDetailsWindowViewModel viewModel)
            {
                _logger.LogInformation("PR Details Window loaded with {Count} file diffs", viewModel.FileDiffs.Count);
                
                // Check if user wants all diffs expanded on open
                if (DiffPreferences.ExpandAllOnOpen)
                {
                    _logger.LogInformation("Expanding all diffs as per user preference");
                    await WaitForDiffsAndExpandAsync(viewModel);
                }
            }
            else
            {
                _logger.LogWarning("PR Details Window loaded with NULL or invalid DataContext");
            }
        };
    }

    private async Task WaitForDiffsAndExpandAsync(PullRequestDetailsWindowViewModel viewModel)
    {
        try
        {
            // Wait for the diffs to be loaded
            _logger.LogInformation("Waiting for diffs to load (IsLoading: {IsLoading}, FileDiffs: {Count})", viewModel.IsLoading, viewModel.FileDiffs.Count);
            
            int waitAttempts = 0;
            const int maxWaitAttempts = 50; // Wait up to 10 seconds (50 * 200ms)
            
            while (viewModel.IsLoading && waitAttempts < maxWaitAttempts)
            {
                await Task.Delay(200);
                waitAttempts++;
                
                if (waitAttempts % 5 == 0) // Log every second
                {
                    _logger.LogDebug("Still waiting for diffs to load... attempt {Attempt}/{Max} (IsLoading: {IsLoading}, FileDiffs: {Count})", 
                        waitAttempts, maxWaitAttempts, viewModel.IsLoading, viewModel.FileDiffs.Count);
                }
            }
            
            if (viewModel.IsLoading)
            {
                _logger.LogWarning("Timed out waiting for diffs to load after {Attempts} attempts", maxWaitAttempts);
                return;
            }
            
            _logger.LogInformation("Diffs loaded successfully, now expanding {Count} diffs", viewModel.FileDiffs.Count);
            await ExpandAllDiffsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for diffs to load");
        }
    }

    private async Task ExpandAllDiffsAsync()
    {
        try
        {
            // First check if there are any FileDiffs to expand
            if (DataContext is PullRequestDetailsWindowViewModel viewModel)
            {
                if (viewModel.FileDiffs.Count == 0)
                {
                    _logger.LogInformation("No file diffs available to expand");
                    return;
                }
                _logger.LogDebug("Will attempt to expand {Count} file diffs", viewModel.FileDiffs.Count);
            }

            // Wait longer for the visual tree to be fully constructed
            await Task.Delay(500);
            
            // Retry mechanism to find expanders - visual tree might not be ready immediately
            List<Expander> expanders = new();
            int retries = 0;
            const int maxRetries = 10; // Increased retry count
            
            while (expanders.Count == 0 && retries < maxRetries)
            {
                // Find all expanders in the visual tree
                expanders = this.GetVisualDescendants()
                    .OfType<Expander>()
                    .Where(exp => exp.DataContext is FileDiff)
                    .ToList();

                _logger.LogDebug("Attempt {Retry}: Found {Count} expanders in visual tree", retries + 1, expanders.Count);
                
                // Also log total visual descendants for debugging
                var totalDescendants = this.GetVisualDescendants().Count();
                var totalExpanders = this.GetVisualDescendants().OfType<Expander>().Count();
                _logger.LogDebug("Visual tree contains {Total} descendants, {Expanders} total expanders", 
                    totalDescendants, totalExpanders);
                
                if (expanders.Count == 0)
                {
                    retries++;
                    await Task.Delay(500); // Increased delay between retries
                }
            }

            if (expanders.Count == 0)
            {
                _logger.LogWarning("No expanders found after {MaxRetries} attempts", maxRetries);
                return;
            }

            // Expand expanders in batches to prevent UI freezing
            const int batchSize = 3; // Process 3 at a time
            for (int i = 0; i < expanders.Count; i += batchSize)
            {
                var batch = expanders.Skip(i).Take(batchSize);
                foreach (var expander in batch)
                {
                    if (!expander.IsExpanded)
                    {
                        expander.IsExpanded = true;
                        _logger.LogDebug("Expanded diff for {Path}", 
                            expander.DataContext is FileDiff diff ? diff.FilePath : "unknown");
                    }
                }
                
                // Small delay between batches to allow UI to update
                if (i + batchSize < expanders.Count)
                {
                    await Task.Delay(100); // Increased batch delay
                }
            }

            _logger.LogInformation("Completed expanding all diffs ({Count} total)", expanders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error expanding all diffs");
        }
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

    private void ViewInIDE_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Handle middle-click or right-click to open IDE
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed || 
            e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            ViewInIDE_Click(sender, new Avalonia.Interactivity.RoutedEventArgs());
        }
        e.Handled = true;
    }

    private void Expander_Expanding(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Expander expander && expander.DataContext is FileDiff diff)
        {
            // Check if DiffViewer already exists to avoid recreating it
            if (_diffViewerMap.ContainsKey(diff.FilePath))
                return;

            // Find the DiffContainer Border within the expander
            var diffContainer = expander.FindDescendantOfType<Border>();
            if (diffContainer != null)
            {
                // Create and configure the DiffViewer
                var diffViewer = new DiffViewer
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                    ViewMode = DiffViewMode.SideBySide,
                    NewText = diff.NewText,
                    OldText = diff.OldText
                };

                // Set PullRequestId if available
                if (DataContext is PullRequestDetailsWindowViewModel vm)
                {
                    diffViewer.PullRequestId = vm.PullRequestId;
                }

                // Add to map and replace container content
                _diffViewerMap[diff.FilePath] = diffViewer;
                diffContainer.Child = diffViewer;

                // Update container height for better UX
                diffContainer.MinHeight = 500;
            }
        }
    }

    private void ViewInIDE_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control btn)
        {
            // Get the FileDiff from the DataContext of the header item
            if (btn.DataContext is FileDiff diff)
            {
                if (_diffViewerMap.TryGetValue(diff.FilePath, out var viewer))
                {
                    try
                    {
                        // Open at current caret if possible; otherwise line 1
                        var line = 1;
                        var active = viewer.GetType().GetMethod("GetActiveEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(viewer, null);
                        if (active is AvaloniaEdit.TextEditor te)
                        {
                            line = te.TextArea?.Caret?.Line ?? 1;
                        }
                        viewer.IDEService?.OpenInIDE(diff.FilePath, line);
                    }
                    catch (Exception ex)
                    {
                        // Best-effort notify via viewer if possible
                        viewer.NotificationService?.Notify("ide-error", new AzurePrOps.ReviewLogic.Models.Notification
                        {
                            Title = "IDE Error",
                            Message = ex.Message,
                            Type = AzurePrOps.ReviewLogic.Models.NotificationType.Error
                        });
                    }
                }
            }
        }
    }
}
