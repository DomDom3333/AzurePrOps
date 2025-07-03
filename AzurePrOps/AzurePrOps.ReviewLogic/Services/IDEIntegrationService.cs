using System.Diagnostics;
using System.IO;

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
        var args = GetArguments(_editorCommand, filePath, lineNumber);

        Process.Start(new ProcessStartInfo
        {
            FileName        = _editorCommand,
            Arguments       = args,
            UseShellExecute = false
        });
    }

    private static string GetArguments(string editor, string filePath, int line)
    {
        string cmd = Path.GetFileNameWithoutExtension(editor).ToLowerInvariant();

        return cmd switch
        {
            "code" or "code-insiders" => $"-g \"{filePath}\":{line}",
            "subl"                  => $"\"{filePath}\":{line}",
            "rider" or "rider64" or "idea" or "idea64" or "studio" or "studio64" => $"\"{filePath}\" --line {line}",
            "notepad++"             => $"-n{line} \"{filePath}\"",
            "gedit" or "vim" or "vi" or "nano" or "emacs" => $"+{line} \"{filePath}\"",
            "devenv"               => $"/Edit \"{filePath}\" /Command \"Edit.Goto {line}\"",
            _                       => $"\"{filePath}\""
        };
    }
}