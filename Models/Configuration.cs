namespace GhIntegrationTool.Models;

public class Configuration
{
    public string Team { get; set; } = "N/A";
    public string RepoDirectory { get; set; } = string.Empty;
    public string PullRequestTemplate { get; set; } = string.Empty;
    public string MainBranch { get; set; } = "main";

    private string title;
    public string Title
    {
        get => GetValueOrDefault(title, ReleaseName);
        set => title = value;
    }

    public string ReleaseName { get; set; } = "release";
    public List<int> Pr { get; set; } = new();
    public bool PushToOrigin { get; set; }
    
    private string GetValueOrDefault(string? value, string defaultValue)
    {
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }
}
