using System;
using Avalonia.Controls;
using AzurePrOps.ViewModels;
using System.Reactive.Linq;

namespace AzurePrOps.Views;

public partial class InsightsWindow : Window
{
    public InsightsWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is InsightsWindowViewModel vm)
        {
            vm.CloseCommand.Subscribe(_ => Close());
        }
    }
}
