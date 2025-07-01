namespace AzurePrOps.ReviewLogic.Models;

public class PatchResult
{
    public bool Success { get; set; }
    public string[] Messages { get; set; } = Array.Empty<string>();
}
