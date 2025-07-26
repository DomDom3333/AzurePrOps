namespace AzurePrOps.ReviewLogic.Models;

public class Comment
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
}
