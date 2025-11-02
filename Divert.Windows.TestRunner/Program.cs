using System.Diagnostics;

string appPath = Path.GetDirectoryName(Environment.ProcessPath)!;
string testResultsDirectory = Path.Combine(appPath, "../TestResults");
string coverageSettingsPath = Path.Combine(appPath, "CoverageSettings.xml");
string coverageOutputPath = Path.Combine(testResultsDirectory, "coverage.cobertura.xml");

Directory.SetCurrentDirectory(appPath);

using var process = Process.Start(
    new ProcessStartInfo
    {
        FileName = Path.Combine(appPath, "Divert.Windows.Tests.exe"),
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
    }
)!;
await process.WaitForExitAsync();
return process.ExitCode;
