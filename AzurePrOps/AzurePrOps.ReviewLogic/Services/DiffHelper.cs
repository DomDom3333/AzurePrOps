using System;
using System.Diagnostics;
using System.IO;

namespace AzurePrOps.ReviewLogic.Services
{
    internal static class DiffHelper
    {
        public static string GenerateUnifiedDiff(string filePath, string oldContent, string newContent, string changeType)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            string oldFile = Path.Combine(tempDir, "old");
            string newFile = Path.Combine(tempDir, "new");

            if (changeType.Equals("delete", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(oldFile, oldContent);
                File.WriteAllText(newFile, string.Empty);
            }
            else if (changeType.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(oldFile, string.Empty);
                File.WriteAllText(newFile, newContent);
            }
            else
            {
                File.WriteAllText(oldFile, oldContent);
                File.WriteAllText(newFile, newContent);
            }

            string arguments = changeType.Equals("add", StringComparison.OrdinalIgnoreCase)
                ? $"diff --no-index /dev/null \"{newFile}\""
                : changeType.Equals("delete", StringComparison.OrdinalIgnoreCase)
                    ? $"diff --no-index \"{oldFile}\" /dev/null"
                    : $"diff --no-index \"{oldFile}\" \"{newFile}\"";

            var psi = new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            string diff = process!.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Directory.Delete(tempDir, true);

            diff = diff.Replace(oldFile, $"a/{filePath}")
                       .Replace(newFile, $"b/{filePath}");
            return diff;
        }
    }
}
