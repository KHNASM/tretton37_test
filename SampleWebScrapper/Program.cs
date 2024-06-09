using SampleWebScrapper.Helpers;
using SampleWebScrapper.Logging;


namespace SampleWebScrapper;

public class Program
{
    public static async Task Main(string[] args)
    {
        // preserve the original colors to be restored later
        ConsoleColor foregroundColor = Console.ForegroundColor;
        ConsoleColor backgroundColor = Console.BackgroundColor;

        try
        {
            InputParams? inputParameters = CommandArgumentParser.Parse(args);

            if(inputParameters is null)
            {
                return;
            }

            if (!inputParameters.IsValid)
            {
                Console.WriteLine();
                Console.WriteLine(inputParameters.ErrorMessage);
                return;
            }

            IOutputLogger logger = new ConsoleLogger();
            IFilingHelper filingHelper = new FilingHelper();

            WebScrapper scrapper = new(inputParameters, logger, filingHelper);
            await scrapper.RunScraperAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            // restore the original colors anyway
            Console.ForegroundColor = foregroundColor;
            Console.BackgroundColor = backgroundColor;

            Console.WriteLine();
            Console.WriteLine("-- END --");
        }
    }
}
