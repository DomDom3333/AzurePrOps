using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class FileAuditTrailService : IAuditTrailService
{
    private readonly Dictionary<string, List<AuditRecord>> _history 
        = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<AuditRecord> GetHistory(string filePath)
        => _history.TryGetValue(filePath, out List<AuditRecord>? list) ? list : Enumerable.Empty<AuditRecord>();

    public void RecordAction(AuditRecord record)
    {
        if (!_history.ContainsKey(record.FilePath))
            _history[record.FilePath] = new List<AuditRecord>();
        _history[record.FilePath].Add(record);
    }
}