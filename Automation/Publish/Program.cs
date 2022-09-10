using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.DotNet.Cli.Utils;

static string GetFilePath([CallerFilePath] string? path = null)
{
    if (path is null)
    {
        throw new InvalidOperationException(nameof(path));
    }
    return path;
}

static void Run(string commandName, params string[] args)
{
    var command = Command.Create(commandName, args);
    Console.WriteLine($"{commandName} {command.CommandArgs}");
    var result = command.Execute();
    if (result.ExitCode != 0)
    {
        throw new Win32Exception(result.ExitCode);
    }
}

string version = args.Length > 0 ? args[0] : "1.0.0";

string filePath = GetFilePath();
string projectPath = new FileInfo(filePath).Directory?.Parent?.Parent?.FullName!;
Console.WriteLine($"Project path: {projectPath}");

string publishPath = Path.Combine(projectPath, "Publish");
if (Directory.Exists(publishPath))
{
    Directory.Delete(publishPath, recursive: true);
}

string userName = Command.Create("git", new[] { "config", "user.name" }).CaptureStdOut().Execute().StdOut.Trim();
Console.WriteLine($"{nameof(userName)}: {userName}");
string projectName = "Divert.Windows";
string description = "WinDivert .NET APIs.";

Run("dotnet", "pack",
    Path.Combine(projectPath, projectName, $"{projectName}.csproj"),
    "--configuration", "Release",
    "--output", publishPath,
    $"-property:PackageVersion={version}",
    $"-property:Authors={userName}",
    $"-property:PackageDescription={description}");
