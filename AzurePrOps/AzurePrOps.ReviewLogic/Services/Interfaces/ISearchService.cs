using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface ISearchService
{
    IEnumerable<SearchResult> Search(string query, string code);
}