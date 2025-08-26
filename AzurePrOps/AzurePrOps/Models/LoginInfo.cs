namespace AzurePrOps.Models;

public record LoginInfo(string ReviewerId)
{
    /// <summary>
    /// Gets the Personal Access Token from secure storage
    /// </summary>
    public string? GetPersonalAccessToken()
    {
        return ConnectionSettingsStorage.GetPersonalAccessToken();
    }
    
    /// <summary>
    /// Indicates whether a Personal Access Token is available in secure storage
    /// </summary>
    public bool HasPersonalAccessToken => !string.IsNullOrEmpty(GetPersonalAccessToken());
}
