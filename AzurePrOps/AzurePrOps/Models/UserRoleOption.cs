namespace AzurePrOps.Models;

/// <summary>
/// Represents a user role option for display in the settings UI
/// </summary>
public class UserRoleOption
{
    public UserRole Role { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public UserRoleOption(UserRole role)
    {
        Role = role;
        DisplayName = role.GetDisplayName();
        Description = role.GetDescription();
    }

    public override string ToString() => DisplayName;
}
