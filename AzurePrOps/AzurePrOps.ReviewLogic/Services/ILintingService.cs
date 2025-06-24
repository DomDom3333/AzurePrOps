using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface ILintingService
{
    IEnumerable<LintIssue> Analyze(string code);
}