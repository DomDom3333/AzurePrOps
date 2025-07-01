namespace AzurePrOps.Models;

public record ConnectionSettings(
    string Organization,
    string Project,
    string Repository,
    string PersonalAccessToken,
    string ReviewerId,
    string EditorCommand = "code",
    bool UseGitDiff = false);
