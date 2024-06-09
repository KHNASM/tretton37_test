namespace SampleWebScrapper.Helpers;

internal class FilingHelper : IFilingHelper
{
    public string MapUriToLocalPath(string rootDirectory, Uri uriToMapTo)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException($"'{nameof(rootDirectory)}' cannot be null or whitespace.", nameof(rootDirectory));
        }

        ArgumentNullException.ThrowIfNull(uriToMapTo);

        if (!uriToMapTo.IsAbsoluteUri)
        {
            throw new ArgumentException($"'{nameof(uriToMapTo)}' must be an absolute uri", nameof(uriToMapTo));
        }

        string localPath = uriToMapTo.LocalPath
            .Trim('/', '\\')
            .Replace('/', '\\');

        localPath = Path.Combine(rootDirectory, localPath);

        return localPath;
    }

    public async Task SaveFileAsync(Stream stream, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException($"'{nameof(destinationPath)}' cannot be null or whitespace.", nameof(destinationPath));
        }

        string destinationDirectory = Path.GetDirectoryName(destinationPath)!;

        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var fileStream = File.Create(destinationPath);
        await stream.CopyToAsync(fileStream);
    }
}
