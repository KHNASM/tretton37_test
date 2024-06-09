﻿using SampleWebScrapper.Helpers;
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
            InputParams inputParameters = CommandArgumentParser.Parse(args);

            if (!inputParameters.IsValid)
            {
                Console.WriteLine(inputParameters.ErrorMessage);
                return;
            }

            Console.Write("Any existing outputs will be overwritten. Sure to continue [Y/N]: ");
            string? response = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(response) || !response.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
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
            Console.WriteLine("That's all folks :)");
        }
    }
}
