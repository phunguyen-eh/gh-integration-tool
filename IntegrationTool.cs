using System.CommandLine;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using GhIntegrationTool.Models;

namespace GhIntegrationTool;

public class IntegrationTool
{
    private const string StateFileName = ".integration-state.json";
    private string? repositoryDirectory;
    private readonly ILogger logger;

    private string defaultPrDescriptionTemplate = @"
## Summary:

{0}

## I Assert That:
- [x] The code meets the standards published in the [Payroll code review policies](https://employmenthero.atlassian.net/wiki/spaces/PAYR/pages/3636625582/Code+review+policies)
";

    private readonly FileInfo defaultConfigFile = new("config.json");

    public IntegrationTool()
    {
        logger = new FileLogger();
    }

    public async Task<int> RunAsync(string[] args)
    {
        logger.LogInformation("Starting GitHub Integration Tool", new { Args = args });
        
        var configOption = new Option<FileInfo?>(
            "--config",
            () => defaultConfigFile,
            "Path to the JSON configuration file");

        var continueOption = new Option<bool>(
            "--continue",
            "Continue after resolving merge conflicts");

        var rootCommand = new RootCommand("GitHub Integration Tool - Creates integration PRs from multiple sub-PRs")
        {
            configOption,
            continueOption
        };

        rootCommand.SetHandler(async (configFile, continueFlag) =>
        {
            try
            {
                if (continueFlag)
                {
                    logger.LogInformation("Continuing integration process");
                    await ContinueIntegrationAsync();
                }
                else if (configFile != null)
                {
                    logger.LogInformation("Starting new integration process", new { ConfigFile = configFile.FullName });
                    await StartIntegrationAsync(configFile);
                }
                else
                {
                    logger.LogError("Invalid command line arguments");
                    Console.WriteLine("Error: Please specify either --config <file> or --continue");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in main execution");
                throw;
            }
        }, configOption, continueOption);

        return await rootCommand.InvokeAsync(args);
    }

    private async Task StartIntegrationAsync(FileInfo configFile)
    {
        var sessionId = Guid.NewGuid().ToString();
        logger.LogInformation("Starting integration session", new { SessionId = sessionId, ConfigFile = configFile.FullName });
        
        try
        {
            // Validate existing state file
            if (File.Exists(StateFileName))
            {
                logger.LogWarning("Found existing state file", new { StateFileName });
                Console.WriteLine(
                    "Found existing state file. Please resolve any previous issues before starting a new integration.");
                Console.WriteLine("If you want to start a new integration, please delete the state file: " +
                                  StateFileName);
                Console.WriteLine("Or use the --continue option to resume the previous integration.");
                return;
            }

            // Read and parse configuration
            var config = await ReadConfigurationAsync(configFile);
            logger.LogInformation("Configuration loaded successfully", new { 
                Team = config.Team, 
                ReleaseName = config.ReleaseName, 
                PrCount = config.Pr.Count,
                RepoDirectory = config.RepoDirectory,
                MainBranch = config.MainBranch
            });
            
            Console.WriteLine($"Starting integration for team: {config.Team}");
            Console.WriteLine($"Release name: {config.ReleaseName}");
            Console.WriteLine($"PRs to merge: {string.Join(", ", config.Pr)}");

            defaultPrDescriptionTemplate = await FileUtils.LoadFileAsync(config.PullRequestTemplate);

            // Validate repository directory
            await ValidateRepositoryDirectoryAsync(config.RepoDirectory);

            // Validate PRs
            await ValidatePrsAsync(config.Pr);

            // Create new branch
            await CreateBranchAsync(config.ReleaseName, config.MainBranch);

            // Save state for potential resume
            await SaveStateAsync(config, 0);

            // Merge PRs sequentially
            await MergePrsAsync(config);

            // Create pull request
            await CreatePullRequestAsync(config);

            // Clean up state file
            CleanupStateFile();

            logger.LogInformation("Integration completed successfully", new { SessionId = sessionId });
            Console.WriteLine("✅ Integration completed successfully!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Integration failed", new { SessionId = sessionId });
            Console.WriteLine($"❌ Error: {ex.Message}");
        }
    }

    private async Task ContinueIntegrationAsync()
    {
        var sessionId = Guid.NewGuid().ToString();
        logger.LogInformation("Continuing integration session", new { SessionId = sessionId });
        
        try
        {
            if (!File.Exists(StateFileName))
            {
                logger.LogWarning("No integration state found");
                Console.WriteLine("No integration state found. Nothing to continue.");
                return;
            }

            var stateJson = await File.ReadAllTextAsync(StateFileName);
            var state = JsonParse<Dictionary<string, JsonElement>>(stateJson);

            if (state == null)
            {
                logger.LogError("Invalid state file found");
                Console.WriteLine("Invalid state file found.");
                return;
            }

            var config = JsonParse<Configuration>(state["config"].GetRawText());
            var currentPrIndex = state["currentPrIndex"].GetInt32();

            if (config == null)
            {
                logger.LogError("Invalid configuration in state file");
                Console.WriteLine("Invalid configuration in state file.");
                return;
            }

            logger.LogInformation("Resuming integration", new { 
                CurrentPrIndex = currentPrIndex, 
                RemainingPrs = config.Pr.Skip(currentPrIndex).ToList() 
            });
            
            Console.WriteLine($"Continuing integration from PR index: {currentPrIndex}");
            Console.WriteLine($"Remaining PRs: {string.Join(", ", config.Pr.Skip(currentPrIndex))}");
            
            defaultPrDescriptionTemplate = await FileUtils.LoadFileAsync(config.PullRequestTemplate);

            // Validate repository directory
            await ValidateRepositoryDirectoryAsync(config.RepoDirectory);

            // Continue merging PRs
            await MergePrsAsync(config, currentPrIndex);

            // Create pull request
            await CreatePullRequestAsync(config);

            // Clean up state file
            CleanupStateFile();

            logger.LogInformation("Integration resumed and completed successfully", new { SessionId = sessionId });
            Console.WriteLine("✅ Integration completed successfully!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Integration resume failed", new { SessionId = sessionId });
            Console.WriteLine($"❌ Error: {ex.Message}");
        }
    }

    private async Task<Configuration> ReadConfigurationAsync(FileInfo configFile)
    {
        logger.LogInformation("Reading configuration file", new { ConfigFile = configFile.FullName });
        
        if (!configFile.Exists)
        {
            logger.LogError("Configuration file not found", new { ConfigFile = configFile.FullName });
            throw new FileNotFoundException($"Configuration file not found: {configFile.FullName}");
        }

        var json = await File.ReadAllTextAsync(configFile.FullName);
        var config = JsonParse<Configuration>(json);

        if (config == null)
        {
            logger.LogError("Failed to parse configuration file");
            throw new InvalidOperationException("Failed to parse configuration file");
        }

        if (string.IsNullOrWhiteSpace(config.Team))
        {
            logger.LogError("Team is required in configuration");
            throw new InvalidOperationException("Team is required in configuration");
        }

        if (string.IsNullOrWhiteSpace(config.ReleaseName))
        {
            logger.LogError("ReleaseName is required in configuration");
            throw new InvalidOperationException("ReleaseName is required in configuration");
        }

        if (config.Pr.Count == 0)
        {
            logger.LogError("At least one PR number is required");
            throw new InvalidOperationException("At least one PR number is required");
        }

        if (string.IsNullOrWhiteSpace(config.RepoDirectory))
        {
            logger.LogError("RepoDirectory is required in configuration");
            throw new InvalidOperationException("RepoDirectory is required in configuration");
        }

        logger.LogInformation("Configuration validation passed");
        return config;
    }

    private async Task ValidateRepositoryDirectoryAsync(string repoDirectory)
    {
        logger.LogInformation("Validating repository directory", new { RepoDirectory = repoDirectory });
        
        if (string.IsNullOrWhiteSpace(repoDirectory))
        {
            logger.LogError("RepoDirectory is required in configuration");
            throw new InvalidOperationException("RepoDirectory is required in configuration");
        }

        var directory = new DirectoryInfo(repoDirectory);
        if (!directory.Exists)
        {
            logger.LogError("Repository directory does not exist", new { RepoDirectory = repoDirectory });
            throw new InvalidOperationException($"Repository directory does not exist: {repoDirectory}");
        }

        // Check if it's a git repository
        var gitDir = Path.Combine(directory.FullName, ".git");
        if (!Directory.Exists(gitDir))
        {
            logger.LogError("Directory is not a git repository", new { RepoDirectory = repoDirectory });
            throw new InvalidOperationException($"Directory is not a git repository: {repoDirectory}");
        }

        repositoryDirectory = directory.FullName;
        logger.LogInformation("Repository directory validated successfully", new { RepoDirectory = repositoryDirectory });
    }

    private List<PullRequest> ReadyPrs = new();

    private async Task ValidatePrsAsync(List<int> prNumbers)
    {
        logger.LogInformation("Validating PR numbers", new { PrCount = prNumbers.Count, PrNumbers = prNumbers });
        Console.WriteLine("Validating PR numbers...");

        foreach (var prNumber in prNumbers)
        {
            try
            {
                logger.LogDebug("Validating PR", new { PrNumber = prNumber });
                
                var result =
                    await ExecuteGhCommandAsync($"pr view {prNumber} --json number,title,state,body,headRefName,author");
                var prInfo = JsonParse<PullRequest>(result)!;

                if (prInfo.State != "OPEN")
                {
                    logger.LogError("PR is not in valid state", new { PrNumber = prNumber, State = prInfo.State });
                    throw new InvalidOperationException($"PR {prNumber} is not in a valid state: {prInfo.State}");
                }

                ReadyPrs.Add(prInfo);
                logger.LogInformation("PR validated successfully", new { PrNumber = prNumber, Title = prInfo.Title });

                Console.WriteLine($"✅ PR {prNumber} is valid");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to validate PR", new { PrNumber = prNumber });
                throw new InvalidOperationException($"Failed to validate PR {prNumber}: {ex.Message}");
            }
        }
        
        logger.LogInformation("All PRs validated successfully", new { ValidatedCount = ReadyPrs.Count });
    }

    private async Task CreateBranchAsync(string branchName, string mainBranch)
    {
        logger.LogInformation("Creating branch", new { BranchName = branchName, MainBranch = mainBranch });
        Console.WriteLine($"Checking if branch '{branchName}' already exists...");

        // Check if the branch exists locally
        var localBranchExists = await CheckBranchExistsAsync(branchName, false);
        
        // Check if the branch exists remotely
        var remoteBranchExists = await CheckBranchExistsAsync(branchName, true);

        logger.LogInformation("Branch existence check completed", new { 
            BranchName = branchName, 
            LocalExists = localBranchExists, 
            RemoteExists = remoteBranchExists 
        });

        if (localBranchExists)
        {
            logger.LogInformation("Branch exists locally, checking out", new { BranchName = branchName });
            Console.WriteLine($"Branch '{branchName}' already exists locally. Checking out to it...");
            await ExecuteGitCommandAsync($"checkout {branchName}");

            // If it also exists remotely, pull the latest changes
            if (remoteBranchExists)
            {
                logger.LogInformation("Pulling latest changes from remote branch", new { BranchName = branchName });
                Console.WriteLine($"Pulling latest changes from remote branch '{branchName}'...");
                await ExecuteGitCommandAsync($"pull origin {branchName}");
            }
        }
        else if (remoteBranchExists)
        {
            logger.LogInformation("Creating local tracking branch from remote", new { BranchName = branchName });
            Console.WriteLine($"Branch '{branchName}' exists remotely. Creating local tracking branch...");
            await ExecuteGitCommandAsync($"checkout -b {branchName} origin/{branchName}");
        }
        else
        {
            logger.LogInformation("Creating new branch", new { BranchName = branchName, MainBranch = mainBranch });
            Console.WriteLine($"Creating new branch: {branchName}");

            // Ensure we're on the main branch first
            await ExecuteGitCommandAsync($"checkout {mainBranch}");
            await ExecuteGitCommandAsync($"pull origin {mainBranch}");

            // Now create the new branch
            await ExecuteGitCommandAsync($"checkout -b {branchName}");
        }
        
        logger.LogInformation("Branch operation completed successfully", new { BranchName = branchName });
    }

    private async Task<bool> CheckBranchExistsAsync(string branchName, bool checkRemote)
    {
        try
        {
            var command = checkRemote ? $"ls-remote --heads origin {branchName}" : $"show-ref --heads {branchName}";
            var result = await ExecuteGitCommandAsync(command, false);
            var exists = !string.IsNullOrWhiteSpace(result);
            
            logger.LogDebug("Branch existence check", new { 
                BranchName = branchName, 
                CheckRemote = checkRemote, 
                Exists = exists 
            });
            
            return exists;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Branch existence check failed", new { 
                BranchName = branchName, 
                CheckRemote = checkRemote 
            });
            return false;
        }
    }

