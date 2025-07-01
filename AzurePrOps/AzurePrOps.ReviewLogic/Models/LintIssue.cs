namespace AzurePrOps.ReviewLogic.Models;

public class LintIssue
{
    public int LineNumber { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}
