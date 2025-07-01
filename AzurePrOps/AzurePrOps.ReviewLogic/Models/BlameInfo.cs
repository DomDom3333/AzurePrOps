namespace AzurePrOps.ReviewLogic.Models;

public class BlameInfo
{
    public int LineNumber { get; set; }
    public string Author { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
