namespace PackageCheck.Cli;

// Placeholder behaviour for the ID-reserving 0.0.1-alpha (design 10); replaced in Task 28a.
public static class CliApp
{
    public const string ComingSoon =
        "Umbraco Package Check: coming soon, see https://github.com/PascalA94/Umbraco.Community.PackageCheck";

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        await stdout.WriteLineAsync(ComingSoon);
        return 2;
    }
}
