using System;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using ConnectionModels = AzurePrOps.AzureConnection.Models;
using ReviewModels = AzurePrOps.ReviewLogic.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using AzurePrOps.Views;
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.ReviewLogic.Services;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Infrastructure;

namespace AzurePrOps.ViewModels;

public class PullRequestDetailsWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestDetailsWindowViewModel>();
    private readonly AzureDevOpsClient _client = new();
    private readonly IPullRequestService _pullRequestService;
    private readonly ConnectionSettings _settings;
    private readonly ICommentsService _commentsService;
    public ConnectionModels.PullRequestInfo PullRequest { get; }
    public int PullRequestId => PullRequest.Id;

    public ObservableCollection<ConnectionModels.PullRequestComment> Comments { get; }
    public ObservableCollection<CommentThread> Threads { get; } = new();

    private bool _showUnresolvedOnly = true;
    public bool ShowUnresolvedOnly
    {
        get => _showUnresolvedOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showUnresolvedOnly, value);
            this.RaisePropertyChanged(nameof(FilteredThreads));
            this.RaisePropertyChanged(nameof(FilteredThreadsCount));
        }
    }

    public IEnumerable<CommentThread> FilteredThreads =>
        ShowUnresolvedOnly ? Threads.Where(t => !string.Equals(t.Status, "closed", StringComparison.OrdinalIgnoreCase)) : Threads;

    public int FilteredThreadsCount =>
        ShowUnresolvedOnly
            ? Threads.Count(t => !string.Equals(t.Status, "closed", StringComparison.OrdinalIgnoreCase))
            : Threads.Count;

    public ReactiveCommand<Unit, Unit> OpenInBrowserCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowInsightsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowCommentsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDiffSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshDiffsCommand { get; }
    public ReactiveCommand<Unit, Unit> CompleteCommand { get; }
    public ReactiveCommand<Unit, Unit> AbandonCommand { get; }
    public ReactiveCommand<Unit, Unit> PostCommentCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleSidebarCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOverviewCommand { get; }

    private string _newCommentText = string.Empty;
    public string NewCommentText
    {
        get => _newCommentText;
        set => this.RaiseAndSetIfChanged(ref _newCommentText, value);
    }

    public ObservableCollection<ReviewModels.FileDiff> FileDiffs { get; } = new();
    
    // Total metrics properties for overview section
    private int _filesChanged;
    public int FilesChanged
    {
        get => _filesChanged;
        set => this.RaiseAndSetIfChanged(ref _filesChanged, value);
    }
    
    private int _totalComments;
    public int TotalComments
    {
        get => _totalComments;
        set => this.RaiseAndSetIfChanged(ref _totalComments, value);
    }
    
    private int _linesAdded;
    public int LinesAdded
    {
        get => _linesAdded;
        set => this.RaiseAndSetIfChanged(ref _linesAdded, value);
    }
    
    private int _linesRemoved;
    public int LinesRemoved
    {
        get => _linesRemoved;
        set => this.RaiseAndSetIfChanged(ref _linesRemoved, value);
    }
    
    private int _lintWarnings;
    public int LintWarnings
    {
        get => _lintWarnings;
        set => this.RaiseAndSetIfChanged(ref _lintWarnings, value);
    }
    
    private double _reviewTimeMin;
    public double ReviewTimeMin
    {
        get => _reviewTimeMin;
        set => this.RaiseAndSetIfChanged(ref _reviewTimeMin, value);
    }
    
    private int _securityRisks;
    public int SecurityRisks
    {
        get => _securityRisks;
        set => this.RaiseAndSetIfChanged(ref _securityRisks, value);
    }
    
    private int _codeComplexity;
    public int CodeComplexity
    {
        get => _codeComplexity;
        set => this.RaiseAndSetIfChanged(ref _codeComplexity, value);
    }
    
    private int _testCoverage;
    public int TestCoverage
    {
        get => _testCoverage;
        set => this.RaiseAndSetIfChanged(ref _testCoverage, value);
    }

    // Loading state properties
    private bool _isLoading = false;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private double _loadingProgress = 0.0;
    public double LoadingProgress
    {
        get => _loadingProgress;
        set => this.RaiseAndSetIfChanged(ref _loadingProgress, value);
    }

    private string _loadingStatus = string.Empty;
    public string LoadingStatus
    {
        get => _loadingStatus;
        set => this.RaiseAndSetIfChanged(ref _loadingStatus, value);
    }

    // Layout toggle properties for maximizing diff viewer space
    private bool _isSidebarVisible = true;
    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSidebarVisible, value);
            this.RaisePropertyChanged(nameof(SidebarWidth));
        }
    }

    private bool _isOverviewVisible = true;
    public bool IsOverviewVisible
    {
        get => _isOverviewVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isOverviewVisible, value);
            this.RaisePropertyChanged(nameof(OverviewHeight));
        }
    }

    // Dynamic column width for sidebar - 300 when visible, 0 when hidden
    public GridLength SidebarWidth => IsSidebarVisible ? new GridLength(300) : new GridLength(0);
    
    // Dynamic row height for overview - Auto when visible, 0 when hidden
    public GridLength OverviewHeight => IsOverviewVisible ? GridLength.Auto : new GridLength(0);

    public PullRequestDetailsWindowViewModel(
        ConnectionModels.PullRequestInfo pullRequest,
        IPullRequestService pullRequestService,
        ConnectionSettings settings,
        IEnumerable<ConnectionModels.PullRequestComment>? comments = null,
        IEnumerable<ReviewModels.FileDiff>? diffs = null,
        ICommentsService? commentsService = null)
    {
        PullRequest = pullRequest;
        _pullRequestService = pullRequestService;
        _settings = settings;
        _commentsService = commentsService ?? ServiceRegistry.Resolve<ICommentsService>() ?? new CommentsService(new AzureDevOpsClient());
        Comments = comments != null
            ? new ObservableCollection<ConnectionModels.PullRequestComment>(comments)
            : new ObservableCollection<ConnectionModels.PullRequestComment>();
        
        // Initialize loading state
        IsLoading = true;
        LoadingProgress = 0.0;
        LoadingStatus = "Initializing...";
        
        if (diffs != null)
        {
            _ = LoadDiffsAsync(diffs);
        }

        _ = LoadThreadsAsync();

        OpenInBrowserCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrWhiteSpace(PullRequest.WebUrl))
                return;
            try
            {
                // Use the validated WebUrl property instead of the raw Url
                string sanitizedUrl = PullRequest.WebUrl.Trim();
                if (!Uri.IsWellFormedUriString(sanitizedUrl, UriKind.Absolute))
                {
                    _logger.LogWarning("Invalid URL format: {Url}", sanitizedUrl);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = sanitizedUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // Swallow exceptions to avoid crashing the app
                _logger.LogWarning(ex, "Failed to open browser");
            }
        }, System.Reactive.Linq.Observable.Return(!string.IsNullOrWhiteSpace(PullRequest.WebUrl)));

        ShowCommentsCommand = ReactiveCommand.Create(() =>
        {
            string title = $"Comments for PR #{PullRequest.Id}";
            var vm = new CommentsWindowViewModel(title, Comments);
            var window = new CommentsWindow { DataContext = vm };
            window.Show();
        });

        ShowInsightsCommand = ReactiveCommand.Create(() =>
        {
            // Use the already calculated total metrics
            var metrics = new List<MetricData>
            {
                new MetricData { Name = "Files Changed", Value = FilesChanged },
                new MetricData { Name = "Comments",     Value = TotalComments },
                new MetricData { Name = "Lines Added",   Value = LinesAdded },
                new MetricData { Name = "Lines Removed", Value = LinesRemoved }
            };

            string title = $"Insights for PR #{PullRequest.Id}";
            var vm = new InsightsWindowViewModel(title, metrics);
            var window = new InsightsWindow { DataContext = vm };
            window.Show();
        });

        OpenDiffSettingsCommand = ReactiveCommand.Create(() =>
        {
            var vm = new DiffSettingsWindowViewModel();
            var window = new DiffSettingsWindow { DataContext = vm };
            window.Show();
        });

        RefreshDiffsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var diffs = await _pullRequestService.GetPullRequestDiffAsync(
                    _settings.Organization,
                    _settings.Project,
                    _settings.Repository,
                    PullRequest.Id,
                    _settings.PersonalAccessToken,
                    null,
                    null);

                await LoadDiffsAsync(diffs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh diffs");
            }
        });

        CompleteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var options = new ReviewModels.MergeOptions(false, false, string.Empty);
            await _client.CompletePullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                options,
                _settings.PersonalAccessToken);
        });

        AbandonCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _client.AbandonPullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                _settings.PersonalAccessToken);
        });

        PostCommentCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewCommentText))
                return;

            await _client.PostPullRequestCommentAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                NewCommentText,
                _settings.PersonalAccessToken);

            await Dispatcher.UIThread.InvokeAsync(() => NewCommentText = string.Empty);
        });

        // Toggle commands for maximizing diff viewer space
        ToggleSidebarCommand = ReactiveCommand.Create(() =>
        {
            IsSidebarVisible = !IsSidebarVisible;
        });

        ToggleOverviewCommand = ReactiveCommand.Create(() =>
        {
            IsOverviewVisible = !IsOverviewVisible;
        });
        
        // Initialize total metrics
        UpdateTotalMetrics();
        
        // Update metrics when collections change
        FileDiffs.CollectionChanged += (_, _) => UpdateTotalMetrics();
        Comments.CollectionChanged += (_, _) => UpdateTotalMetrics();
    }
    
    private void UpdateTotalMetrics()
    {
        FilesChanged = FileDiffs.Count;
        TotalComments = Comments.Count;
        
        int added = 0;
        int removed = 0;
        int lintWarnings = 0;
        int securityRisks = 0;
        int complexityScore = 0;
        int testLines = 0;
        int totalCodeLines = 0;
        int testFiles = 0;
        int substantiveChanges = 0; // Track meaningful code changes
        int logicLines = 0; // Lines with actual logic (methods, classes, etc.)
        int configFiles = 0; // Configuration/infrastructure files
        double totalFileComplexity = 0;
        
        foreach (var diff in FileDiffs)
        {
            if (string.IsNullOrWhiteSpace(diff.Diff))
                continue;

            var fileName = Path.GetFileName(diff.FilePath).ToLowerInvariant();
            var filePath = diff.FilePath.ToLowerInvariant();
            
            // More comprehensive file type detection
            var isTestFile = filePath.Contains("test") || filePath.Contains("spec") ||
                           fileName.EndsWith(".test.js") || fileName.EndsWith(".test.cs") ||
                           fileName.EndsWith("tests.cs") || filePath.Contains("__tests__") ||
                           filePath.Contains("testing");
            
            var isConfigFile = fileName.EndsWith(".json") || fileName.EndsWith(".xml") ||
                             fileName.EndsWith(".yml") || fileName.EndsWith(".yaml") ||
                             fileName.EndsWith(".config") || fileName.EndsWith(".settings") ||
                             filePath.Contains("appsettings") || filePath.Contains("web.config") ||
                             fileName.EndsWith(".csproj") || fileName.EndsWith(".sln");
            
            var isDocumentationFile = fileName.EndsWith(".md") || fileName.EndsWith(".txt") ||
                                    fileName.EndsWith(".rst") || filePath.Contains("docs") ||
                                    filePath.Contains("readme");
            
            if (isTestFile) testFiles++;
            if (isConfigFile) configFiles++;

            var diffLines = diff.Diff.Split('\n');
            double fileComplexity = 0;
            int fileSubstantiveChanges = 0;
            int fileLogicLines = 0;
            
            foreach (var line in diffLines)
            {
                if (line.StartsWith("+") && !line.StartsWith("+++ "))
                {
                    added++;
                    totalCodeLines++;
                    if (isTestFile)
                        testLines++;
                        
                    var cleanLine = line.Substring(1).Trim();
                    var lowerLine = cleanLine.ToLowerInvariant();
                    
                    // Enhanced substantive change detection
                    var isSubstantiveChange = !string.IsNullOrWhiteSpace(cleanLine) &&
                                            !lowerLine.StartsWith("namespace ") && 
                                            !lowerLine.StartsWith("using ") &&
                                            !lowerLine.StartsWith("import ") &&
                                            cleanLine != "{" && cleanLine != "}" && 
                                            cleanLine != ");" && cleanLine != ";" &&
                                            !lowerLine.StartsWith("//") && // Skip comments
                                            !lowerLine.StartsWith("*") && // Skip comment blocks
                                            !lowerLine.StartsWith("<!--") && // Skip XML comments
                                            !System.Text.RegularExpressions.Regex.IsMatch(cleanLine, @"^\s*\[.*\]\s*$"); // Skip attributes
                    
                    if (isSubstantiveChange)
                    {
                        substantiveChanges++;
                        fileSubstantiveChanges++;
                        
                        // Check if this line contains actual logic
                        var hasLogic = lowerLine.Contains("if") || lowerLine.Contains("for") || 
                                     lowerLine.Contains("while") || lowerLine.Contains("switch") ||
                                     lowerLine.Contains("function") || lowerLine.Contains("method") ||
                                     lowerLine.Contains("class") || lowerLine.Contains("return") ||
                                     lowerLine.Contains("=>") || lowerLine.Contains("=") ||
                                     cleanLine.Contains("(") || cleanLine.Contains("[");
                        
                        if (hasLogic)
                        {
                            logicLines++;
                            fileLogicLines++;
                        }
                        
                        // Enhanced lint warning detection
                        if (lowerLine.Contains("todo") || lowerLine.Contains("fixme") || 
                            lowerLine.Contains("hack") || lowerLine.Contains("warning") ||
                            lowerLine.Contains("console.log") || lowerLine.Contains("system.out.print") ||
                            lowerLine.Contains("debugger") || lowerLine.Contains("alert(") ||
                            lowerLine.Contains("print(") || lowerLine.Contains("debug.print"))
                        {
                            lintWarnings++;
                        }
                        
                        // Enhanced security risk detection
                        if (lowerLine.Contains("password") || lowerLine.Contains("secret") ||
                            lowerLine.Contains("api_key") || lowerLine.Contains("apikey") ||
                            lowerLine.Contains("token") || lowerLine.Contains("eval(") || 
                            lowerLine.Contains("exec(") || lowerLine.Contains("sql") || 
                            lowerLine.Contains("innerhtml") || lowerLine.Contains("dangerouslysetinnerhtml") ||
                            lowerLine.Contains("unsafe") || lowerLine.Contains("external") ||
                            lowerLine.Contains("shell") || lowerLine.Contains("system("))
                        {
                            securityRisks++;
                        }
                        
                        // Enhanced complexity scoring - weight different constructs differently
                        var complexityKeywords = new Dictionary<string, double>
                        {
                            ["if"] = 1.0, ["else"] = 0.5, ["for"] = 1.5, ["foreach"] = 1.2,
                            ["while"] = 1.5, ["do"] = 1.5, ["switch"] = 2.0, ["case"] = 0.3,
                            ["try"] = 1.0, ["catch"] = 1.0, ["finally"] = 0.5,
                            ["&&"] = 0.5, ["||"] = 0.5, ["?"] = 0.8, // ternary
                            ["async"] = 0.5, ["await"] = 0.5, ["lambda"] = 1.0, ["=>"] = 0.8
                        };
                        
                        foreach (var kvp in complexityKeywords)
                        {
                            if (lowerLine.Contains(kvp.Key))
                            {
                                fileComplexity += kvp.Value;
                            }
                        }
                        
                        // Add complexity for nested structures
                        var nestingLevel = cleanLine.TakeWhile(c => c == ' ' || c == '\t').Count();
                        if (nestingLevel > 8) // Deeply nested code is more complex
                        {
                            fileComplexity += (nestingLevel - 8) * 0.2;
                        }
                    }
                }
                else if (line.StartsWith("-") && !line.StartsWith("--- "))
                    removed++;
            }
            
            // File-type based complexity adjustments
            if (isTestFile)
            {
                fileComplexity *= 0.4; // Test files are less complex to review
            }
            else if (isConfigFile)
            {
                fileComplexity *= 0.2; // Config files are usually simple
            }
            else if (isDocumentationFile)
            {
                fileComplexity *= 0.1; // Documentation is easy to review
            }
            else if (fileLogicLines > fileSubstantiveChanges * 0.8)
            {
                fileComplexity *= 1.3; // Logic-heavy files are more complex
            }
            
            totalFileComplexity += fileComplexity;
        }
        
        LinesAdded = added;
        LinesRemoved = removed;
        LintWarnings = lintWarnings;
        SecurityRisks = securityRisks;
        
        // More sophisticated complexity scoring
        var averageFileComplexity = FilesChanged > 0 ? totalFileComplexity / FilesChanged : 0;
        var normalizedComplexity = Math.Min(100, (int)(averageFileComplexity * 2)); // Scale to 0-100
        
        // Adjust based on PR characteristics
        if (testFiles > FilesChanged * 0.6) // If >60% are test files
            normalizedComplexity = (int)(normalizedComplexity * 0.7);
        if (configFiles > FilesChanged * 0.4) // If >40% are config files
            normalizedComplexity = (int)(normalizedComplexity * 0.5);
        
        CodeComplexity = Math.Max(1, normalizedComplexity); // Minimum complexity of 1
        
        // Better test coverage calculation
        TestCoverage = totalCodeLines > 0 ? Math.Min(100, (int)Math.Round((double)testLines / totalCodeLines * 100)) : 0;
        
        // Much more realistic review time calculation
        var baseTimeMin = Math.Max(3.0, FilesChanged * 0.5); // Base time scales with file count
        
        // Time per substantive change - varies by complexity and file type
        var avgTimePerChange = logicLines > substantiveChanges * 0.5 ? 1.2 : 0.8; // Logic-heavy changes take longer
        var changeTimeMin = substantiveChanges * avgTimePerChange;
        
        // File type adjustments
        var fileTypeMultiplier = 1.0;
        if (testFiles > FilesChanged * 0.7) fileTypeMultiplier *= 0.6; // Mostly tests - easier
        else if (configFiles > FilesChanged * 0.5) fileTypeMultiplier *= 0.4; // Mostly config - very easy
        else if (logicLines > substantiveChanges * 0.7) fileTypeMultiplier *= 1.4; // Logic-heavy - harder
        
        // Size-based adjustments (large PRs have some economies of scale)
        var sizeMultiplier = 1.0;
        if (added > 500) sizeMultiplier *= 0.9; // Some efficiency gains
        if (added > 1500) sizeMultiplier *= 0.8; // More efficiency for very large changes
        if (added > 5000) sizeMultiplier *= 0.7; // Bulk changes get significant discount
        
        // Complexity penalty
        var complexityMultiplier = 1.0 + (normalizedComplexity / 200.0); // Up to 50% penalty for high complexity
        
        // Quality issues penalty
        var qualityPenalty = lintWarnings * 1.0 + securityRisks * 2.0; // Security issues take longer
        
        ReviewTimeMin = Math.Round(
            (baseTimeMin + changeTimeMin + qualityPenalty) * fileTypeMultiplier * sizeMultiplier * complexityMultiplier,
            1);
        
        // Ensure reasonable bounds
        ReviewTimeMin = Math.Max(2.0, Math.Min(240.0, ReviewTimeMin)); // 2 min to 4 hours max
    }

    public async Task LoadDiffsAsync(IEnumerable<ReviewModels.FileDiff> diffs)
    {
        // Offload heavy diff processing to a background thread to avoid blocking the UI thread
        var diffList = diffs.ToList();
        var totalCount = diffList.Count;
        
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LoadingStatus = "Processing file diffs...";
            LoadingProgress = 10.0;
        });

        var processed = await Task.Run(() =>
        {
            var localProcessed = new List<ReviewModels.FileDiff>();
            _logger.LogDebug("Processing {Count} diffs", totalCount);
            
            for (int i = 0; i < diffList.Count; i++)
            {
                var d = diffList[i];
                var fileDiff = d;
                _logger.LogDebug("Processing diff for {Path}", fileDiff.FilePath);
                _logger.LogDebug("  Original OldText length: {Length}", fileDiff.OldText?.Length ?? 0);
                _logger.LogDebug("  Original NewText length: {Length}", fileDiff.NewText?.Length ?? 0);
                _logger.LogDebug("  Original Diff length: {Length}", fileDiff.Diff?.Length ?? 0);

                if (string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText) && !string.IsNullOrEmpty(fileDiff.Diff))
                {
                    _logger.LogDebug("  Creating placeholder content for {Path}", fileDiff.FilePath);
                    var (oldContent, newContent) = ParseDiffToContent(fileDiff.Diff);

                    bool isNewFile = oldContent.Contains("[FILE ADDED]");
                    bool isDeletedFile = newContent.Contains("[FILE DELETED]");

                    _logger.LogDebug("  Parsed diff content: isNewFile={IsNew}, isDeletedFile={IsDeleted}", isNewFile, isDeletedFile);
                    _logger.LogDebug("  oldContent length: {OldLength}, newContent length: {NewLength}", oldContent.Length, newContent.Length);

                    if (string.IsNullOrWhiteSpace(oldContent) && string.IsNullOrWhiteSpace(newContent))
                    {
                        if (fileDiff.Diff.Contains("new file mode") || fileDiff.Diff.Contains("/dev/null") && fileDiff.Diff.Contains("+++ b/"))
                        {
                            oldContent = "[FILE ADDED]\n";
                            var addedLines = new List<string>();
                            foreach (var line in fileDiff.Diff.Split('\n'))
                            {
                                if (line.StartsWith("+") && !line.StartsWith("+++ "))
                                {
                                    addedLines.Add(line.Substring(1));
                                }
                            }
                            newContent = string.Join("\n", addedLines);
                            if (string.IsNullOrWhiteSpace(newContent))
                            {
                                newContent = "[Added file content not available]\n";
                            }
                        }
                        else if (fileDiff.Diff.Contains("deleted file mode") || fileDiff.Diff.Contains("--- a/") && fileDiff.Diff.Contains("+++ /dev/null"))
                        {
                            var removedLines = new List<string>();
                            foreach (var line in fileDiff.Diff.Split('\n'))
                            {
                                if (line.StartsWith("-") && !line.StartsWith("--- "))
                                {
                                    removedLines.Add(line.Substring(1));
                                }
                            }
                            oldContent = string.Join("\n", removedLines);
                            if (string.IsNullOrWhiteSpace(oldContent))
                            {
                                oldContent = "[Deleted file content not available]\n";
                            }
                            newContent = "[FILE DELETED]\n";
                        }
                        else
                        {
                            oldContent = "[No original content could be extracted]\n";
                            newContent = "[No modified content could be extracted]\n";

                            if (!string.IsNullOrEmpty(fileDiff.Diff))
                            {
                                newContent = fileDiff.Diff;
                            }
                        }
                    }

                    string oldText = !string.IsNullOrEmpty(oldContent) ? oldContent : isNewFile ? "[FILE ADDED]\n" : "[Original content not available]";
                    string newText = !string.IsNullOrEmpty(newContent) ? newContent : isDeletedFile ? "[FILE DELETED]\n" : "[Modified content not available]";

                    if ((string.IsNullOrWhiteSpace(oldText) || oldText.StartsWith("[")) &&
                        (string.IsNullOrWhiteSpace(newText) || newText.StartsWith("[")) &&
                        !string.IsNullOrEmpty(fileDiff.Diff))
                    {
                        string diffHeader = "Showing raw diff content:\n\n";
                        oldText = "[Original content]\n" + diffHeader + fileDiff.Diff;
                        newText = "[Modified content]\n" + diffHeader + fileDiff.Diff;
                    }

                    if (!oldText.Contains('\n')) oldText += "\n \n ";
                    if (!newText.Contains('\n')) newText += "\n \n ";

                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        fileDiff.Diff,
                        oldText,
                        newText
                    );
                }
                else if (string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText))
                {
                    _logger.LogWarning("  WARNING: No content available for {Path}", fileDiff.FilePath);
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        "No diff content available.",
                        "[No original content available]\n \n ",
                        "[No new content available]\n \n "
                    );
                }
                else if (string.IsNullOrEmpty(fileDiff.OldText) && !string.IsNullOrEmpty(fileDiff.NewText))
                {
                    _logger.LogDebug("  New file detected: {Path}", fileDiff.FilePath);
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        fileDiff.Diff ?? string.Empty,
                        "[FILE ADDED]\n",
                        fileDiff.NewText
                    );
                }
                else if (!string.IsNullOrEmpty(fileDiff.OldText) && string.IsNullOrEmpty(fileDiff.NewText))
                {
                    _logger.LogDebug("  Deleted file detected: {Path}", fileDiff.FilePath);
                    fileDiff = new ReviewModels.FileDiff(
                        fileDiff.FilePath,
                        fileDiff.Diff ?? string.Empty,
                        fileDiff.OldText,
                        "[FILE DELETED]\n"
                    );
                }

                _logger.LogDebug("  Final OldText length: {Length}", fileDiff.OldText?.Length ?? 0);
                _logger.LogDebug("  Final NewText length: {Length}", fileDiff.NewText?.Length ?? 0);
                localProcessed.Add(fileDiff);

                // Update progress every few items
                if (totalCount > 0 && (i % Math.Max(1, totalCount / 10) == 0 || i == totalCount - 1))
                {
                    var progressPercent = 10.0 + (i + 1) * 80.0 / totalCount; // 10% to 90%
                    var status = $"Processing diff {i + 1}/{totalCount}...";
                    
                    Dispatcher.UIThread.Post(() =>
                    {
                        LoadingProgress = progressPercent;
                        LoadingStatus = status;
                    });
                }
            }
            return localProcessed;
        }).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            FileDiffs.Clear();
            foreach (var fd in processed)
                FileDiffs.Add(fd);
            
            // Mark loading as complete
            IsLoading = false;
            LoadingStatus = "Loading complete";
            LoadingProgress = 100.0;
        });
    }

    private async Task LoadThreadsAsync()
    {
        try
        {
            var threads = await _commentsService.GetThreadsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                PullRequest.Id,
                _settings.PersonalAccessToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Threads.Clear();
                this.RaisePropertyChanged(nameof(FilteredThreads));

                foreach (var t in threads)
                    Threads.Add(t);

                this.RaisePropertyChanged(nameof(FilteredThreads));
                this.RaisePropertyChanged(nameof(FilteredThreadsCount));
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load threads");
            // Ensure we still show the 
        }
    }

    // Helper method to parse a unified diff format into old and new content
            public (string oldContent, string newContent) ParseDiffToContent(string diffText)
            {
        if (string.IsNullOrEmpty(diffText))
            return (string.Empty, string.Empty);

        var oldContentLines = new List<string>();
        var newContentLines = new List<string>();

            // Check for new or deleted file indicators in the diff
            bool isNewFile = diffText.Contains("new file mode") || diffText.Contains("/dev/null") && diffText.Contains("+++ b/");
            bool isDeletedFile = diffText.Contains("deleted file mode") || diffText.Contains("--- a/") && diffText.Contains("+++ /dev/null");

            _logger.LogDebug("ParseDiffToContent - isNewFile: {IsNew}, isDeletedFile: {IsDeleted}", isNewFile, isDeletedFile);
            _logger.LogDebug("Diff text length: {Length} bytes", diffText.Length);

            // Log the first few lines of the diff for debugging
            var diffStart = string.Join("\n", diffText.Split('\n').Take(10));
            _logger.LogDebug("Diff starts with:\n{Start}", diffStart);

            // Count how many '+' and '-' lines we have
            int addedLineCount = diffText.Split('\n').Count(l => l.StartsWith("+") && !l.StartsWith("+++ "));
            int removedLineCount = diffText.Split('\n').Count(l => l.StartsWith("-") && !l.StartsWith("--- "));
            _logger.LogDebug("Diff contains {Added} added lines and {Removed} removed lines", addedLineCount, removedLineCount);

        // Skip header lines that start with "diff", "index", "---", "+++", etc.
        var lines = diffText.Split('\n');
        bool inContent = false;

        foreach (var line in lines)
        {
            // Skip diff metadata until we get to a hunk header
            if (!inContent)
            {
                if (line.StartsWith("@@ "))
                    inContent = true;
                continue;
            }

            if (line.StartsWith("+") && !line.StartsWith("+++ "))
            {
                // Added line - goes only to new content
                newContentLines.Add(line.Substring(1));
            }
            else if (line.StartsWith("-") && !line.StartsWith("--- "))
            {
                // Removed line - goes only to old content
                oldContentLines.Add(line.Substring(1));
            }
            else if (!line.StartsWith("@@ "))
            {
                // Context line - goes to both old and new content
                if (line.StartsWith(" "))
                {
                    oldContentLines.Add(line.Substring(1));
                    newContentLines.Add(line.Substring(1));
                }
                else
                {
                    oldContentLines.Add(line);
                    newContentLines.Add(line);
                }
            }
        }

        string oldContent = string.Join("\n", oldContentLines);
        string newContent = string.Join("\n", newContentLines);

        // Add file status indicators if needed
        if (isNewFile && string.IsNullOrWhiteSpace(oldContent))
        {
            oldContent = "[FILE ADDED]\n";
            _logger.LogDebug("Added [FILE ADDED] marker to oldContent");
        }
        else if (isDeletedFile && string.IsNullOrWhiteSpace(newContent))
        {
            newContent = "[FILE DELETED]\n";
            _logger.LogDebug("Added [FILE DELETED] marker to newContent");
        }

        // If we couldn't extract any content but have the raw diff, show the raw diff
        if (string.IsNullOrWhiteSpace(oldContent) && string.IsNullOrWhiteSpace(newContent) && !string.IsNullOrWhiteSpace(diffText))
        {
            _logger.LogDebug("Using raw diff as content since no content could be extracted");
            if (isNewFile)
            {
                oldContent = "[FILE ADDED]\n";
                newContent = diffText; // Show the raw diff in the new content area
            }
            else if (isDeletedFile)
            {
                oldContent = diffText; // Show the raw diff in the old content area
                newContent = "[FILE DELETED]\n";
            }
            else
            {
                // For modified files where we couldn't extract content properly
                oldContent = "[Original content not available]\n\nRaw diff:\n" + diffText;
                newContent = "[Modified content not available]\n\nRaw diff:\n" + diffText;
            }
        }

        // Ensure we have reasonable content lengths for display
        if (oldContent.Length < 5 && !isNewFile)
        {
            oldContent = "[No significant content available for original version]\n";
        }

        if (newContent.Length < 5 && !isDeletedFile)
        {
            newContent = "[No significant content available for new version]\n";
        }

        return (oldContent, newContent);
    }
}
