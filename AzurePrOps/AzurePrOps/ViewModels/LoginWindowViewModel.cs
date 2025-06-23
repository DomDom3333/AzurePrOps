using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using AzurePrOps.Models;

namespace AzurePrOps.ViewModels;

public class LoginWindowViewModel : ViewModelBase
{
    private string _organization = string.Empty;
    public string Organization
    {
        get => _organization;
        set => this.RaiseAndSetIfChanged(ref _organization, value);
    }

    private string _project = string.Empty;
    public string Project
    {
        get => _project;
        set => this.RaiseAndSetIfChanged(ref _project, value);
    }

    private string _repository = string.Empty;
    public string Repository
    {
        get => _repository;
        set => this.RaiseAndSetIfChanged(ref _repository, value);
    }

    private string _personalAccessToken = string.Empty;
    public string PersonalAccessToken
    {
        get => _personalAccessToken;
        set => this.RaiseAndSetIfChanged(ref _personalAccessToken, value);
    }

    private string _reviewerId = string.Empty;
    public string ReviewerId
    {
        get => _reviewerId;
        set => this.RaiseAndSetIfChanged(ref _reviewerId, value);
    }

    public ReactiveCommand<Unit, ConnectionSettings> ConnectCommand { get; }

    public LoginWindowViewModel()
    {
        ConnectCommand = ReactiveCommand.Create(() =>
            new ConnectionSettings(Organization, Project, Repository, PersonalAccessToken, ReviewerId));
    }
}
