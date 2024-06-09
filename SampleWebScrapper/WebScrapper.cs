using HtmlAgilityPack;

namespace SampleWebScrapper;

internal class WebScrapper
{
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _completed = new();

    private readonly Uri _baseUri;
    private readonly string _startingUrl;

    private readonly string _outputRootDirectory;


    public WebScrapper(string url, string outputRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException($"'{nameof(url)}' cannot be null or whitespace.", nameof(url));
        }

        _outputRootDirectory = string.IsNullOrWhiteSpace(outputRootDirectory)
            ? "_SampleWebscrapperOutput"
            : outputRootDirectory;


        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? uri) || uri == null || !uri.IsAbsoluteUri)
        {
            throw new ArgumentException($"'{nameof(url)}' must be a valid absolute url.", nameof(url));
        }

        string baseUrl = uri.GetLeftPart(UriPartial.Authority);

        _baseUri = new Uri(baseUrl);
        _startingUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/', '\\');
    }

    public async Task RunScraperAsync()
    {
        _queue.Enqueue(_startingUrl);

        while (_queue.TryDequeue(out var url))
        {
            await DownloadPage(url);
        }
    }

    private async Task DownloadPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri == null)
        {
            await WriteLineAsync($"X X X X X X Unable to download page becase '{url}' is not a valid absolute url"); // TODO: revisit to improve this
            return;
        }

        if (uri.Host != _baseUri.Host)
        {
            await WriteLineAsync($"X X X X X X Unable to download page becase '{url}' is not on the same domain as the base url"); // TODO: revisit to improve this
            return;
        }

        string localPath = ComputePath(_outputRootDirectory, uri);
        if (localPath == _outputRootDirectory) // TODO: revisit to improve this
        {
            localPath = Path.Combine(localPath, "index.html");
        }
        if (File.Exists(localPath))
        {
            await WriteLineAsync($"I I I I Page already downloaded: {url}"); // TODO: revisit to improve this
            return;
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

        QueueLinksFromFileAsync(localPath, uri);
    }

    private void QueueLinksFromFileAsync(string path, Uri currentUri)
    {
        HtmlDocument doc = new();

        doc.Load(path); // TODO: Async? non-html?

        var pageLinks = doc.DocumentNode.Descendants("a")
            .Select(a => a.GetAttributeValue("href", null))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();

        var linkedLinks = doc.DocumentNode.Descendants("link")
            .Select(a => a.GetAttributeValue("href", null))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();

        var scriptLinks = doc.DocumentNode.Descendants("script")
            .Select(a => a.GetAttributeValue("src", null))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();

        var imageLinks = doc.DocumentNode.Descendants("img")
            .Select(a => a.GetAttributeValue("src", null))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();

        var links = pageLinks
            .Union(linkedLinks)
            .Union(scriptLinks)
            .Union(imageLinks);

        foreach (string link in links)
        {
            if (!Uri.TryCreate(link, UriKind.RelativeOrAbsolute, out Uri? uri) || uri == null)
            {
                continue;
            }
            
            if (!uri.IsAbsoluteUri)
            {
                uri = new Uri(currentUri, uri);
            }

            if (uri.IsAbsoluteUri && uri.Host != _baseUri.Host)
            {
                continue;
            }

            string absoluteUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/', '\\');

            _queue.Enqueue(absoluteUrl);
        }
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

    private static async Task SaveStreamAsFileAsync(Stream stream, string localPath)
    {
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
