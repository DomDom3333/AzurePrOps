using System;
using Avalonia.Controls;
using Avalonia;
using AzurePrOps.Controls;
using AzurePrOps.ViewModels;
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.Models;
using System.Collections.Generic;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using Avalonia.Media;

namespace AzurePrOps.Views;

public partial class PullRequestDetailsWindow : Window
{
    private readonly Dictionary<string, DiffViewer> _diffViewerMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _expansionSemaphore = new(1);
    private CancellationTokenSource? _expansionCancellation;
    private readonly HashSet<string> _expandingDiffs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Expander> _expanderCache = new(StringComparer.OrdinalIgnoreCase);
    
    public PullRequestDetailsWindow()
    {
        InitializeComponent();

        // Enable ExpandAllOnOpen by default if not already enabled
        if (!DiffPreferences.ExpandAllOnOpen)
        {
            DiffPreferences.ExpandAllOnOpen = true;
        }

        // Auto-expand diffs when window loads if user preference is enabled
        this.Loaded += async (_, _) =>
        {
            if (DataContext is PullRequestDetailsWindowViewModel viewModel && DiffPreferences.ExpandAllOnOpen)
            {
                await StartAsyncExpansionAsync(viewModel);
            }
        };
        
        // Cancel expansion when window is closing
        this.Closing += (_, _) =>
        {
            _expansionCancellation?.Cancel();
        };
    }

    // Manual expansion method for testing/debugging
    public async Task ManualExpandAllAsync()
    {
        if (DataContext is PullRequestDetailsWindowViewModel viewModel)
        {
            await StartAsyncExpansionAsync(viewModel);
        }
    }

    private async Task StartAsyncExpansionAsync(PullRequestDetailsWindowViewModel viewModel)
    {
        try
        {
            // Cancel any previous expansion
            if (_expansionCancellation != null)
            {
                await _expansionCancellation.CancelAsync();
                _expansionCancellation.Dispose();
            }
            _expansionCancellation = new CancellationTokenSource();
            
            // Wait for the diffs to load first
            int waitAttempts = 0;
            const int maxWaitAttempts = 50;
            
            while (viewModel.IsLoading && waitAttempts < maxWaitAttempts && !_expansionCancellation.Token.IsCancellationRequested)
            {
                await Task.Delay(200, _expansionCancellation.Token);
                waitAttempts++;
            }
            
            if (_expansionCancellation.Token.IsCancellationRequested || viewModel.IsLoading) 
                return;
            
            // Wait for UI to fully render before attempting expansion
            await Task.Delay(1000, _expansionCancellation.Token);
            
            // Prioritize smaller files first for better perceived performance
            var sortedDiffs = viewModel.FileDiffs
                .OrderBy(EstimateRenderComplexity)
                .ThenBy(d => d.FilePath)
                .ToList();
            
            // Start background expansion with smart throttling
            _ = Task.Run(async () =>
            {
                try
                {
                    await SmartExpansionWorkerAsync(sortedDiffs, _expansionCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expansion was cancelled, this is expected
                }
                catch (Exception)
                {
                    // Log error if needed, but don't show to user
                }
            }, _expansionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Expansion startup was cancelled, this is expected
        }
        catch (Exception)
        {
            // Log error if needed, but don't show to user
        }
    }

    private async Task SmartExpansionWorkerAsync(List<FileDiffListItemViewModel> sortedDiffs, CancellationToken cancellationToken)
    {
        int processedCount = 0;
        const int batchSize = 5;
        
        var filePaths = sortedDiffs.Select(diff => diff.FilePath).ToList();
        
        foreach (var filePath in filePaths)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await _expansionSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                lock (_expandingDiffs)
                {
                    if (_expandingDiffs.Contains(filePath))
                        continue; // Already being expanded
                    _expandingDiffs.Add(filePath);
                }

                await ExpandDiffAsync(filePath, cancellationToken);
                
                processedCount++;
                
                // Add longer delays between batches to prevent system overload
                if (processedCount % batchSize == 0)
                {
                    await Task.Delay(500, cancellationToken);
                }
                else
                {
                    await Task.Delay(200, cancellationToken);
                }
            }
            finally
            {
                lock (_expandingDiffs)
                {
                    _expandingDiffs.Remove(filePath);
                }
                _expansionSemaphore.Release();
            }
        }
    }

    private async Task ExpandDiffAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                
                // Search for the expander in the visual tree
                var expander = this.GetVisualDescendants()
                    .OfType<Expander>()
                    .FirstOrDefault(exp => exp.DataContext is FileDiffListItemViewModel diff && diff.FilePath == filePath);
                
