using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface ISuggestionService
{
    IEnumerable<Suggestion> GetAIHints(string code);
}