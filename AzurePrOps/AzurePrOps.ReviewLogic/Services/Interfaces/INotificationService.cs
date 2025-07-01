using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface INotificationService
{
    void Notify(string channel, Notification notification);
}