    private async Task MergePrsAsync(Configuration config, int startIndex = 0)
    {
        logger.LogInformation("Starting PR merge process", new { 
            StartIndex = startIndex, 
            TotalPrs = ReadyPrs.Count, 
            RemainingPrs = ReadyPrs.Count - startIndex 
        });
        
        for (int i = startIndex; i < ReadyPrs.Count; i++)
        {
            var prInfo = ReadyPrs[i];
            var prNumber = prInfo.Number;
            var headRefName = prInfo.HeadRefName;
            
            logger.LogInformation("Merging PR", new { 
                PrNumber = prNumber, 
                Index = i + 1, 
                Total = ReadyPrs.Count, 
                HeadRefName = headRefName 
            });
            
            Console.WriteLine($"\nMerging PR {prInfo.Number} ({i + 1}/{ReadyPrs.Count})...");

            try
            {
                // Fetch the PR branch
                await ExecuteGitCommandAsync($"fetch origin {headRefName}");

                // Merge the PR (non-fast-forward to preserve history)
                var mergeResult =
                    await ExecuteGitCommandAsync($"merge origin/{headRefName} --no-ff",
                        false);

                logger.LogInformation("PR merged successfully", new { PrNumber = prNumber });
                Console.WriteLine($"✅ Successfully merged PR {prNumber}");

                // Update state
                await SaveStateAsync(config, i + 1);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to merge PR", new { PrNumber = prNumber, Index = i });
                Console.WriteLine($"❌ Failed to merge PR {prNumber}: {ex.Message}");
                Console.WriteLine("\n🔧 Merge conflict detected!");
                Console.WriteLine("Please resolve the conflicts manually and then run:");
                Console.WriteLine("  gh-integration-tool --continue");

                // Save state for resume
                await SaveStateAsync(config, i);
                Environment.Exit(1);
            }
        }
        
        logger.LogInformation("All PRs merged successfully", new { MergedCount = ReadyPrs.Count });
    }

