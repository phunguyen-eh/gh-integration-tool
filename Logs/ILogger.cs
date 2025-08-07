namespace GhIntegrationTool;

/// <summary>
/// Interface for structured logging with different log levels
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Logs a debug message with optional structured data
    /// </summary>
    void LogDebug(string message, object? data = null);
    
    /// <summary>
    /// Logs a debug message with exception and optional structured data
    /// </summary>
    void LogDebug(Exception exception, string message, object? data = null);
    
    /// <summary>
    /// Logs an information message with optional structured data
    /// </summary>
    void LogInformation(string message, object? data = null);
    
    /// <summary>
    /// Logs a warning message with optional structured data
    /// </summary>
    void LogWarning(string message, object? data = null);
    
    /// <summary>
    /// Logs an error message with optional structured data
    /// </summary>
    void LogError(string message, object? data = null);
    
    /// <summary>
    /// Logs an error message with exception and optional structured data
    /// </summary>
    void LogError(Exception exception, string message, object? data = null);
}
