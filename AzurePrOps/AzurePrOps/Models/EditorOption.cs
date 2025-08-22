namespace AzurePrOps.Models;

/// <summary>
/// Represents an external editor option with display name and command
/// </summary>
public class EditorOption
{
    public string DisplayName { get; }
    public string Command { get; }

    public EditorOption(string displayName, string command)
    {
        DisplayName = displayName;
        Command = command;
    }

    public override string ToString() => DisplayName;
}