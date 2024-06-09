using System.Text.RegularExpressions;

namespace SampleWebScrapper.Helpers;

internal static class CommandArgumentParser
{
    public static InputParams? Parse(string[] args)
    {
        if (args.Length == 0 || args.Length > 4)
        {
            return InputParams.CreateInvalid("Missing required arguments.");
        }

        string baseUrl = args[0].Trim();

        string? outputDirectory = null;
        int? parallelProcessCount = null;
        string? htmlExtensions = null;
        string? cssExtensions = null;
        bool? retryOnTimeout = null;

        if (args.Length > 1)
        {
            foreach (string arg in args[1..])
            {
                var match = Regex.Match(arg, @"^\s*od\s*:\s*(?<od>.*)\s*$", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    outputDirectory = match.Groups["od"].Value.Trim();
                    continue;
                }

                match = Regex.Match(arg, @"^\s*pc\s*:\s*(?<pc>\d{1,2})\s*$", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    parallelProcessCount = int.Parse(match.Groups["pc"].Value);
                    continue;
                }

                match = Regex.Match(arg, @"^\s*htm\s*:\s*(?<htm>[a-zA-Z0-9_\-,]+)\s*$", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    htmlExtensions = match.Groups["htm"].Value.Trim();
                    continue;
                }

                match = Regex.Match(arg, @"^\s*css\s*:\s*(?<css>[a-zA-Z0-9_\-,]+)\s*$", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    cssExtensions = match.Groups["css"].Value.Trim();
                    continue;
                }

                match = Regex.Match(arg, @"^\s*rto\s*:\s*(?<rto>true|false)\s*$", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    retryOnTimeout = bool.Parse(match.Groups["rto"].Value);
                    continue;
                }

                return InputParams.CreateInvalid($"Invalid argument: \"{arg}\"");
            }
        }

        if (Uri.IsWellFormedUriString(args[0], UriKind.Absolute))
        {
            var parameters = InputParams.CreateValid(baseUrl, outputDirectory, parallelProcessCount, htmlExtensions, cssExtensions, retryOnTimeout);

            string message = $"""

                1. If you choose to proceed, the following parameters will be used:

                   Base URL:                {parameters.BaseUrl}
                   Output Directory:        {parameters.OutputDirectory}
                   Parallel Process Count:  {parameters.ParallelProcessCount}
                   HTML Extensions:         {parameters.HtmlExtensions}
                   Stylesheet Extensions:   {parameters.CssExtensions}
                   Retry on Timeout:        {parameters.RetryOnTimeout}

                2. Any existing outputs will nbe overwritten.
                
                Are you sure you want to continue? [Y/N]:
                """;

            Console.Write(message);
            string? response = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(response) || !response.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.WriteLine("Operation cancelled by user.");
                return null;
            }

            return parameters;
        }

        return InputParams.CreateInvalid("<base-url> needs to be a valid absolute URL.");
    }
}

internal class InputParams
{
    private HashSet<string> _validHtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm"
    };

    private HashSet<string> _validStylesheetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css"
    };

    private const string DefaultOutputDirectory = "_SampleWebScrapper_Output";
    private const string DefaultHtmlExtensions = "html,htm";
    private const string DefaultCssExtensions = "css";

    private InputParams(
        string baseUrl,
        string? outputDirectory,
        int? parallelProcessCount,
        string? htmlExtensions,
        string? cssExtensions,
        bool? retryOnTimeout,
        string? errorMessage)
    {
        BaseUrl = baseUrl;
        OutputDirectory = outputDirectory ?? DefaultOutputDirectory;
        HtmlExtensions = htmlExtensions ?? DefaultHtmlExtensions;
        CssExtensions = cssExtensions ?? DefaultCssExtensions;
        ErrorMessage = BuildErrorMessage(errorMessage);
        RetryOnTimeout = retryOnTimeout ?? false;

        ParallelProcessCount = parallelProcessCount.HasValue
            ? parallelProcessCount.Value < 1 ? 1 : parallelProcessCount.Value
            : Environment.ProcessorCount;

        _validHtmlExtensions = new HashSet<string>(
            HtmlExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => $".{e.Trim(' ', '.')}"),
            StringComparer.OrdinalIgnoreCase);

        _validStylesheetExtensions = new HashSet<string>(
            CssExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => $".{e.Trim(' ', '.')}"),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? BuildErrorMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        return $"""
            {errorMessage}

            Usage: SampleWebScrapper <base-url> [od:<output-directory>] [pc:<parallel-count>] [htm:<html-extensions>] [rto:<retry-on-timeout>]

               <base-url>                  This is required parameter.
                                           The URL to start the web scraping from. 
                                           The URL must be an absolute URL.
            
               od:<output-directory>       This is optional parameter.
                                           The directory to save the downloaded files. 
                                           Default is "od:{DefaultOutputDirectory}".
            
               pc:<parallel-count>         This is optional parameter.
                                           The number of parallel processes to use. 
                                           A number less than 2 means no parallel processing.
                                           The number must be between 1 and 99.
                                           Default is "pc:{Environment.ProcessorCount}".

               htm:<html-extensions>       This is optional parameter.
                                           The file extensions to consider as HTML files.
                                           Value must be a comma separated list of extensions without the dot and spaces.
                                           Default is "htm:{DefaultHtmlExtensions}".

               rto:<retry-on-timeout>      This is optional parameter.
                                           A boolean (true/false) value indicating whether to retry the download on timeout.
                                           Default is "rto:false".
                                           WARNING: Setting this to true may cause the program to run indefinitely.

               Example: SampleWebScrapper "https://www.example.com" "od:C:\{DefaultOutputDirectory}" "pc:{Environment.ProcessorCount}" "htm:{DefaultHtmlExtensions}"
            """;
    }

    public static InputParams CreateInvalid(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException($"'{nameof(errorMessage)}' cannot be null or whitespace.", nameof(errorMessage));
        }

        return new InputParams(null!, null, null, null, null, null, errorMessage);
    }

    public static InputParams CreateValid(string baseUrl, string? outputDirectory, int? parallelProcessCount, string? htmlExtensions, string? cssExtensions, bool? retryOnTimeout)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException($"'{nameof(baseUrl)}' cannot be null or whitespace.", nameof(baseUrl));
        }

        outputDirectory ??= DefaultOutputDirectory;

        try
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            Directory.CreateDirectory(outputDirectory);

            return new InputParams(
                baseUrl: baseUrl,
                outputDirectory: outputDirectory,
                parallelProcessCount: parallelProcessCount,
                htmlExtensions: htmlExtensions,
                cssExtensions: cssExtensions,
                retryOnTimeout: retryOnTimeout,
                errorMessage: null);
        }
        catch (Exception ex)
        {
            return CreateInvalid($"Error creating output directory: {ex.Message}");
        }
    }

    public bool IsValid => string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsHtmlFile(string fileName) => _validHtmlExtensions.Contains(Path.GetExtension(fileName).Trim());

    public bool IsStylesheetFile(string fileName) => _validStylesheetExtensions.Contains(Path.GetExtension(fileName).Trim());

    public string BaseUrl { get; }

    public string OutputDirectory { get; }

    public string? ErrorMessage { get; }

    public int ParallelProcessCount { get; }

    public string HtmlExtensions { get; }

    public string CssExtensions { get; }

    public bool RetryOnTimeout { get; }
}