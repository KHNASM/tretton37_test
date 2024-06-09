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
        const string baseUrl = "https://books.toscrape.com/";
        const string outputDirectory = "E:\\_Scrapper_Output";

        if(Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, true);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(outputDirectory);

        WebScrapper scrapper = new(baseUrl, outputDirectory);

        await scrapper.RunScraperAsync();

        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine($"Scraping completed in {stopwatch.Elapsed.TotalSeconds} seconds.");

        Console.ReadLine();
    }
}
