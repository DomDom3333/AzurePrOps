using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface ICommentProvider
{
    IEnumerable<ReviewComment> GetComments(string filePath, int lineNumber);
    void AddComment(string filePath, int lineNumber, string author, string text);
}