﻿using HtmlAgilityPack;
using SampleWebScrapper.Helpers;
using SampleWebScrapper.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SampleWebScrapper;

internal class WebScrapper
{
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly ConcurrentDictionary<string, object?> _visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object?> _filesProcessed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object?> _deferredLinks = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentBag<string> _warnings = new();
    private readonly ConcurrentBag<string> _errors = new();

    private readonly Uri _baseUri;
    private readonly string _startingUrl;
    private readonly string _destinationRootPath;
    private readonly InputParams _inputParams;
    private readonly IOutputLogger _logger;
    private readonly IFilingHelper _persistenceHelper;
    private volatile int _itemCount = 0; // volatile to prevent compiler optimizations

    public WebScrapper(InputParams inputParams, IOutputLogger logger, IFilingHelper persistenceHelper)
    {
        ArgumentNullException.ThrowIfNull(inputParams);

        if (!inputParams.IsValid)
        {
            throw new ArgumentException(inputParams.ErrorMessage, nameof(inputParams));
        }

        _destinationRootPath = inputParams.OutputDirectory;

        if (!Uri.TryCreate(inputParams.BaseUrl, UriKind.RelativeOrAbsolute, out Uri? uri) || uri == null || !uri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                $"'{nameof(inputParams)}.{inputParams.BaseUrl}' must be a valid absolute url.",
                $"{nameof(inputParams)}.{inputParams.BaseUrl}");
        }

        string baseUrl = uri.GetLeftPart(UriPartial.Authority);

