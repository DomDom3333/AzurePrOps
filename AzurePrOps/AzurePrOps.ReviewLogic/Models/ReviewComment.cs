namespace AzurePrOps.ReviewLogic.Models;

public class ReviewComment
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public ReviewComment[] Replies { get; set; } = Array.Empty<ReviewComment>();
}
