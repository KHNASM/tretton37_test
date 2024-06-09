namespace SampleWebScrapper.Helpers;

internal interface IFilingHelper
{
    string MapUriToLocalPath(string rootDirectory, Uri uriToMapTo);

    Task SaveFileAsync(Stream stream, string destinationPath);

    (string ModifiedUrl, string ModifiedPath) ModifyPaths(string originalUrl, string originalPath);

    string CombinePaths(string path1, string path2);
}
