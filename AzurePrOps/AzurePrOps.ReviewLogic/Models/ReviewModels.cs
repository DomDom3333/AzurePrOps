namespace AzurePrOps.ReviewLogic.Models
{
    // Comment model for code reviews
    public class ReviewComment
    {
        public int Id { get; set; }
        public string Author { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Text { get; set; } = string.Empty;
        public ReviewComment[] Replies { get; set; } = Array.Empty<ReviewComment>();
    }

    // Linting issues (warnings, errors) model
    public class LintIssue
    {
        public int LineNumber { get; set; }
        public string RuleId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
    }

    // Git blame information model
    public class BlameInfo
    {
        public int LineNumber { get; set; }
        public string Author { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    // Audit trail record model
    public class AuditRecord
    {
        public string FilePath { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Details { get; set; } = string.Empty;
    }

    // Notification model
    public class Notification
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; } = NotificationType.Info;
    }

    // Notification type enum
    public enum NotificationType
    {
        Info,
        Warning,
        Error,
        Success
    }

    // Metrics data model
    public class MetricData
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    // AI suggestion model
    public class Suggestion
    {
        public int LineNumber { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public SuggestionType Type { get; set; } = SuggestionType.Improvement;
    }

    // Suggestion type enum
    public enum SuggestionType
    {
        Improvement,
        BestPractice,
        Performance,
        Security
    }

    // Patch result model
    public class PatchResult
    {
        public bool Success { get; set; }
        public string[] Messages { get; set; } = Array.Empty<string>();
    }

    // Code folding region model
    public class FoldRegion
    {
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    // Search result model
    public class SearchResult
    {
        public int LineNumber { get; set; }
        public string Context { get; set; } = string.Empty;
    }
}
