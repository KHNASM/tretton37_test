using HtmlAgilityPack;
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
    private readonly string _outputRootDirectory;
    private readonly IOutputLogger _logger;

    private volatile int _itemCount = 0; // volatile to prevent compiler optimizations

    public WebScrapper(InputParams inputParams, IOutputLogger logger)
    {
        ArgumentNullException.ThrowIfNull(inputParams);

        if (!inputParams.IsValid)
        {
            throw new ArgumentException(inputParams.ErrorMessage, nameof(inputParams));
        }

        _outputRootDirectory = inputParams.OutputDirectory;

        if (!Uri.TryCreate(inputParams.BaseUrl, UriKind.RelativeOrAbsolute, out Uri? uri) || uri == null || !uri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                $"'{nameof(inputParams)}.{inputParams.BaseUrl}' must be a valid absolute url.",
                $"{nameof(inputParams)}.{inputParams.BaseUrl}");
        }

        string baseUrl = uri.GetLeftPart(UriPartial.Authority);

        _baseUri = new Uri(baseUrl);
        _startingUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/', '\\');
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunScraperAsync()
    {
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, $"Starting scrapping {_baseUri} recursively into the directory '{_outputRootDirectory}'. . .");
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);

        Stopwatch stopwatch = Stopwatch.StartNew();


        _queue.Enqueue(_startingUrl);

        while (_queue.Count > 0)
        {
            int parallelCount = Math.Min(_queue.Count, Environment.ProcessorCount);

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

        List<Task> deferredTasks = new();

        for (int i = 0; i < _deferredLinks.Count; i++)
        {
            deferredTasks.Add(DownloadResourceAsync(_deferredLinks.ElementAt(i).Key, false));

            if ((i + 1) % Environment.ProcessorCount == 0)
            {
                await Task.WhenAll(deferredTasks);
                deferredTasks.Clear();
            }
        }

        if (deferredTasks.Count > 0)
        {
            await Task.WhenAll(deferredTasks);
        }

        stopwatch.Stop();

        await LogSummary(stopwatch.Elapsed);
    }

    private async Task LogSummary(TimeSpan processDuration)
    {
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, "SUMMARY", string.Empty);
        await _logger.LogAsync(MessageType.Normal, "=======", string.Empty);
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);

        MessageType messageType = MessageType.Success;

        if (_warnings.Count > 0)
        {
            messageType = MessageType.Warning;

            await _logger.LogAsync(messageType, Environment.NewLine, string.Empty);

            await _logger.LogAsync(messageType, "WARNINGS:", string.Empty);

            foreach (string warning in _warnings)
            {
                await _logger.LogAsync(messageType, warning, string.Empty);
            }

            await _logger.LogAsync(messageType, Environment.NewLine, string.Empty);
        }

        if (_errors.Count > 0)
        {
            messageType = MessageType.Error;

            await _logger.LogAsync(messageType, Environment.NewLine, string.Empty);

            await _logger.LogAsync(messageType, "ERRORS:", string.Empty);

            foreach (string error in _errors)
            {
                await _logger.LogAsync(messageType, error, string.Empty);
            }

            await _logger.LogAsync(messageType, Environment.NewLine, string.Empty);
        }

        await _logger.LogAsync(messageType,
            $"""


            Scraped Items: {_itemCount}
            Errors:        {_errors.Count}
            Warnings:      {_warnings.Count}
            Completed In:  {processDuration}
            """, string.Empty);
    }

    private async Task DownloadResourceAsync(string url, bool drilldown = true)
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

        string localPath = ComputePath(_outputRootDirectory, uri);

        if (localPath == _outputRootDirectory) // TODO: revisit to improve this
        {
            localPath = Path.Combine(localPath, "index.html");
        }

        bool success = await DownloadAsync(url, uri, localPath);

        if (success && drilldown)
        {
            QueueLinksFromFileAsync(localPath, uri);
        }
    }

    private async Task<bool> DownloadAsync(string url, Uri uri, string localPath)
    {
        try
        {
            using HttpClient client = new HttpClient();
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
            await SaveStreamAsFileAsync(stream, localPath);

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
            await _logger.LogInsignificantAsync($"Already downloaded {localPath}");
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
}
