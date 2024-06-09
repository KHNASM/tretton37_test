using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;


namespace SampleWebScrapper;

public class Program
{

    public static async Task Main(string[] args)
    {
        Console.Write("Any existing outputs will be overwritten. Sure to continue [Y/N]: ");
        string? response = Console.ReadLine();

        if(string.IsNullOrWhiteSpace(response) || !response.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        InputParams inputParameters = CommandArgumentParser.Parse(args);

        if (!inputParameters .IsValid)
        {
            Console.WriteLine(inputParameters.ErrorMessage);
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Starting scrapping {inputParameters.BaseUrl} recursively into the directory '{inputParameters.OutputDirectory}'. . .");
        Console.WriteLine();

        Stopwatch stopwatch = Stopwatch.StartNew();

        WebScrapper scrapper = new(inputParameters);
        await scrapper.RunScraperAsync();

        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine($"Scraping completed in {stopwatch.Elapsed.TotalSeconds} seconds.");

        Console.ReadLine();
    }
}
