using ReactiveUI;

namespace AzurePrOps.ViewModels;

public class FileDiffListItemViewModel : ReactiveObject
{
    public FileDiffListItemViewModel(string filePath, string diff)
    {
        FilePath = filePath;
        Diff = diff;
    }

    public string FilePath { get; }
    public string Diff { get; }

    private string _oldText = string.Empty;
    public string OldText
    {
        get => _oldText;
        set => this.RaiseAndSetIfChanged(ref _oldText, value);
    }

    private string _newText = string.Empty;
    public string NewText
    {
        get => _newText;
        set => this.RaiseAndSetIfChanged(ref _newText, value);
    }

    private bool _isContentLoaded;
    public bool IsContentLoaded
    {
        get => _isContentLoaded;
        set => this.RaiseAndSetIfChanged(ref _isContentLoaded, value);
    }
}
