// File: CodeReviewServices.cs

using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services
{
    // 1) In-memory comment store
    public class InMemoryCommentProvider : ICommentProvider
    {
        private readonly Dictionary<string, List<ReviewComment>> _comments 
            = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<ReviewComment> GetComments(string filePath, int lineNumber)
        {
            if (_comments.TryGetValue(filePath, out List<ReviewComment>? list))
                return list.Where(c => c.LineNumber == lineNumber);
            return Enumerable.Empty<ReviewComment>();
        }

        public void AddComment(string filePath, int lineNumber, string author, string text)
        {
            if (!_comments.ContainsKey(filePath))
                _comments[filePath] = new List<ReviewComment>();

            ReviewComment comment = new ReviewComment
            {
                Id = _comments[filePath].Count + 1,
                Author = author,
                LineNumber = lineNumber,
                Text = text,
                Replies = Array.Empty<ReviewComment>()
            };
            _comments[filePath].Add(comment);
        }
    }
}
