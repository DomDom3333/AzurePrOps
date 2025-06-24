namespace AzurePrOps.ReviewLogic.Services;

public interface IIDEIntegrationService
{
    void OpenInIDE(string filePath, int lineNumber);
}