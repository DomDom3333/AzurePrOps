using System.Diagnostics;

namespace AzurePrOps.ReviewLogic.Services;

public class IDEIntegrationService : IIDEIntegrationService
{
    private readonly string _editorCommand;

    public IDEIntegrationService(string editorCommand)
    {
        _editorCommand = editorCommand;
    }

    public void OpenInIDE(string filePath, int lineNumber)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = _editorCommand,
            Arguments       = $"-g \"{filePath}\":{lineNumber}",
            UseShellExecute = true
        });
    }
}