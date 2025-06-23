using ReactiveUI;
using System.Reactive;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Services;

namespace AzurePrOps.ViewModels;

public class LoginWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    private string _personalAccessToken = string.Empty;
    public string PersonalAccessToken
    {
        get => _personalAccessToken;
        set => this.RaiseAndSetIfChanged(ref _personalAccessToken, value);
    }

    public ReactiveCommand<Unit, LoginInfo> LoginCommand { get; }

    public LoginWindowViewModel()
    {
        if (ConnectionSettingsStorage.TryLoad(out var loaded))
        {
            PersonalAccessToken = loaded!.PersonalAccessToken;
        }

        LoginCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var reviewerId = await _client.GetUserIdAsync(PersonalAccessToken);
            return new LoginInfo(reviewerId, PersonalAccessToken);
        });
    }
}
