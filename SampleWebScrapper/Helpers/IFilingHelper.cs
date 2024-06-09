namespace SampleWebScrapper.Helpers;

internal interface IFilingHelper
{
    string MapUriToLocalPath(string rootDirectory, Uri uriToMapTo);

    Task SaveFileAsync(Stream stream, string destinationPath);
}
