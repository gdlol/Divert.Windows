using System.Net.Sockets;
using Cake.Common.Diagnostics;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Tool;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;
using Path = System.IO.Path;

namespace Automation;

public class Test : AsyncFrostingTask<Context>
{
    public const string TEST_HOST = nameof(TEST_HOST);
    public const string TestProjectName = "Divert.Windows.Tests";
    public const string TestRunnerProjectName = "Divert.Windows.TestRunner";

    public static string GetTestHost() => Environment.GetEnvironmentVariable(TEST_HOST) ?? "host.docker.internal";

    public static string TestResultsDirectory => Path.Combine(Context.LocalWindowsDirectory, "TestResults");

    public static string CoverletOutput => Path.Combine(TestResultsDirectory, "coverage.cobertura.xml");

    public static string TestReportsDirectory => Path.Combine(Context.LocalDirectory, "TestReports");

    public static string TestRunnerLockFilePath =>
        Path.Combine(Context.LocalWindowsDirectory, $"{TestRunnerProjectName}/TestRunner.lock");

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

    public override async Task RunAsync(Context context)
    {
        context.DotNetBuild(Path.Combine(Context.ProjectRoot, TestProjectName));
        if (!Directory.Exists(Path.Combine(Context.LocalWindowsDirectory, TestRunnerProjectName)))
        {
            context.DotNetBuild(Path.Combine(Context.ProjectRoot, TestRunnerProjectName));
        }

        context.Information($"Waiting for test runner lock file {TestRunnerLockFilePath}...");
        int port = 0;
        while (port is 0)
        {
            try
            {
                string text = await File.ReadAllTextAsync(TestRunnerLockFilePath);
                port = int.Parse(text);
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1000));
            }
        }

        using var client = new TcpClient();
        await client.ConnectAsync(GetTestHost(), port);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        string lastLine = string.Empty;
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }
            lastLine = line;
            Console.WriteLine(line);
        }
        if (!int.TryParse(lastLine, out int exitCode) || exitCode != 0)
        {
            throw new Exception("Tests failed");
        }
        GenerateReport(context);
    }
}

public class TestReport : FrostingTask<Context>
{
    public override void Run(Context context)
    {
        Test.GenerateReport(context);
    }
}
