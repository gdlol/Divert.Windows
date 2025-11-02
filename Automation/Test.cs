using System.Threading.Channels;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Tool;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;
using Path = System.IO.Path;

namespace Automation;

internal static class Test
{
    public static string TestResultsDirectory => Path.Combine(Context.LocalWindowsDirectory, "TestResults");

    public static string CoverletOutput => Path.Combine(TestResultsDirectory, "coverage.cobertura.xml");

    public static string TestReportsDirectory => Path.Combine(Context.LocalDirectory, "TestReports");

    public static void GenerateReport(Context context)
    {
        string sourcePath = Path.Combine(Context.ProjectRoot, "Divert.Windows");
        // spell-checker: ignore sourcedirs targetdir reporttypes
        context.DotNetTool(
            "reportgenerator",
            new DotNetToolSettings
            {
                ArgumentCustomization = _ =>
                    ProcessArgumentBuilder.FromStrings(
                        [
                            "reportgenerator",
                            $"-reports:{CoverletOutput}",
                            $"-sourcedirs:{sourcePath}",
                            $"-targetdir:{TestReportsDirectory}",
                            "-reporttypes:Html;MarkdownSummary",
                        ]
                    ),
            }
        );
    }
}

public class TestReport : FrostingTask<Context>
{
    public override void Run(Context context)
    {
        Test.GenerateReport(context);
    }
}

public class TestReportWatch : AsyncFrostingTask<Context>
{
    public override async Task RunAsync(Context context)
    {
        var changed = Channel.CreateBounded<byte>(10);
        using var watcher = new FileSystemWatcher
        {
            Path = Context.LocalWindowsDirectory,
            Filter = "coverage.cobertura.xml",
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite,
        };
        watcher.Changed += (_, e) => changed.Writer.TryWrite(0);

        var poll = Task.Run(async () =>
        {
            while (changed.Writer.TryWrite(0))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1000));
            }
        });

        var exit = Task.Run(() =>
        {
            Console.ReadLine();
            context.Log.Information("Stopping coverage report watcher...");
            changed.Writer.TryComplete();
        });

        DateTimeOffset? lastChanged = null;
        await foreach (var _ in changed.Reader.ReadAllAsync())
        {
            try
            {
                var offset = new DateTimeOffset(File.GetLastWriteTime(Test.CoverletOutput));
                if (lastChanged != offset)
                {
                    lastChanged = offset;
                    context.Log.Information("Detected change to coverage file, regenerating report...");
                    Test.GenerateReport(context);
                }
            }
            catch (Exception e)
            {
                context.Log.Error("Error regenerating report: {0}", e);
            }
        }
        await Task.WhenAll(poll, exit);
        context.Log.Information("Coverage report watcher stopped.");
    }
}
