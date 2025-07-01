namespace AzurePrOps.ReviewLogic.Models;

public class AuditRecord
{
    public string FilePath { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Details { get; set; } = string.Empty;
}
