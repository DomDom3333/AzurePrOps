namespace AzurePrOps.ReviewLogic.Models;

public class Suggestion
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public SuggestionType Type { get; set; } = SuggestionType.Improvement;
}
