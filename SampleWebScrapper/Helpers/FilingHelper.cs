using System.Text.RegularExpressions;

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

    public (string ModifiedUrl, string ModifiedPath) ModifyPaths(string originalUrl, string originalPath)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);

        string fileExtension = Path.GetExtension(originalPath);
        string randomString = Guid.NewGuid().ToString("N");
        string modifiedFileName = $"{fileNameWithoutExtension}_{randomString}{fileExtension}";
        string directory = Path.GetDirectoryName(originalPath)!;
        string modifiedFilePath = Path.Combine(directory, $"{modifiedFileName}");

        string pattern = @$"{fileNameWithoutExtension}.*$";
        string modifiedUrl = Regex.Replace(originalUrl, pattern, modifiedFileName);

        return (modifiedUrl, modifiedFilePath);
    }

    public string CombinePaths(string path1, string path2) => Path.Combine(path1, path2);
}
