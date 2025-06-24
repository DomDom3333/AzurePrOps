namespace AzurePrOps.ReviewLogic.Services
{
    public enum PullRequestServiceType
    {
        Git,       // Local Git repository
        AzureDevOps // Azure DevOps API
        // Add more types as needed (GitHub, GitLab, etc.)
    }

    public class PullRequestServiceFactory
    {
        public static IPullRequestService Create(PullRequestServiceType serviceType, HttpClient? httpClient = null)
        {
            return serviceType switch
            {
                PullRequestServiceType.Git => new GitPullRequestService(),
                PullRequestServiceType.AzureDevOps => httpClient != null 
                    ? new AzureDevOpsPullRequestService(httpClient) 
                    : new AzureDevOpsPullRequestService(),
                _ => throw new ArgumentException($"Unsupported service type: {serviceType}")
            };
        }
    }
}
