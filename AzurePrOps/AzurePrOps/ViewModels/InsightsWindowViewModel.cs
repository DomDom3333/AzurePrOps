using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Reactive;
using ReactiveUI;
using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ViewModels;

public class InsightsWindowViewModel : ViewModelBase
{
    public string Title { get; }
    public ObservableCollection<MetricData> Metrics { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public InsightsWindowViewModel(string title, IEnumerable<MetricData> metrics)
    {
        Title = title;
        Metrics = new ObservableCollection<MetricData>(metrics);
        CloseCommand = ReactiveCommand.Create(() => { });
    }
}
