using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class SimpleMetricsService : IMetricsService
{
    public IEnumerable<MetricData> GetMetrics(string repository) => new[]
    {
        new MetricData { Name = "Comments",     Value = 5 },
        new MetricData { Name = "LintWarnings", Value = 2 },
        new MetricData { Name = "ReviewTimeMin",Value = 12.5 }
    };
}