using PackageCheck.Cli;
using PackageCheck.Report;

var noColor = ReportConsole.NoColorRequested(Environment.GetEnvironmentVariable);
var env = new CliEnvironment(Console.Out, Console.Error, ReportConsole.Create(Console.Out, noColor), Handler: null, TimeProvider.System, CacheRoot: null);
return await CliApp.RunAsync(args, env);
