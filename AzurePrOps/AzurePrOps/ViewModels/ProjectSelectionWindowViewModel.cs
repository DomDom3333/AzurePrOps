using ReactiveUI;
using System.Collections.ObjectModel;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using System.Reactive;
using System.Linq;
using System.Threading.Tasks;

namespace AzurePrOps.ViewModels;

public class ProjectSelectionWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly string _personalAccessToken;
    private readonly string _reviewerId;

    public ObservableCollection<NamedItem> Organizations { get; } = new();
    public ObservableCollection<NamedItem> Projects { get; } = new();
    public ObservableCollection<NamedItem> Repositories { get; } = new();

    private NamedItem? _selectedOrganization;
    public NamedItem? SelectedOrganization
    {
        get => _selectedOrganization;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedOrganization, value);
            _ = LoadProjectsAsync();
        }
    }

    private NamedItem? _selectedProject;
    public NamedItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedProject, value);
            _ = LoadRepositoriesAsync();
        }
    }

    private NamedItem? _selectedRepository;
    public NamedItem? SelectedRepository
    {
        get => _selectedRepository;
        set => this.RaiseAndSetIfChanged(ref _selectedRepository, value);
    }

    public ReactiveCommand<Unit, ConnectionSettings> ConnectCommand { get; }

    public ProjectSelectionWindowViewModel(string personalAccessToken, string reviewerId)
    {
        _personalAccessToken = personalAccessToken;
        _reviewerId = reviewerId;

        ConnectCommand = ReactiveCommand.Create(() =>
        {
            var settings = new ConnectionSettings(
                SelectedOrganization?.Name ?? string.Empty,
                SelectedProject?.Name ?? string.Empty,
                SelectedRepository?.Id ?? string.Empty,
                _personalAccessToken,
                _reviewerId);
            ConnectionSettingsStorage.Save(settings);
            return settings;
        });
    }

    public async Task LoadAsync()
    {
        Organizations.Clear();
        var userId = _reviewerId;
        var orgs = await _client.GetOrganizationsAsync(userId, _personalAccessToken);
        foreach (var o in orgs)
            Organizations.Add(o);
        SelectedOrganization = Organizations.FirstOrDefault();
    }

    private async Task LoadProjectsAsync()
    {
        Projects.Clear();
        Repositories.Clear();
        if (SelectedOrganization == null)
            return;

        var projects = await _client.GetProjectsAsync(SelectedOrganization.Name, _personalAccessToken);
        foreach (var p in projects)
            Projects.Add(p);
        SelectedProject = Projects.FirstOrDefault();
    }

    private async Task LoadRepositoriesAsync()
    {
        Repositories.Clear();
        if (SelectedOrganization == null || SelectedProject == null)
            return;

        var repos = await _client.GetRepositoriesAsync(SelectedOrganization.Name, SelectedProject.Name, _personalAccessToken);
        foreach (var r in repos)
            Repositories.Add(r);
        SelectedRepository = Repositories.FirstOrDefault();
    }
}
