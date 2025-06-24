using System.Diagnostics;

namespace AzurePrOps.ReviewLogic.Services;

public class IDEIntegrationService : IIDEIntegrationService
{
    public void OpenInIDE(string filePath, int lineNumber)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = "code",
            Arguments       = $"-g \"{filePath}\":{lineNumber}",
            UseShellExecute = true
        });
    }
}