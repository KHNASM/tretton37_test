namespace SampleWebScrapper.Logging;

internal class ConsoleLogger : OutputLogger
{
    protected override Task WriteLineAsync(MessageType level, string message)
    {
        using var _ = Color(GetColor(level));
        return Console.Out.WriteLineAsync(message);
    }

    protected override void WriteLine(MessageType level, string message)
    {
        using var _ = Color(GetColor(level));
        Console.WriteLine(message);
    }
    private static ConsoleColor GetColor(MessageType level) => level switch
    {
        MessageType.Insignificant => ConsoleColor.DarkGray,
        MessageType.Normal => ConsoleColor.Gray,
        MessageType.Emphasis => ConsoleColor.Cyan,
        MessageType.Success => ConsoleColor.Green,
        MessageType.Warning => ConsoleColor.Yellow,
        MessageType.Error => ConsoleColor.Red,
        _ => ConsoleColor.Gray,
    };

    #region Colors

    private IDisposable Color(ConsoleColor foregroundColor, ConsoleColor backgroundColor = ConsoleColor.Black)
        => new __Color(foregroundColor, backgroundColor);

    private class __Color : IDisposable
    {
        ConsoleColor _currentForegroundColor;
        ConsoleColor _currentBackgroundColor;

        public __Color(ConsoleColor foregroundColor, ConsoleColor backgroundColor = ConsoleColor.Black)
        {
            _currentForegroundColor = Console.ForegroundColor;
            _currentBackgroundColor = Console.BackgroundColor;

            Console.ForegroundColor = foregroundColor;
            Console.BackgroundColor = backgroundColor;
        }

        public void Dispose()
        {
            Console.ForegroundColor = _currentForegroundColor;
            Console.BackgroundColor = _currentBackgroundColor;
        }
    }

    #endregion
}
