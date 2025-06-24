using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class SimpleSearchService : ISearchService
{
    public IEnumerable<SearchResult> Search(string query, string code)
    {
        string[] lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                yield return new SearchResult { LineNumber = i + 1, Context = lines[i].Trim() };
        }
    }
}