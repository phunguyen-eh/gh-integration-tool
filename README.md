# GitHub Integration Tool

A C# console tool that automates the process of creating a single integration pull request (PR) from a list of sub-PRs. The tool leverages the GitHub CLI (`gh`) for all Git operations and provides comprehensive state management for handling merge conflicts.

## Features

- **Automated Integration**: Creates integration PRs from multiple sub-PRs
- **Conflict Resolution**: Gracefully handles merge conflicts with resume capability
- **State Management**: Saves progress and allows resuming after conflict resolution
- **Validation**: Validates PR numbers and states before processing
- **Non-fast-forward Merges**: Preserves clear history of merged PRs
- **JIRA Ticket Extraction**: Automatically extracts and displays JIRA tickets from PR descriptions
- **Author Grouping**: Groups PRs by author in the final description
- **Template Support**: Uses customizable PR description templates
- **Existing PR Detection**: Detects and updates existing integration PRs

## Prerequisites

1. **GitHub CLI**: Must be installed and authenticated
   ```bash
   # Install GitHub CLI
   # Windows: winget install GitHub.cli
   # macOS: brew install gh
   # Linux: See https://github.com/cli/cli#installation

   # Authenticate
   gh auth login
   ```

2. **.NET 8.0**: Required to run the tool
   ```bash
   # Check if .NET 8.0 is installed
   dotnet --version
   ```

## Installation

1. Clone or download this repository
2. Navigate to the project directory
3. Build the project:
   ```bash
   dotnet build
   ```

## Usage

### Configuration File

Create a JSON configuration file with the following structure:

```json
{
  "team": "eni",  //Team name associated with the release
  "repoDirectory": "D:\\keypay-dev", //Path to the Git repository directory
  "pullRequestTemplate": "pull_request_template.md", //Path to the PR description template file
  "mainBranch": "staging", //The main branch to create the integration PR against (default: "main")
  "pushToOrigin": true, //Whether to push to origin and create a PR (default: false)
  "title": "Enigma release in 11 Aug, AU PM", //Custom title for the integration PR (optional, defaults to releaseName)
  "releaseName": "release/eni-20250811-au-pm", //Name for the new integration branch and PR title
  "pr": [ //Array of PR numbers to merge
    20153,
    20362
  ]
}
```

### PR Description Template

Create a template file (e.g., `pull_request_template.md`) with the following structure:

```markdown
## Summary:

{0}

## I Assert That:
- [x] The code meets the standards published in the [Payroll code review policies](https://employmenthero.atlassian.net/wiki/spaces/PAYR/pages/3636625582/Code+review+policies)
```

The `{0}` placeholder will be replaced with the generated PR description containing:
- Grouped PRs by author
- JIRA ticket extraction from PR titles and descriptions
- Formatted list of all included PRs

### Commands

#### Start Integration Process

```bash
gh-integration-tool --config config.json
```

This command will:
1. Read and validate the configuration file (By default it will pick `config.json` file in execution folder)
2. Validate all PR numbers and their states
3. Create a new branch from the main branch
4. Sequentially merge each PR using non-fast-forward merges
5. Create an integration pull request (if pushToOrigin is true)
6. Clean up temporary files

#### Continue After Conflict Resolution

```bash
gh-integration-tool --continue
```

Use this command after manually resolving merge conflicts to continue the integration process from where it left off.

## Workflow

1. **Initial Setup**: Create a configuration file with your team, repository path, and PR numbers
2. **Start Integration**: Run the tool with `--config` flag
3. **Handle Conflicts**: If merge conflicts occur, resolve them manually in your Git client
4. **Continue**: Run the tool with `--continue` flag to resume
5. **Review**: The tool creates an integration PR that you can review

## Example Workflow

```bash
# 1. Create configuration file
echo '{
  "team": "frontend",
  "repoDirectory": "C:\\my-project",
  "pullRequestTemplate": "pull_request_template.md",
  "mainBranch": "main",
  "pushToOrigin": true,
  "title": "Frontend Release v2.1.0",
  "releaseName": "release/frontend-v2.1.0",
  "pr": [123, 124, 125]
}' > release-config.json

# 2. Create PR template
echo '## Summary:

{0}

## I Assert That:
- [x] The code meets our standards' > pull_request_template.md

# 3. Start integration
gh-integration-tool --config release-config.json

# 4. If conflicts occur, resolve them manually, then continue
gh-integration-tool --continue
```

## Generated PR Description

The tool automatically generates a comprehensive PR description that includes:

- **Author Grouping**: PRs are grouped by author with `@username` mentions
- **JIRA Ticket Extraction**: Automatically extracts JIRA tickets (e.g., ENI-1542, PROJ-123) from PR titles and descriptions
- **Formatted List**: Each PR shows its number and associated JIRA tickets

Example output:
```
@john.doe
- #20153: ENI-1542, ENI-1543
- #20362: ENI-1545

@jane.smith
- #20154: PROJ-123
```

## Error Handling

The tool provides comprehensive error handling:

- **Invalid Configuration**: Validates JSON structure and required fields
- **PR Validation**: Checks if PRs exist and are in valid states (must be OPEN)
- **Repository Validation**: Ensures the specified directory is a valid Git repository
- **Merge Conflicts**: Pauses execution and provides clear instructions for manual resolution
- **State Persistence**: Saves progress to resume after conflict resolution

## State Management

The tool creates a temporary state file (`.integration-state.json`) during execution to track progress. This file contains:

- Configuration data
- Current PR index being processed
- Timestamp of the session

The state file is automatically cleaned up when the integration completes successfully.

## Branch Management

The tool intelligently handles branch creation:

- **New Branch**: Creates a new branch from the main branch if it doesn't exist
- **Existing Local Branch**: Checks out and updates the existing local branch
- **Existing Remote Branch**: Creates a local tracking branch from the remote branch
- **Conflict Prevention**: Ensures the working directory is clean before starting

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Running in Debug Mode

```bash
dotnet run -- --config config.json
```

### Project Structure

```
gh-integration-tool/
├── Models/
│   ├── Configuration.cs    # Configuration model
│   └── PullRequest.cs     # PR data model
├── Logs/
│   ├── FileLogger.cs      # Logging implementation
│   └── ILogger.cs         # Logging interface
├── IntegrationTool.cs     # Main tool logic
├── Utils.cs              # Utility functions
├── Program.cs            # Entry point
└── config.json           # Example configuration
```

## Troubleshooting

### Common Issues

1. **GitHub CLI not authenticated**
   ```bash
   gh auth login
   ```

2. **Invalid PR numbers**
   - Ensure PRs exist and are accessible
   - Check PR states (must be OPEN)
   - Verify you have access to the repository

3. **Repository directory issues**
   - Ensure the path is correct and accessible
   - Verify it's a valid Git repository
   - Check that you have write permissions

4. **Merge conflicts**
   - Resolve conflicts manually in your Git client
   - Run `gh-integration-tool --continue` to resume
   - The tool will continue from the exact point where it stopped

5. **Permission issues**
   - Ensure you have write access to the repository
   - Check GitHub CLI permissions
   - Verify you can create branches and PRs

6. **State file conflicts**
   - If a state file exists, either delete it or use `--continue`
   - The tool prevents starting a new integration while one is in progress

### Logging

The tool provides comprehensive logging to help with debugging:

- Logs are written to `Logs/logs-{date}.log`
- Includes detailed information about each step
- Captures command outputs and errors
- Session tracking with unique IDs

## Note

Feel free to add more features for the tool