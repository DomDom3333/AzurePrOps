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
    private const int AutoExpandInitialLimit = 10;
    private readonly Dictionary<string, DiffViewer> _diffViewerMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _expansionSemaphore = new(1);
    private CancellationTokenSource? _expansionCancellation;
    private readonly HashSet<string> _expandingDiffs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Expander> _expanderCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _isExpansionInProgress;
    
    public PullRequestDetailsWindow()
    {
        InitializeComponent();

        // Auto-expand a capped number of diffs when window loads if user preference is enabled
        this.Loaded += async (_, _) =>
        {
            if (DataContext is PullRequestDetailsWindowViewModel viewModel && DiffPreferences.ExpandAllOnOpen)
            {
                await StartAsyncExpansionAsync(viewModel, isExplicitExpandAll: false);
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
            await StartAsyncExpansionAsync(viewModel, isExplicitExpandAll: true);
        }
    }

    private async Task StartAsyncExpansionAsync(PullRequestDetailsWindowViewModel viewModel, bool isExplicitExpandAll)
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
            _isExpansionInProgress = true;
            
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

            var filePaths = sortedDiffs.Select(diff => diff.FilePath).ToList();
            List<string> targetPaths;

            if (isExplicitExpandAll)
            {
                targetPaths = filePaths
                    .Where(path => !IsDiffExpanded(path))
                    .ToList();
                await RunExpandAllWithProgressAsync(targetPaths, _expansionCancellation.Token);
            }
            else
            {
                var autoExpandCount = CalculateAutoExpandCount(DiffPreferences.ExpandAllOnOpen, filePaths.Count);
                targetPaths = filePaths.Take(autoExpandCount).ToList();
                await SmartExpansionWorkerAsync(targetPaths, _expansionCancellation.Token);
                await Dispatcher.UIThread.InvokeAsync(() => UpdateExpandProgressUi(0, 0, false));
            }
        }
        catch (OperationCanceledException)
        {
            // Expansion startup was cancelled, this is expected
        }
        catch (Exception)
        {
            // Log error if needed, but don't show to user
        }
        finally
        {
            _isExpansionInProgress = false;
        }
    }

    internal static int CalculateAutoExpandCount(bool expandAllOnOpen, int fileCount)
    {
        if (!expandAllOnOpen || fileCount <= 0)
            return 0;

        return Math.Min(fileCount, AutoExpandInitialLimit);
    }

    private async Task RunExpandAllWithProgressAsync(List<string> filePaths, CancellationToken cancellationToken)
    {
        if (filePaths.Count == 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() => UpdateExpandProgressUi(0, 0, false));
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => UpdateExpandProgressUi(0, filePaths.Count, true));

        await SmartExpansionWorkerAsync(filePaths, cancellationToken, (processed, total) =>
        {
            _ = Dispatcher.UIThread.InvokeAsync(() => UpdateExpandProgressUi(processed, total, true));
        });

        await Dispatcher.UIThread.InvokeAsync(() => UpdateExpandProgressUi(filePaths.Count, filePaths.Count, false));
    }

    private async Task SmartExpansionWorkerAsync(List<string> filePaths, CancellationToken cancellationToken, Action<int, int>? progress = null)
    {
        int processedCount = 0;
        const int batchSize = 5;
        int total = filePaths.Count;
        
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
                progress?.Invoke(processedCount, total);
                
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

    private bool IsDiffExpanded(string filePath)
    {
        if (_expanderCache.TryGetValue(filePath, out var cachedExpander))
            return cachedExpander.IsExpanded;

        var expander = this.GetVisualDescendants()
            .OfType<Expander>()
            .FirstOrDefault(exp => exp.DataContext is FileDiffListItemViewModel diff && diff.FilePath == filePath);

        if (expander == null)
            return false;

        _expanderCache[filePath] = expander;
        return expander.IsExpanded;
    }

    private void UpdateExpandProgressUi(int processed, int total, bool isRunning)
    {
        if (ExpandAllButton == null || ExpandProgressBar == null || ExpandProgressText == null)
            return;

        ExpandAllButton.Content = isRunning ? "⏹ Cancel" : "📂 Expand all";
        ExpandProgressBar.IsVisible = isRunning || (total > 0 && processed > 0);
        ExpandProgressText.IsVisible = total > 0;
        ExpandProgressBar.Maximum = Math.Max(total, 1);
        ExpandProgressBar.Value = Math.Min(processed, total);
        ExpandProgressText.Text = total > 0
            ? $"Expanded {processed}/{total}"
            : "";
    }

    private async void ExpandAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailsWindowViewModel viewModel)
            return;

        if (_isExpansionInProgress && _expansionCancellation is { IsCancellationRequested: false })
        {
            _expansionCancellation.Cancel();
            return;
        }

        await StartAsyncExpansionAsync(viewModel, isExplicitExpandAll: true);
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
            e.Handled = true;
        }
        // Left-click: do NOT mark as handled — let the Button fire its Click event normally
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
            // Use the expander's Content as the direct container
            if (expander.Content is not Border diffContainer)
                return;

            // Check if DiffViewer already exists to avoid recreating it
            if (_diffViewerMap.TryGetValue(diff.FilePath, out var existingViewer))
            {
                if (diffContainer.Child != existingViewer)
                {
                    diffContainer.Child = null; // Clear existing placeholder or stale viewer
                    diffContainer.Child = existingViewer;
                    diffContainer.MinHeight = 500;
                }
                return;
            }

            // First, show loading placeholder on UI thread - explicitly replace whatever is there
            diffContainer.Child = null; 
            var loadingText = new TextBlock
            {
                Text = "Loading diff...",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(20)
            };
            diffContainer.Child = loadingText;

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
            _diffViewerMap[diff.FilePath] = diffViewer;
            diffContainer.Child = null; // Remove loading indicator
            diffContainer.Child = diffViewer;
            diffContainer.MinHeight = 500;

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
            if (expander.Content is Border errorContainer)
            {
                bool isErrorMessage = errorContainer.Child is TextBlock tb && tb.Foreground == Brushes.Red;
                if (!isErrorMessage)
                {
                    errorContainer.Child = null;
                    var errorText = new TextBlock
                    {
                        Text = $"Error loading diff: {ex.Message}",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Foreground = Brushes.Red,
                        Margin = new Thickness(20)
                    };
                    errorContainer.Child = errorText;
                }
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
