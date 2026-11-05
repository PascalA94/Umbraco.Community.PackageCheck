using PackageCheck.Cli;

namespace PackageCheck.Tests.Support;

public static class CliHarness
{
    public static async Task<(int Code, string Stdout)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApp.RunAsync(args, stdout, stderr);
        return (code, stdout.ToString());
    }
}
