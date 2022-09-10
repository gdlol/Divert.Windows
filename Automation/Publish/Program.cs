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

static string GetOutput(string commandName, params string[] args)
{
    var command = Command.Create(commandName, args).CaptureStdOut();
    var result = command.Execute();
    if (result.ExitCode != 0)
    {
        throw new Win32Exception(result.ExitCode);
    }
    return result.StdOut;
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

// Get metadata from Git.
string userName = GetOutput("git", "config", "user.name").Trim();
var remotes = GetOutput("git", "remote").Trim();
string? repositoryUrl = remotes.Split('\n').FirstOrDefault() switch
{
    null or "" => null,
    string remote => GetOutput("git", "remote", "get-url", remote.Trim()).Trim()
};
Console.WriteLine($"{nameof(userName)}: {userName}");
Console.WriteLine($"{nameof(repositoryUrl)}: {repositoryUrl}");
string projectName = "Divert.Windows";
string description = "WinDivert .NET APIs.";

var arguments = new List<string>
{
    "pack",
    Path.Combine(projectPath, projectName, $"{projectName}.csproj"),
    "--configuration", "Release",
    "--output", publishPath,
    $"-property:PackageVersion={version}",
    $"-property:Authors={userName}",
    $"-property:PackageDescription={description}",
    "-property:PackageLicenseExpression=MIT",
    "-property:PackageRequireLicenseAcceptance=true",
    "-property:PackageTags=WinDivert",
};
if (repositoryUrl is not null)
{
    arguments.Add($"-property:RepositoryUrl={repositoryUrl}");
}
Run("dotnet", arguments.ToArray());
