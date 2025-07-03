# AzurePrOps

A minimal Avalonia UI tool for interacting with Azure DevOps pull requests.

Upon launch, the application now prompts for your organization, project, repository, reviewer id and personal access token (PAT). These values are stored only for the current session and remove the need to edit the source code when connecting to different projects.

## Performance

Git operations now use partial clone options (`--filter=blob:none`) and shallow fetches. Cloned repositories are cached under the system's temporary directory so subsequent runs only require a quick `git fetch`. This drastically reduces network traffic and speeds up retrieving pull request diffs for large projects.
