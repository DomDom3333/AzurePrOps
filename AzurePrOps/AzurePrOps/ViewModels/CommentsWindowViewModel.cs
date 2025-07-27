using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using AzurePrOps.AzureConnection.Models;
using ReactiveUI;

namespace AzurePrOps.ViewModels;

public class CommentsWindowViewModel : ViewModelBase
{
    public string Title { get; }
    public ObservableCollection<PullRequestComment> Comments { get; }
    public int CommentCount => Comments.Count;
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public CommentsWindowViewModel(string title, IEnumerable<PullRequestComment> comments)
    {
        Title = title;
        Comments = new ObservableCollection<PullRequestComment>(
            comments.OrderByDescending(c => c.PostedDate));
        Comments.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(CommentCount));

        CloseCommand = ReactiveCommand.Create(() => { });
    }
}
