using System;
using System.Collections.Concurrent;
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

        Directory.CreateDirectory(outputDirectory);

        WebScrapper scrapper = new(baseUrl, outputDirectory);

        await scrapper.RunScraperAsync();
    }

}