    private async Task CreatePullRequestAsync(Configuration config)
    {
        if (!config.PushToOrigin)
        {
            logger.LogInformation("Skipping push to origin as configured");
            Console.WriteLine("Skipping push to origin. No pull request will be created.");
            return;
        }

        logger.LogInformation("Creating integration pull request");
        Console.WriteLine("\nCreating integration pull request...");

        // Push the branch
        await ExecuteGitCommandAsync("push origin HEAD");
        
        // Generate PR description
        var description = string.Format(defaultPrDescriptionTemplate, GetPrDescription());

        // Check if the PR from the current branch to main branch already exists
        var existingPr = await CheckExistingPullRequestAsync(config.MainBranch);
        
        if (existingPr != null)
        {
            logger.LogInformation("Pull request already exists", new { 
                PrNumber = existingPr.Number, 
                Url = existingPr.Url 
            });
            Console.WriteLine($"✅ Pull request already exists: #{existingPr.Number}");

            if (existingPr.Body != description)
            {
                Console.WriteLine("Updating existing pull request description...");
                await ExecuteGhCommandAsync($"pr edit {existingPr.Number} --body \"{description}\"");
            }
            
            Console.WriteLine($"PR URL: {existingPr.Url}");
            return;
        }

        // Create PR using gh CLI
        var prResult =
            await ExecuteGhCommandAsync(
                $"pr create --title \"{config.Title}\" --body \"{description}\" --base {config.MainBranch} --draft");

        logger.LogInformation("Pull request created successfully", new { PrUrl = prResult.Trim() });
        Console.WriteLine("✅ Pull request created successfully!");
        Console.WriteLine($"PR URL: {prResult.Trim()}");
    }

