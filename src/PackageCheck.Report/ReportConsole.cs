using Spectre.Console;

namespace PackageCheck.Report;

/// <summary>Creates the console the report is written to, honouring <c>NO_COLOR</c> (design 7.6, https://no-color.org).</summary>
public static class ReportConsole
{
    /// <summary>NO_COLOR disables colour when it is present and not empty (the no-color.org rule).</summary>
    public static bool NoColorRequested(Func<string, string?> getEnvironmentVariable) =>
        !string.IsNullOrEmpty(getEnvironmentVariable("NO_COLOR"));

    /// <param name="ansi">null detects the terminal; true forces ANSI with 256 colours (tests, and terminals that under-report).</param>
    public static IAnsiConsole Create(TextWriter writer, bool noColor, bool? ansi = null) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = ansi switch { true => AnsiSupport.Yes, false => AnsiSupport.No, null => AnsiSupport.Detect },
            ColorSystem = noColor ? ColorSystemSupport.NoColors
                : ansi == true ? ColorSystemSupport.EightBit
                : ColorSystemSupport.Detect,
            Interactive = InteractionSupport.No,
        });
}
