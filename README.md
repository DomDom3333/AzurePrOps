# AzurePrOps

A minimal Avalonia UI tool for interacting with Azure DevOps pull requests.

Upon launch, the application now prompts for your organization, project, repository, reviewer id and personal access token (PAT). These values are stored only for the current session and remove the need to edit the source code when connecting to different projects.

## Performance

Git operations now use partial clone options (`--filter=blob:none`) and shallow fetches. Cloned repositories are cached under the system's temporary directory so subsequent runs only require a quick `git fetch`. This drastically reduces network traffic and speeds up retrieving pull request diffs for large projects.

The settings window also lets you choose which external editor to open files with. Available options are detected based on the editors found on your system.

Make sure your chosen editor is available on the system `PATH`. The application launches the editor using its command name (e.g. `code` for Visual Studio Code), so the command must be discoverable without specifying a full path.

Opening files works across Linux and Windows for many popular IDEs such as VS Code, JetBrains Rider, Sublime Text and Notepad++. The launch arguments are adjusted automatically for each supported editor so that the specified line number is focused when possible.

## Inline Comments

Enable **Inline comments** from the Settings window to try inline commenting while reviewing diffs. Your Personal Access Token must include the **Code (Read & Write)** scope for posting replies.

When viewing a diff with the feature enabled, click in the gutter beside a line to open a popup showing existing threads. Enter text in the box to add a new comment or reply.

Each comment now shows the author and timestamp and includes a **Resolve** button that toggles the thread's status.
The sidebar lists all threads and can filter to only unresolved items.
