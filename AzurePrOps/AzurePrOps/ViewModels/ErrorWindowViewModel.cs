using System.Reactive;
using ReactiveUI;

namespace AzurePrOps.ViewModels;

public class ErrorWindowViewModel : ViewModelBase
{
    private string _errorMessage = string.Empty;

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ErrorWindowViewModel()
    {
        CloseCommand = ReactiveCommand.Create(() => { });
    }
}
