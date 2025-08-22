namespace AzurePrOps.AzureConnection.Models;

public record ReviewerInfo(string Id, string DisplayName, string Vote, bool IsGroup = false);
