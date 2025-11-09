using System.Runtime.CompilerServices;
using Cake.Core;
using Cake.Frosting;

namespace Automation;

public class Context(ICakeContext context) : FrostingContext(context)
{
    static string GetFilePath([CallerFilePath] string? path = null) => path!;

    public static string ProjectRoot => new FileInfo(GetFilePath()).Directory!.Parent!.FullName;

    public static string Workspaces => new DirectoryInfo(ProjectRoot).Parent!.FullName;

    public static string LocalDirectory => Path.Combine(ProjectRoot, ".local");

    public static string LocalWindowsDirectory => Path.Combine(LocalDirectory, "windows");

    public static string PackagesDirectory => Path.Combine(LocalDirectory, "packages");
}
