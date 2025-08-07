using System.Text.RegularExpressions;

namespace GhIntegrationTool;

public class FileUtils
{
    public static async Task<string> LoadFileAsync(string fileName)
    {
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException($"File not found: {fileName}");
        }

        return await File.ReadAllTextAsync(fileName);
    }
}


public class TextParser
{
    public static List<string> ExtractJiraTickets(string prDescription, string title)
    {
        // Pattern to match JIRA tickets like ENI-1542, PROJ-123, etc.
        // Matches uppercase letters followed by dash and numbers
        var pattern = @"[A-Z]+-\d+";
        
        var matches = FindMatches(prDescription, pattern);
        matches.AddRange(FindMatches(title, pattern));
        
        return matches.Distinct().ToList();
    }

    private static List<string> FindMatches(string text, string pattern)
    {
        return Regex.Matches(text, pattern).Select(x => x.Value).ToList();
    }
}
