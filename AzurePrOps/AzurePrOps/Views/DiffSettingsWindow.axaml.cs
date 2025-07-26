using System;
using Avalonia.Controls;
using AzurePrOps.ViewModels;
using System.Reactive.Linq;

namespace AzurePrOps.Views;

public partial class DiffSettingsWindow : Window
{
    public DiffSettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is DiffSettingsWindowViewModel vm)
        {
            vm.SaveCommand.Subscribe(_ => Close());
            vm.CloseCommand.Subscribe(_ => Close());
        }
    }
}
