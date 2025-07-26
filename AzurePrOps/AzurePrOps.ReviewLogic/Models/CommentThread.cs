namespace AzurePrOps.ReviewLogic.Models;

public class CommentThread
{
    public int ThreadId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public IList<Comment> Comments { get; set; } = new List<Comment>();
}
