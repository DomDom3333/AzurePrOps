using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AzurePrOps.ViewModels;
using System.Reactive.Linq;

namespace AzurePrOps.Views;

public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ErrorWindowViewModel vm)
        {
            vm.CloseCommand.Subscribe(_ => Close());
        }
    }
}
