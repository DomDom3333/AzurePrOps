using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface IBlameService
{
    BlameInfo GetBlame(string filePath, int lineNumber);
}