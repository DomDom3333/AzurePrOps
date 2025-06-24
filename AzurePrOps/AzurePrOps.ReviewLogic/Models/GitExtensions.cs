using LibGit2Sharp;

namespace AzurePrOps.ReviewLogic.Models
{
    public static class GitExtensions
    {
        /// <summary>
        /// Gets the content text from a Git blob
        /// </summary>
        public static string GetContentText(this Blob blob)
        {
            using Stream? contentStream = blob.GetContentStream();
            using StreamReader reader = new System.IO.StreamReader(contentStream);
            return reader.ReadToEnd();
        }
    }
}
