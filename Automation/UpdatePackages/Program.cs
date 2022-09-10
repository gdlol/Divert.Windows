using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;

static string GetFilePath([CallerFilePath] string? path = null)
{
    if (path is null)
    {
        throw new InvalidOperationException(nameof(path));
    }
    return path;
}

string filePath = GetFilePath();
string workspacePath = new FileInfo(filePath).Directory?.Parent?.Parent?.FullName!;
Console.WriteLine($"Workspace path: {workspacePath}");

// Load package versions.
string packagePropsFileName = "Directory.Packages.props";
var packageVersions = await Task.Run(() =>
{
    string packagePropsPath = Path.Combine(Path.GetDirectoryName(filePath)!, packagePropsFileName);
    var props = ProjectRootElement.Open(packagePropsPath);
    var result = new Dictionary<string, string>();
    foreach (var item in props.Items)
    {
        if (item is
            {
                ElementName: "PackageVersion",
                Include: string packageName,
                FirstChild: ProjectMetadataElement
                {
                    Name: "Version",
                    Value: string version
                }
            })
        {
            result.Add(packageName, version);
        }
    }
    return result;
});

// Update package versions in .csproj files.
var foundPackages = new HashSet<string>();
foreach (var csProjectPath in Directory.EnumerateFiles(workspacePath, "*.csproj", SearchOption.AllDirectories))
{
    Console.WriteLine(csProjectPath);
    var projectRoot = ProjectRootElement.Open(csProjectPath, new(), preserveFormatting: true);

    foreach (var item in projectRoot.Items)
    {
        if (item is
            {
                ElementName: "PackageReference",
                Include: string packageName,
                FirstChild: ProjectMetadataElement packageVersion and
                {
                    Name: "Version",
                    Value: string version
                }
            })
        {
            if (packageVersions.TryGetValue(packageName, out string? specifiedVersion))
            {
                foundPackages.Add(packageName);
                if (version != specifiedVersion)
                {
                    Console.WriteLine($"Updating {packageName} version from {version} to {specifiedVersion}.");
                    packageVersion.Value = specifiedVersion;
                }
            }
            else
            {
                Console.WriteLine($"{packageName} version is not specified in {packagePropsFileName}.");
            }
        }
    }

    projectRoot.Save();
    Console.WriteLine();
}

foreach (var packageName in packageVersions.Keys.Except(foundPackages).ToImmutableSortedSet())
{
    Console.WriteLine($"Package {packageName} is not referenced.");
}

Console.WriteLine("Done.");
