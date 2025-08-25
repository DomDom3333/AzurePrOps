using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AzurePrOps.ViewModels;

namespace AzurePrOps.Views;

public partial class GroupSelectionWindow : Window
{
    public GroupSelectionWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Wire up the command handlers to close the dialog
        if (DataContext is GroupSelectionViewModel viewModel)
        {
            viewModel.ConfirmCommand.Subscribe(_ => Close(true));
            viewModel.CancelCommand.Subscribe(_ => Close(false));
        }
    }
}