                if (expander != null)
                {
                    // Cache for future use
                    _expanderCache[filePath] = expander;
                    
                    if (!expander.IsExpanded)
                    {
                        expander.IsExpanded = true;
                    }
                }
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling
        }
        catch (Exception)
        {
            // Log error if needed
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
            // Note: JumpToLine method needs to be implemented in DiffViewer
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
        if (sender is Expander expander && expander.DataContext is FileDiffListItemViewModel diff)
        {
            // Cache this expander for future use
            _expanderCache[diff.FilePath] = expander;
            
            // Use async creation to prevent UI blocking
            _ = CreateDiffViewerAsync(expander, diff);
        }
    }

    private async Task CreateDiffViewerAsync(Expander expander, FileDiffListItemViewModel diff)
    {
        try
        {
            // Check if DiffViewer already exists to avoid recreating it
            if (_diffViewerMap.ContainsKey(diff.FilePath))
                return;

            // First, show loading placeholder on UI thread
            var diffContainer = expander.FindDescendantOfType<Border>();
            if (diffContainer != null)
            {
                var loadingText = new TextBlock
                {
                    Text = "Loading diff...",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(20)
                };
                diffContainer.Child = loadingText;
            }

            await Task.Delay(50);

            if (DataContext is not PullRequestDetailsWindowViewModel vm)
                return;

            var loadToken = vm.BeginDiffContentLoad();
            await vm.EnsureDiffContentLoadedAsync(diff.FilePath, loadToken);
            if (loadToken.IsCancellationRequested)
                return;

            // Create DiffViewer on UI thread
            var diffViewer = new DiffViewer
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                ViewMode = DiffViewMode.SideBySide
            };

            // Set PullRequestId if available
            diffViewer.PullRequestId = vm.PullRequestId;

            // Add to UI and map immediately
            if (diffContainer != null)
            {
                _diffViewerMap[diff.FilePath] = diffViewer;
                diffContainer.Child = diffViewer;
                diffContainer.MinHeight = 500;
            }
            // Apply loaded diff data
            diffViewer.OldText = diff.OldText ?? string.Empty;
            diffViewer.NewText = diff.NewText ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            // User switched files rapidly; ignore.
        }
        catch (Exception ex)
        {
            // Log error if needed
            
            // Show error on UI thread
            var diffContainer = expander.FindDescendantOfType<Border>();
            if (diffContainer?.Child is not TextBlock)
            {
                var errorText = new TextBlock
                {
                    Text = $"Error loading diff: {ex.Message}",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = Brushes.Red,
                    Margin = new Thickness(20)
                };
                diffContainer.Child = errorText;
            }
        }
    }

    private void ViewInIDE_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control btn && btn.DataContext is FileDiffListItemViewModel diff)
        {
            if (_diffViewerMap.TryGetValue(diff.FilePath, out var viewer))
            {
                try
                {
                    // Open at current caret if possible; otherwise line 1
                    var line = 1;
                    var getActiveEditorMethod = viewer.GetType().GetMethod("GetActiveEditor", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var active = getActiveEditorMethod?.Invoke(viewer, null);
                    
                    if (active is AvaloniaEdit.TextEditor te)
                    {
                        line = te.TextArea?.Caret?.Line ?? 1;
                    }
                    
                    viewer.IDEService?.OpenInIDE(diff.FilePath, line);
                }
                catch (Exception)
                {
                    // Best-effort notify via viewer if possible
                }
            }
        }
    }

    /// <summary>
    /// Estimate rendering complexity to prioritize simpler files first
    /// </summary>
    private static int EstimateRenderComplexity(FileDiffListItemViewModel diff)
    {
        int complexity = 0;
        
        // Size factor (larger files are more complex)
        var oldLength = diff.OldText?.Length ?? 0;
        var newLength = diff.NewText?.Length ?? 0;
        complexity += (oldLength + newLength) / 1000;
        
        // Line count factor
        var oldLines = diff.OldText?.Split('\n').Length ?? 0;
        var newLines = diff.NewText?.Split('\n').Length ?? 0;
        complexity += (oldLines + newLines) / 10;
        
        // File type factor
        var extension = System.IO.Path.GetExtension(diff.FilePath).ToLowerInvariant();
        complexity += extension switch
        {
            ".cs" => 5,
            ".js" => 5,
            ".ts" => 5,
            ".json" => 3,
            ".xml" => 3,
            ".html" => 4,
            ".css" => 3,
            ".md" => 2,
            ".txt" => 1,
            _ => 4
        };
        
        return complexity;
    }
}
