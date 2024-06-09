using SampleWebScrapper.Logging;


namespace SampleWebScrapper;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.Write("Any existing outputs will be overwritten. Sure to continue [Y/N]: ");
        string? response = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(response) || !response.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        InputParams inputParameters = CommandArgumentParser.Parse(args);

        if (!inputParameters.IsValid)
        {
            Console.WriteLine(inputParameters.ErrorMessage);
            return;
        }

        IOutputLogger logger = new ConsoleLogger();

        WebScrapper scrapper = new(inputParameters, logger);
        await scrapper.RunScraperAsync();

        Console.ReadLine();
    }
}
