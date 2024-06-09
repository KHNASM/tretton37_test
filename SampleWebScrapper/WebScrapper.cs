using HtmlAgilityPack;
using System.Collections.Concurrent;

namespace SampleWebScrapper;

internal class WebScrapper
{
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly ConcurrentDictionary<string, object?> _visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object?> _filesProcessed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object?> _nonScrapableLinks = new(StringComparer.OrdinalIgnoreCase);

    private readonly Uri _baseUri;
    private readonly string _startingUrl;
    private readonly string _outputRootDirectory;
    
    public WebScrapper(InputParams inputParams)
    {
        if (inputParams is null)
        {
            throw new ArgumentNullException(nameof(inputParams));
        }

        if(!inputParams.IsValid)
        {
           throw new ArgumentException(inputParams.ErrorMessage, nameof(inputParams));
        }

        _outputRootDirectory = inputParams.OutputDirectory;
        

        if (!Uri.TryCreate(inputParams.BaseUrl, UriKind.RelativeOrAbsolute, out Uri? uri) || uri == null || !uri.IsAbsoluteUri)
        {
            throw new ArgumentException($"'{nameof(inputParams)}.{inputParams.BaseUrl}' must be a valid absolute url.", $"{nameof(inputParams)}.{inputParams.BaseUrl}");
        }

        string baseUrl = uri.GetLeftPart(UriPartial.Authority);

        _baseUri = new Uri(baseUrl);
        _startingUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/', '\\');
    }

    public async Task RunScraperAsync()
    {
        _queue.Enqueue(_startingUrl);

        while (_queue.Count > 0)
        {
            int parallelCount = Math.Min(_queue.Count, Environment.ProcessorCount);

            List<Task> tasks = new();

            for (int i = 0; i < parallelCount; i++)
            {
                if (_queue.TryDequeue(out var parallelUrl))
                {
                    tasks.Add(DownloadPageAsync(parallelUrl));
                }
            }

            await Task.WhenAll(tasks);
        }

        // non-scrapable links
        List<Task> moreTasks = new();

        for (int i = 0; i < _nonScrapableLinks.Count; i++)
        {
            moreTasks.Add(DownloadPageAsync(_nonScrapableLinks.ElementAt(i).Key, false));

            if ((i + 1) % Environment.ProcessorCount == 0)
            {
                await Task.WhenAll(moreTasks);
                moreTasks.Clear();
            }
        }

        if (moreTasks.Count > 0)
        {
            await Task.WhenAll(moreTasks);
        }
    }

    private async Task DownloadPageAsync(string url, bool drilldown = true)
    {
        if (_visited.ContainsKey(url))
        {
            await WriteLineAsync($"U U U U  Already visited {url}"); // TODO: revisit to improve this
            return;
        }
        else
        {
            _visited.TryAdd(url, null);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri == null)
        {
            await WriteLineAsync($"X X X X X X Unable to download page because '{url}' is not a valid absolute url"); // TODO: revisit to improve this
            return;
        }

        if (uri.Host != _baseUri.Host)
        {
            await WriteLineAsync($"X X X X X X Unable to download page because '{url}' is not on the same domain as the base url"); // TODO: revisit to improve this
            return;
        }

        string localPath = ComputePath(_outputRootDirectory, uri);
        if (localPath == _outputRootDirectory) // TODO: revisit to improve this
        {
            localPath = Path.Combine(localPath, "index.html");
        }

        using var client = new HttpClient();
        var response = await client.GetAsync(uri);

        HttpContent content = response.Content;

        if (!response.IsSuccessStatusCode)
        {
            await WriteLineAsync($"X X X X X X Failed to download {url}: Server returned: HTTP {(int)response.StatusCode}"); // TODO: revisit to improve this
            return;
        }

        using Stream stream = await response.Content.ReadAsStreamAsync();
        await SaveStreamAsFileAsync(stream, localPath);

        await WriteLineAsync($":) :) :) Downloaded {url}"); // TODO: revisit to improve this

        if (drilldown)
        {
            QueueLinksFromFileAsync(localPath, uri);
        }
    }


    private void QueueLinksFromFileAsync(string path, Uri currentUri)
    {
        HtmlDocument doc = new();

        doc.Load(path); // TODO: Async? non-html?

        var pageLinks = doc.DocumentNode.Descendants("a")
            .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("href", null), currentUri)!)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();

        var nonScrapableLinks = doc.DocumentNode.Descendants("link")
            .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("href", null), currentUri)!)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Union(doc.DocumentNode.Descendants("script")
                .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("src", null), currentUri)!)
                .Where(href => !string.IsNullOrWhiteSpace(href)))
            .Union(doc.DocumentNode.Descendants("img")
                .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("src", null), currentUri)!)
                .Where(href => !string.IsNullOrWhiteSpace(href)));


        foreach (var link in nonScrapableLinks)
        {
            _nonScrapableLinks.TryAdd(link, null);
        }

        foreach (string pageLink in pageLinks)
        {
            _queue.Enqueue(pageLink);
        }
    }

    private string? ConvertToAbsoluteUrl(string url, Uri currentUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? uri) || uri == null)
        {
            return null;
        }

        if (!uri.IsAbsoluteUri)
        {
            uri = new Uri(currentUri, uri);
        }

        if (uri.IsAbsoluteUri && uri.Host != _baseUri.Host)
        {
            return null;
        }

        string absoluteUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/', '\\');

        return absoluteUrl;
    }

    private static string ComputePath(string rootDirectory, Uri uri)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException($"'{nameof(rootDirectory)}' cannot be null or whitespace.", nameof(rootDirectory));
        }

        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException($"'{nameof(uri)}' must be an absolute uri", nameof(uri));
        }

        string localPath = uri.LocalPath
            .Trim('/', '\\')
            .Replace('/', '\\');

        localPath = Path.Combine(rootDirectory, localPath);

        return localPath;
    }

    private async Task SaveStreamAsFileAsync(Stream stream, string localPath)
    {
        if (_filesProcessed.ContainsKey(localPath))
        {
            await WriteLineAsync($"F F F F Already processed {localPath}"); // TODO: revisit to improve this
            return;
        }
        else
        {
            _filesProcessed.TryAdd(localPath, null);
        }

        string destinationDirectory = Path.GetDirectoryName(localPath)!;

        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var fileStream = File.Create(localPath);
        await stream.CopyToAsync(fileStream);
    }

    private Task WriteLineAsync(string text) => Console.Out.WriteLineAsync(text);
}
