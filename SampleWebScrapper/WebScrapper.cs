using HtmlAgilityPack;
using SampleWebScrapper.Helpers;
using SampleWebScrapper.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SampleWebScrapper;

internal class WebScrapper
{
    private const int WSAETIMEDOUT = 10060; //https://learn.microsoft.com/en-us/windows/win32/winsock/windows-sockets-error-codes-2

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
    private readonly IFilingHelper _filingHelper;
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
        _filingHelper = persistenceHelper;
    }

    public async Task RunScraperAsync()
    {
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);
        await _logger.LogAsync(MessageType.Normal, $"Starting scrapping {_baseUri} recursively into the directory '{_destinationRootPath}'. . .");
        await _logger.LogAsync(MessageType.Normal, Environment.NewLine, string.Empty);

        Stopwatch stopwatch = Stopwatch.StartNew();

        await _logger.LogEmphasisAsync($"+ Enqueuing '{_startingUrl}' for processing...");
        _queue.Enqueue(_startingUrl);

        while (true)
        {
            await ScrapeCoreResourcesAsync();
            await DownloadDeferredResourcesAsync();

            if (_queue.Count == 0)
            {
                break;
            }

            await _logger.LogEmphasisAsync($"Restarting to process {_queue.Count} requeued resources...");

            _deferredLinks.Clear();
        }

        stopwatch.Stop();

        await LogSummaryAsync(stopwatch.Elapsed);
    }

    private async Task ScrapeCoreResourcesAsync()
    {
        await _logger.LogEmphasisAsync("Starting scraping...");

        while (_queue.Count > 0)
        {
            int parallelCount = Math.Min(_queue.Count, _inputParams.ParallelProcessCount);

            List<Task> tasks = new();

            for (int i = 0; i < parallelCount; i++)
            {
                if (_queue.TryDequeue(out var parallelUrl))
                {
                    await _logger.LogNormalAsync($"- Dequeued '{parallelUrl}' for processing...");

                    tasks.Add(DownloadResourceAsync(parallelUrl));
                }
            }

            await Task.WhenAll(tasks);
        }
    }

    private async Task DownloadDeferredResourcesAsync()
    {
        await _logger.LogEmphasisAsync("Starting downloading deferred resources...");

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

        _deferredLinks.Clear();
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
        if (entries?.Any() != true)
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
            await _logger.LogInsignificantAsync($"Skipping '{url}' because it has already been visited.");
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

        if (!string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            string message = $"Skipping downloading page because '{url}' is not on the same domain as the base url.";
            await _logger.LogWarningAsync(message);
            _warnings.Add(message);
            return;
        }

        string destinationPath = _filingHelper.MapUriToLocalPath(_destinationRootPath, uri);

        if (destinationPath == _destinationRootPath)
        {
            destinationPath = _filingHelper.CombinePaths(destinationPath, "index.html");
        }

        bool success = await DownloadAsync(url, destinationPath);

        if (success)
        {
            await DrilldownForFurtherLinks(destinationPath, uri);
        }
    }

    private async Task<bool> DownloadAsync(string url, string destinationPath)
    {
        try
        {
            using HttpClient client = new();
            var response = await client.GetAsync(url);

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
            bool isSocketTimeout =
                ex is HttpRequestException httpEx
                &&
                httpEx.InnerException is SocketException socketEx
                && (
                    socketEx.ErrorCode == WSAETIMEDOUT
                    ||
                    socketEx.SocketErrorCode == SocketError.TimedOut
                    ||
                    socketEx.NativeErrorCode == WSAETIMEDOUT);

            if (isSocketTimeout && _inputParams.RetryOnTimeout)
            {
                string retryMessage = $"Failed to download '{url}' due to socket timeout.";
                await _logger.LogErrorAsync(retryMessage);
                RequeueForProcessingUrl(url);
                return false;
            }

            string message = $"Failed to download {url}: {ex.Message}.";
            await _logger.LogErrorAsync(message);
            _errors.Add(message);
            return false;
        }
    }

    private async void RequeueForProcessingUrl(string url)
    {
        await _logger.LogEmphasisAsync($"Requeuing '{url}' for processing...");

        _visited.TryRemove(url, out _);
        _queue.Enqueue(url);
    }

    private async Task DrilldownForFurtherLinks(string path, Uri currentUri)
    {
        if (_inputParams.IsHtmlFile(path))
        {
            await DrilldownHtmlForFurtherLinksAsync(path, currentUri);
        }
        else if (_inputParams.IsStylesheetFile(path))
        {
            await DrilldownStylesheetForFurtherLinksAsync(path, currentUri);
        }
        else
        {
            await _logger.LogInsignificantAsync($"Skipping '{path}' because it is not file type configured for drilldown.");
        }
    }

    private async Task DrilldownHtmlForFurtherLinksAsync(string path, Uri currentUri)
    {
        if (!_inputParams.IsHtmlFile(path))
        {
            await _logger.LogInsignificantAsync($"Skipping '{path}' because it is not an html file.");
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

    private async Task DrilldownStylesheetForFurtherLinksAsync(string path, Uri currentUri)
    {
        if (!_inputParams.IsStylesheetFile(path))
        {
            await _logger.LogInsignificantAsync($"Skipping '{path}' because it is not a stylesheet file.");
            return;
        }

        string[] allLines = await File.ReadAllLinesAsync(path);

        for (int i = 0; i< allLines.Length; i++)
        {
            string line = allLines[i];
        
            var matches = Regex.Matches(line, @"url\s*\(\s*'(?<url>.*?)\s*'\s*\)", RegexOptions.IgnoreCase)
                 .Where(m => m.Success)
                 .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            string outputLine = line;

            foreach (var match in matches)
            {
                string originalUrl = match.Groups["url"].Value;

                if (!Uri.TryCreate(originalUrl, UriKind.RelativeOrAbsolute, out Uri? originalUri) || originalUri == null)
                {
                    continue;
                }

                Uri absoluteUri = originalUri;

                if (!originalUri.IsAbsoluteUri)
                {
                    absoluteUri = new Uri(currentUri, originalUri);
                }

                if(!string.Equals(absoluteUri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    await _logger.LogInsignificantAsync($"Skipping '{originalUrl}' because it is not on the same domain as the base url.");
                    continue;
                }

                Uri purgedUri = new (_baseUri, absoluteUri.LocalPath);

                string destinationPath = _filingHelper.MapUriToLocalPath(_destinationRootPath, purgedUri);

                if (string.IsNullOrWhiteSpace(purgedUri.Query))
                {
                    string? absoluteOriginalUrl = ConvertToAbsoluteUrl(originalUrl, currentUri);

                    if (!string.IsNullOrWhiteSpace(absoluteOriginalUrl))
                    {
                        await DownloadAsync(absoluteOriginalUrl, destinationPath);
                    }
                }
                else
                {
                    var (modifiedUrl, modifiedFilePath) = _filingHelper.ModifyPaths(originalUrl, destinationPath);    

                    string? absoluteOriginalUrl = ConvertToAbsoluteUrl(originalUrl, currentUri);

                    if (!string.IsNullOrWhiteSpace(absoluteOriginalUrl))
                    {
                        bool result = await DownloadAsync(absoluteOriginalUrl, modifiedFilePath);

                        if (result)
                        {
                            outputLine = outputLine.Replace(originalUrl, modifiedUrl);
                        }
                    }
                }
            }

            allLines[i] = outputLine;
        }

        await File.WriteAllLinesAsync(path, allLines);
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

        if (uri.IsAbsoluteUri && !string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
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

        await _filingHelper.SaveFileAsync(stream, destinationPath);
    }
}