        _baseUri = new Uri(baseUrl);
        _startingUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/', '\\');
        _inputParams = inputParams;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _persistenceHelper = persistenceHelper;
    }

    public async Task RunScraperAsync()
    {
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, $"Starting scrapping {_baseUri} recursively into the directory '{_destinationRootPath}'. . .");
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);

        Stopwatch stopwatch = Stopwatch.StartNew();
        
        await ScrapeCoreResourcesAsync();
        await DownloadDeferredResourcesAsync();

        stopwatch.Stop();

        await LogSummaryAsync(stopwatch.Elapsed);
    }

    private async Task ScrapeCoreResourcesAsync()
    {
        _queue.Enqueue(_startingUrl);

        while (_queue.Count > 0)
        {
            int parallelCount = Math.Min(_queue.Count, _inputParams.ParallelProcessCount);

            List<Task> tasks = new();

            for (int i = 0; i < parallelCount; i++)
            {
                if (_queue.TryDequeue(out var parallelUrl))
                {
                    tasks.Add(DownloadResourceAsync(parallelUrl));
                }
            }

            await Task.WhenAll(tasks);
        }
    }

    private async Task DownloadDeferredResourcesAsync()
    {
        List<Task> deferredTasks = new();

        for (int i = 0; i < _deferredLinks.Count; i++)
        {
            deferredTasks.Add(DownloadResourceAsync(_deferredLinks.ElementAt(i).Key));

            if ((i + 1) % _inputParams.ParallelProcessCount == 0)
            {
                await Task.WhenAll(deferredTasks);
                deferredTasks.Clear();
            }
        }

        if (deferredTasks.Count > 0)
        {
            await Task.WhenAll(deferredTasks);
        }
    }

    private async Task LogSummaryAsync(TimeSpan processDuration)
    {
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, "SUMMARY", string.Empty);
        await _logger.LogAsync(MessageType.Normal, "=======", string.Empty);
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);

        await LogIssuesAsync("WARNINGS:", _warnings, MessageType.Warning);
        await LogIssuesAsync("ERRORS:", _errors, MessageType.Error);

        await _logger.LogAsync(MessageType.Normal,
            $"""

            Scraped Items: {_itemCount}
            Errors:        {_errors.Count}
            Warnings:      {_warnings.Count}
            Completed In:  {processDuration}
            """, string.Empty);
    }

    private async Task LogIssuesAsync(string title, IEnumerable<string> entries, MessageType messageType)
    {
        if(entries?.Any() != true)
        {
            return;
        }

        await _logger.LogAsync(messageType, Environment.NewLine, string.Empty);
        await _logger.LogAsync(messageType, "title:", string.Empty);

        foreach (string warning in entries)
        {
            await _logger.LogAsync(messageType, warning, string.Empty);
        }

        await _logger.LogAsync(messageType, Environment.NewLine, string.Empty);
    }

    private async Task DownloadResourceAsync(string url)
    {
        if (_visited.ContainsKey(url))
        {
            await _logger.LogInsignificantAsync($"The resource at '{url}' has already been visited.");
            return;
        }
        else
        {
            _visited.TryAdd(url, null);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri == null)
        {
            string message = $"Skipping downloading page because '{url}' is not a valid absolute url.";
            await _logger.LogWarningAsync(message);
            _warnings.Add(message);
            return;
        }

        if (uri.Host != _baseUri.Host)
        {
            string message = $"Skipping downloading page because '{url}' is not on the same domain as the base url.";
            await _logger.LogWarningAsync(message);
            _warnings.Add(message);
            return;
        }

        string destinationPath = _persistenceHelper.MapUriToLocalPath(_destinationRootPath, uri);

        if (destinationPath == _destinationRootPath)
        {
            destinationPath = Path.Combine(destinationPath, "index.html");
        }

        bool success = await DownloadAsync(url, uri, destinationPath);

        if (success)
        {
            QueueLinksFromFileAsync(destinationPath, uri);
        }
    }

    private async Task<bool> DownloadAsync(string url, Uri uri, string destinationPath)
    {
        try
        {
            using HttpClient client = new ();
            var response = await client.GetAsync(uri);

            HttpContent content = response.Content;

            if (!response.IsSuccessStatusCode)
            {
                string message = $"Failed to download {url}: Server returned: HTTP {(int)response.StatusCode}.";
                await _logger.LogErrorAsync(message);
                _errors.Add(message);
                return false;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync();
            await SaveResourceAsync(stream, destinationPath);

            Interlocked.Increment(ref _itemCount);  // thread-safe increment

            await _logger.LogSuccessAsync($"Downloaded item {_itemCount} from '{url}'.");
            return true;
        }
        catch (Exception ex)
        {
            string message = $"Failed to download {url}: {ex.Message}.";
            await _logger.LogErrorAsync(message);
            _errors.Add(message);
            return false;
        }
    }

    private void QueueLinksFromFileAsync(string path, Uri currentUri)
    {
        if(!_inputParams.IsHtmlFile(path))
        {
            _logger.LogInsignificantAsync($"Skipping '{path}' because it is not an html file.");
            return;
        }

        HtmlDocument doc = new();

        doc.Load(path); // TODO: Async? non-html?

        var pageLinks = doc.DocumentNode.Descendants("a")
            .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("href", null), currentUri)!)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();

        var deferredLinks = doc.DocumentNode.Descendants("link")
            .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("href", null), currentUri)!)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Union(doc.DocumentNode.Descendants("script")
                .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("src", null), currentUri)!)
                .Where(href => !string.IsNullOrWhiteSpace(href)))
            .Union(doc.DocumentNode.Descendants("img")
                .Select(a => ConvertToAbsoluteUrl(a.GetAttributeValue("src", null), currentUri)!)
                .Where(href => !string.IsNullOrWhiteSpace(href)));

        foreach (var link in deferredLinks)
        {
            _deferredLinks.TryAdd(link, null);
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

    private async Task SaveResourceAsync(Stream stream, string destinationPath)
    {
        if (_filesProcessed.ContainsKey(destinationPath))
        {
            await _logger.LogInsignificantAsync($"Already downloaded {destinationPath}");
            return;
        }
        else
        {
            _filesProcessed.TryAdd(destinationPath, null);
        }

        await _persistenceHelper.SaveFileAsync(stream, destinationPath);
    }
}
