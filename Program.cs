namespace GhIntegrationTool;

public static class Program
{
    static async Task<int> Main(string[] args)
    {
        var tool = new IntegrationTool();
        return await tool.RunAsync(args);
    }
}
