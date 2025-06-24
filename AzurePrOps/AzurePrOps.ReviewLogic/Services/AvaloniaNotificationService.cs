using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class AvaloniaNotificationService : INotificationService
{
    public void Notify(string channel, Notification notification)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Window win = new Window
            {
                Title = notification.Title,
                Width = 300,
                Height = 120,
                Content = new TextBlock
                {
                    Text = notification.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(10)
                }
            };
            win.Show();
        });
    }
}