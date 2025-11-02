using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Channels;

string appPath = Path.GetDirectoryName(Environment.ProcessPath)!;
string testResultsDirectory = Path.Combine(appPath, "../TestResults");
string coverageSettingsPath = Path.Combine(appPath, "CoverageSettings.xml");
string coverageOutputPath = Path.Combine(testResultsDirectory, "coverage.cobertura.xml");
string testAppPath = Path.Combine(appPath, "../Divert.Windows.Tests");

Directory.SetCurrentDirectory(testAppPath);

Process LaunchTestProcess(bool redirect) =>
    Process.Start(
        new ProcessStartInfo()
        {
            FileName = Path.Combine(testAppPath, "Divert.Windows.Tests.exe"),
            ArgumentList =
            {
                "--results-directory",
                testResultsDirectory,
                "--coverage",
                "--coverage-settings",
                coverageSettingsPath,
                "--coverage-output-format",
                "cobertura",
                "--coverage-output",
                coverageOutputPath,
            },
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            Environment =
            {
                // MSTest should respect DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION instead.
                ["GITHUB_ACTIONS"] = "true", // Trick MSTest to output ANSI colors.
            },
        }
    )!;

if (args is not ["watch", ..])
{
    using var process = LaunchTestProcess(redirect: false);
    await process.WaitForExitAsync();
    return process.ExitCode;
}

Console.WriteLine("Starting Test Runner in watch mode...");
using var mutex = new Mutex(true, Assembly.GetExecutingAssembly().FullName, out bool createdNew);
if (!createdNew)
{
    Console.WriteLine("Another instance is already running. Exiting...");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var token = cts.Token;

using var listener = new TcpListener(IPAddress.Any, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
Console.WriteLine($"Listening on port {port}...");
using var lockFile = new FileStream(
    Path.Combine(appPath, "TestRunner.lock"),
    FileMode.Create,
    FileAccess.ReadWrite,
    FileShare.Read,
    bufferSize: 0,
    FileOptions.Asynchronous | FileOptions.DeleteOnClose
);
await lockFile.WriteAsync(Encoding.UTF8.GetBytes(port.ToString() + '\n'), token);
await lockFile.FlushAsync(token);

try
{
    while (!token.IsCancellationRequested)
    {
        Console.WriteLine("Waiting for client connection...");
        using var client = await listener.AcceptSocketAsync(token);
        Console.WriteLine("Received client connection.");
        using var stream = new NetworkStream(client, ownsSocket: false);
        using var process = LaunchTestProcess(redirect: true);
        using var _ = token.Register(() => process.Kill(entireProcessTree: true));

        var lines = Channel.CreateBounded<string>(1024);
        var stdOutReader = new StreamReader(process.StandardOutput.BaseStream);
        var stdErrReader = new StreamReader(process.StandardError.BaseStream);

        async Task ForwardLines(StreamReader reader)
        {
            string? line = null;
            while (true)
            {
                line = await reader.ReadLineAsync(token);
                if (line is null)
                {
                    break;
                }
                await lines.Writer.WriteAsync(line, token);
            }
        }
        var readStdOutTask = ForwardLines(stdOutReader);
        var readStdErrTask = ForwardLines(stdErrReader);

        using var writer = new StreamWriter(stream) { AutoFlush = true };
        var writeTask = Task.Run(
            async () =>
            {
                await foreach (var line in lines.Reader.ReadAllAsync(token))
                {
                    Console.WriteLine(line);
                    await writer.WriteLineAsync(line.AsMemory(), token);
                }
            },
            token
        );

        await process.WaitForExitAsync(token);
        lines.Writer.Complete();
        await Task.WhenAll(readStdOutTask, readStdErrTask, writeTask);
        await writer.WriteLineAsync(process.ExitCode.ToString().AsMemory(), token);
    }
}
catch (OperationCanceledException) when (token.IsCancellationRequested) { }

return 0;
