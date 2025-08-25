using ReactiveUI;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;

namespace AzurePrOps.ViewModels;

public class GroupSelectionViewModel : ViewModelBase
{
    public ObservableCollection<GroupSelectionItem> Groups { get; } = new();
    
    public IEnumerable<string> SelectedGroups => Groups.Where(g => g.IsSelected).Select(g => g.Name);
    
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public GroupSelectionViewModel(IEnumerable<string> availableGroups, IEnumerable<string> selectedGroups)
    {
        var selectedSet = selectedGroups.ToHashSet();
        
        foreach (var group in availableGroups.OrderBy(g => g))
        {
            Groups.Add(new GroupSelectionItem 
            { 
                Name = group, 
                IsSelected = selectedSet.Contains(group) 
            });
        }

        SelectAllCommand = ReactiveCommand.Create(() =>
        {
            foreach (var group in Groups)
            {
                group.IsSelected = true;
            }
        });

        SelectNoneCommand = ReactiveCommand.Create(() =>
        {
            foreach (var group in Groups)
            {
                group.IsSelected = false;
            }
        });

        ConfirmCommand = ReactiveCommand.Create(() => { });
        CancelCommand = ReactiveCommand.Create(() => { });
    }
}

public class GroupSelectionItem : ReactiveObject
{
    private bool _isSelected;
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
