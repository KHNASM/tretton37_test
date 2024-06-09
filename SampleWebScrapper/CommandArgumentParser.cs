namespace SampleWebScrapper;

internal static class CommandArgumentParser
{
    public static InputParams Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return InputParams.CreateInvalid("Missing required arguments.");
        }

        string baseUrl = args[0].Trim();
        string? outputDirectory = "_SampleWebScrapper_Output";

        if (args.Length > 1)
        {
            outputDirectory = args[1].Trim();
        }


        if (Uri.IsWellFormedUriString(args[0], UriKind.Absolute))
        {
            return InputParams.CreateValid(baseUrl, outputDirectory);
        }
         
        return InputParams.CreateInvalid("<base-url> needs to be a valid absolute URL.");
    }
}

internal class InputParams
{
    private InputParams(string baseUrl, string outputDirectory, string? errorMessage)
    {
        BaseUrl = baseUrl;
        OutputDirectory = outputDirectory;
        ErrorMessage = BuildErrorMessage(errorMessage);
    }

    private static string? BuildErrorMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        return $"""
            {errorMessage}

            Usage: SampleWebScrapper <base-url> [<output-directory>]
            """;
    }

    public static InputParams CreateInvalid(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException($"'{nameof(errorMessage)}' cannot be null or whitespace.", nameof(errorMessage));
        }

        return new InputParams(null!, null!, errorMessage);
    }

    public static InputParams CreateValid(string baseUrl, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException($"'{nameof(baseUrl)}' cannot be null or whitespace.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException($"'{nameof(outputDirectory)}' cannot be null or whitespace.", nameof(outputDirectory));
        }

        try
        {
            if(Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            Directory.CreateDirectory(outputDirectory);
            return new InputParams(baseUrl, outputDirectory!, null);
        }
        catch (Exception ex)
        {
            return CreateInvalid($"Error creating output directory: {ex.Message}");
        }
    }

    public bool IsValid => string.IsNullOrWhiteSpace(ErrorMessage);

    public string BaseUrl { get; }

    public string OutputDirectory { get; }

    public string? ErrorMessage { get; }
}