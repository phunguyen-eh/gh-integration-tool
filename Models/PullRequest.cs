
namespace GhIntegrationTool.Models;

public class PullRequest
{
    public int Number { get; set; }
    public string Title { get; set; }
    public string State { get; set; }
    public string Body { get; set; }
    public string HeadRefName { get; set; }
    public PRAuthor Author { get; set; }
    public string Url { get; set; }
}

public class PRAuthor
{
    public string Id { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
}