    private async Task<PullRequest?> CheckExistingPullRequestAsync(string baseBranch)
    {
        try
        {
            // Get current branch name
            var currentBranch = await ExecuteGitCommandAsync("rev-parse --abbrev-ref HEAD");
            
            logger.LogDebug("Checking for existing PR", new { CurrentBranch = currentBranch, BaseBranch = baseBranch });
            
            // List PRs from current branch to the base branch
            var result = await ExecuteGhCommandAsync($"pr list --head {currentBranch} --base {baseBranch} --json number,title,state,url", false);
            
            if (string.IsNullOrWhiteSpace(result))
            {
                logger.LogDebug("No existing PR found");
                return null;
            }
            
            var prs = JsonParse<List<PullRequest>>(result);
            if (prs == null || prs.Count == 0)
            {
                logger.LogDebug("No existing PR found");
                return null;
            }
            
            // Return the first open PR (there should only be one)
            var existingPr = prs.FirstOrDefault(pr => pr.State == "OPEN");
            if (existingPr != null)
            {
                logger.LogInformation("Found existing PR", new { PrNumber = existingPr.Number, State = existingPr.State });
            }
            
            return existingPr;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error checking for existing PR");
            return null;
        }
    }

    private string GetPrDescription()
    {
        logger.LogDebug("Generating PR description");
        
        var description = new StringBuilder();
        var groupPrs = ReadyPrs
            .GroupBy(pr => pr.Author.Login)
            .OrderBy(group => group.Key)
            .ToDictionary(x => x.Key, x=> x.ToList());
        
        foreach (var (author, prs) in groupPrs)
        {
            // Add author information
            description.AppendLine($"@{author}");
            
            // Add PRs for this author
            foreach (var pr in prs)
            {
                var tickets = TextParser.ExtractJiraTickets(pr.Body, pr.Title);
                var ticketsString = string.Join(", ", tickets);
                if (string.IsNullOrEmpty(ticketsString))
                {
                    ticketsString = "N/A";
                }
                description.AppendLine($"- #{pr.Number}: {ticketsString}");
            }
        }
        
        logger.LogDebug("PR description generated", new { DescriptionLength = description.Length });
        return description.ToString();
    }

