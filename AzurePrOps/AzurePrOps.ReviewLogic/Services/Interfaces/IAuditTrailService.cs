using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface IAuditTrailService
{
    IEnumerable<AuditRecord> GetHistory(string filePath);
    void RecordAction(AuditRecord record);
}