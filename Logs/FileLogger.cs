using System.Text.Json;

namespace GhIntegrationTool;

/// <summary>
/// File-based logger implementation with structured logging and best practices
/// </summary>
public class FileLogger : ILogger
{
    private readonly string logFilePath;
    private readonly object lockObject = new();
    
    public FileLogger()
    {
        var today = DateTime.Now;
        var fileName = $"Logs/logs-{today:ddMMyyyy}.log";
        logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
    }
    
    public void LogDebug(string message, object? data = null)
    {
        WriteLog("DEBUG", message, null, data);
    }
    
    public void LogDebug(Exception exception, string message, object? data = null)
    {
        WriteLog("DEBUG", message, exception, data);
    }
    
    public void LogInformation(string message, object? data = null)
    {
        WriteLog("INFO", message, null, data);
    }
    
    public void LogWarning(string message, object? data = null)
    {
        WriteLog("WARN", message, null, data);
    }
    
    public void LogError(string message, object? data = null)
    {
        WriteLog("ERROR", message, null, data);
    }
    
    public void LogError(Exception exception, string message, object? data = null)
    {
        WriteLog("ERROR", message, exception, data);
    }
    
    private void WriteLog(string level, string message, Exception? exception = null, object? data = null)
    {
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            Exception = exception?.ToString(),
            Data = data,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName
        };
        
        var json = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        lock (lockObject)
        {
            try
            {
                if (!File.Exists(logFilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                }
                File.AppendAllText(logFilePath, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Fallback to console if file writing fails
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
                Console.WriteLine($"Log entry: {json}");
            }
        }
    }
    
    private class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public object? Data { get; set; }
        public int ProcessId { get; set; }
        public int ThreadId { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }
}