    private async Task SaveStateAsync(Configuration config, int currentPrIndex)
    {
        logger.LogDebug("Saving integration state", new { CurrentPrIndex = currentPrIndex });
        
        var state = new
        {
            config,
            currentPrIndex,
            timestamp = DateTime.UtcNow
        };

        var stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(StateFileName, stateJson);
        
        logger.LogDebug("Integration state saved", new { StateFileName, CurrentPrIndex = currentPrIndex });
    }

    private void CleanupStateFile()
    {
        if (File.Exists(StateFileName))
        {
            File.Delete(StateFileName);
            logger.LogInformation("State file cleaned up", new { StateFileName });
        }
    }

    private async Task<string> ExecuteGhCommandAsync(string command, bool throwOnError = true)
    {
        logger.LogDebug("Executing GitHub CLI command", new { Command = command, ThrowOnError = throwOnError });
        return await ExecuteCommandAsync("gh", command, throwOnError, repositoryDirectory);
    }

    private async Task<string> ExecuteGitCommandAsync(string command, bool throwOnError = true)
    {
        logger.LogDebug("Executing Git command", new { Command = command, ThrowOnError = throwOnError });
        return await ExecuteCommandAsync("git", command, throwOnError, repositoryDirectory);
    }

    private async Task<string> ExecuteCommandAsync(string program, string arguments, bool throwOnError)
    {
        return await ExecuteCommandAsync(program, arguments, throwOnError, null);
    }

    private async Task<string> ExecuteCommandAsync(string program, string arguments, bool throwOnError,
        string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && throwOnError)
        {
            logger.LogError("Command execution failed", new { 
                Program = program, 
                Arguments = arguments, 
                ExitCode = process.ExitCode, 
                Error = error.ToString() 
            });
            throw new InvalidOperationException($"Command failed: {program} {arguments}\nError: {error}");
        }

        logger.LogDebug("Command executed successfully", new { 
            Program = program, 
            Arguments = arguments, 
            ExitCode = process.ExitCode, 
            OutputLength = output.Length 
        });
        
        return output.ToString().TrimEnd();
    }

    private static T? JsonParse<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
