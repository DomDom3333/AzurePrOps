using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface IPatchService
{
    PatchResult ApplyPatch(string filePath, string patch);
    void AcceptChange(int changeId);
    void RejectChange(int changeId);
    byte[] DownloadDiff(string filePath);
}