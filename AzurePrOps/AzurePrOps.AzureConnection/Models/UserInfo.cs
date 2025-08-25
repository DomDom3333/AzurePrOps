namespace AzurePrOps.AzureConnection.Models;

public record UserInfo(
    string Id,
    string DisplayName,
    string Email,
    string UniqueName)
{
    public static UserInfo Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
}
