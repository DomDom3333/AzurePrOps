using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface IMetricsService
{
    IEnumerable<MetricData> GetMetrics(string repository);
}