namespace SampleWebScrapper.Logging;

interface IOutputLogger 
{
    Task LogAsync(MessageType level, string message, string? prefix = null);

    void Log(MessageType level, string message, string? prefix = null);

    Task LogInsignificantAsync(string message);

    Task LogNormalAsync(string message);

    Task LogEmphasisAsync(string message);

    Task LogSuccessAsync(string message);

    Task LogWarningAsync(string message);

    Task LogErrorAsync(string message);
}
