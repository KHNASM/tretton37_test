namespace SampleWebScrapper.Logging;

internal abstract class OutputLogger : IOutputLogger
{
    private string TimeStamp => DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff");
    public Task LogAsync(MessageType level, string message, string? prefix = null)
    {
        prefix ??= $"[{TimeStamp}] ";
        string formattedMessage = $"{prefix}{message}";
        return WriteLineAsync(level, formattedMessage);
    }

    public void Log(MessageType level, string message, string? prefix = null)
    {
        prefix ??= $"[{TimeStamp}] ";
        string formattedMessage = $"{prefix}{message}";
        WriteLine(level, formattedMessage);
    }

    public Task LogInsignificantAsync(string message) => LogAsync(MessageType.Insignificant, message);
    public Task LogNormalAsync(string message) => LogAsync(MessageType.Normal, message);
    public Task LogSuccessAsync(string message) => LogAsync(MessageType.Success, message);
    public Task LogWarningAsync(string message) => LogAsync(MessageType.Warning, message);
    public Task LogErrorAsync(string message) => LogAsync(MessageType.Error, message);

    public void LogInsignificant(string message) => Log(MessageType.Insignificant, message);
    public void LogInfo(string message) => Log(MessageType.Normal, message);
    public void LogSuccess(string message) => Log(MessageType.Success, message);
    public void LogWarning(string message) => Log(MessageType.Warning, message);
    public void LogError(string message) => Log(MessageType.Error, message);
   
    protected abstract void WriteLine(MessageType level, string message);
    protected abstract Task WriteLineAsync(MessageType level, string message);
}
