using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface ICodeFoldingService
{
    IEnumerable<FoldRegion> GetFoldRegions(string code);
}