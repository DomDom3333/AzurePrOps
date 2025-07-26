using System.Reactive;
using ReactiveUI;
using AzurePrOps.Models;

namespace AzurePrOps.ViewModels;

public class DiffSettingsWindowViewModel : ViewModelBase
{
    private bool _ignoreWhitespace = DiffPreferences.IgnoreWhitespace;
    public bool IgnoreWhitespace
    {
        get => _ignoreWhitespace;
        set => this.RaiseAndSetIfChanged(ref _ignoreWhitespace, value);
    }

    private bool _wrapLines = DiffPreferences.WrapLines;
    public bool WrapLines
    {
        get => _wrapLines;
        set => this.RaiseAndSetIfChanged(ref _wrapLines, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public DiffSettingsWindowViewModel()
    {
        SaveCommand = ReactiveCommand.Create(() =>
        {
            DiffPreferences.IgnoreWhitespace = IgnoreWhitespace;
            DiffPreferences.WrapLines = WrapLines;
        });
        CloseCommand = ReactiveCommand.Create(() => { });
    }
}
