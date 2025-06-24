using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class SimpleSuggestionService : ISuggestionService
{
    public IEnumerable<Suggestion> GetAIHints(string code)
        => Enumerable.Empty<Suggestion>();
}