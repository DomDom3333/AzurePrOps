using Avalonia.Controls;
using Avalonia.ReactiveUI;
using AzurePrOps.ViewModels;

namespace AzurePrOps.Views;

public partial class LoginWindow : ReactiveWindow<LoginWindowViewModel>
{
    public LoginWindow()
    {
        InitializeComponent();
    }
}
