using AzurePrOps.ReviewLogic.Models;
using LibGit2Sharp;

namespace AzurePrOps.ReviewLogic.Services;

public class GitBlameService : IBlameService
{
    private readonly string _repoRoot;
    public GitBlameService(string repositoryPath) => _repoRoot = repositoryPath;

    public BlameInfo GetBlame(string filePath, int lineNumber)
    {
        using Repository repo = new Repository(_repoRoot);
        BlameHunkCollection? blame = repo.Blame(filePath);
        LibGit2Sharp.BlameHunk? hunk = blame.FirstOrDefault(h =>
            lineNumber >= h.FinalStartLineNumber &&
            lineNumber < h.FinalStartLineNumber + h.LineCount);

        if (hunk == null)
            return new BlameInfo { LineNumber = lineNumber, Author = "Unknown", Date = DateTime.MinValue };

        Commit? commit = hunk.FinalCommit;
        return new BlameInfo
        {
            LineNumber = lineNumber,
            Author     = commit.Author.Name,
            Date       = commit.Author.When.DateTime
        };
    }
}