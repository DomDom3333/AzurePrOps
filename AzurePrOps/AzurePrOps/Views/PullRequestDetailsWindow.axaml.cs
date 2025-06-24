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

    private void DiffViewer_Loaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is DiffViewer diffViewer && diffViewer.Parent is IDataContextProvider container)
        {
            // Force a render by explicitly setting the properties again
            if (container.DataContext is FileDiff fileDiff)
            {
                Console.WriteLine($"DiffViewer_Loaded: Explicitly loading diff for {fileDiff.FilePath}");

                // Clear and reset texts to force re-render
                string oldText = fileDiff.OldText ?? string.Empty;
                string newText = fileDiff.NewText ?? string.Empty;

                // If we have completely empty content but a diff string, try parsing the diff directly
                if (string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText) && !string.IsNullOrEmpty(fileDiff.Diff))
                {
                    Console.WriteLine("  Empty texts but has diff - attempting to generate visual diff");
                    if (DataContext is PullRequestDetailsWindowViewModel vm)
                    {
                        var (parsedOld, parsedNew) = vm.ParseDiffToContent(fileDiff.Diff);
                        if (!string.IsNullOrEmpty(parsedOld) || !string.IsNullOrEmpty(parsedNew))
                        {
                            oldText = parsedOld;
                            newText = parsedNew;
                            Console.WriteLine($"  Generated OldText: {oldText.Length} chars, NewText: {newText.Length} chars");
                        }
                    }
                }

                // For new files, ensure we show the changes properly
                if (string.IsNullOrEmpty(oldText) && !string.IsNullOrEmpty(newText) || oldText.Contains("[FILE ADDED]"))
                {
                    Console.WriteLine("  This appears to be a new file");
                    // Prepare content for new files
                    if (oldText.Contains("[FILE ADDED]"))
                    {
                        // Keep the marker to signal the DiffViewer this is a new file
                        // but ensure newText has proper content
                        if (string.IsNullOrEmpty(newText))
                        {
                            newText = "[No content available]\n";
                        }
                    }
                    else
                    {
                        // Set a marker explicitly
                        oldText = "[FILE ADDED]\n";
                    }

                    // Force re-render by clearing and setting both texts in sequence
                    diffViewer.OldText = string.Empty;
                    diffViewer.NewText = string.Empty;
                    diffViewer.OldText = oldText;
                    diffViewer.NewText = newText;
                }
                // For deleted files, ensure we show the changes properly
                else if (!string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText) || newText.Contains("[FILE DELETED]"))
                {
                    Console.WriteLine("  This appears to be a deleted file");
                    // Prepare content for deleted files
                    if (newText.Contains("[FILE DELETED]"))
                    {
                        // Keep the marker to signal the DiffViewer this is a deleted file
                        // but ensure oldText has proper content
                        if (string.IsNullOrEmpty(oldText))
                        {
                            oldText = "[No content available]\n";
                        }
                    }
                    else
                    {
                        // Set a marker explicitly
                        newText = "[FILE DELETED]\n";
                    }

                    // Force re-render by clearing and setting both texts in sequence
                    diffViewer.OldText = string.Empty;
                    diffViewer.NewText = string.Empty;
                    diffViewer.OldText = oldText;
                    diffViewer.NewText = newText;
                }
                // For modified files
                else
                {
                    // Force re-render by clearing and setting both texts
                    diffViewer.OldText = string.Empty;
                    diffViewer.NewText = string.Empty;

                    // Ensure we have some content for both sides
                    oldText = string.IsNullOrEmpty(oldText) ? "[No original content]" : oldText;
                    newText = string.IsNullOrEmpty(newText) ? "[No new content]" : newText;

                    // Check if texts are identical by content (not just length) and force differences if needed
                    if (string.Equals(oldText, newText, StringComparison.Ordinal) && oldText.Length > 0 && !string.IsNullOrEmpty(fileDiff.Diff))
                    {
                        Console.WriteLine("  Identical content detected when there should be differences - using diff string");
                        // Add indicator and raw diff to help troubleshoot
                        newText = oldText + "\n\n[DIFF STRING CONTENT]:\n" + fileDiff.Diff;
                    }

                    diffViewer.OldText = oldText;
                    diffViewer.NewText = newText;
                }

                // Force a render now that the texts are set
                diffViewer.Render();
            }
        }
    }
}
