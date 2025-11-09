using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Frosting;
using Git = LibGit2Sharp;

namespace Automation;

public class Pack : FrostingTask<Context>
{
    public override void Run(Context context)
    {
        context.CleanDirectory(Context.PackagesDirectory);

        using var repository = new Git.Repository(Context.ProjectRoot);
        string authors = repository.Config.Get<string>("user.name").Value;

        context.DotNetPack(
            Path.Combine(Context.ProjectRoot, "Divert.Windows"),
            new()
            {
                MSBuildSettings = new()
                {
                    Properties =
                    {
                        ["ReadMePath"] = [Path.Combine(Context.ProjectRoot, "ReadMe.md")],
                        ["PackageOutputPath"] = [Context.PackagesDirectory],
                        ["Authors"] = [authors],
                        ["PackageDescription"] = ["High quality .NET APIs for WinDivert."],
                        ["PackageLicenseExpression"] = ["LGPL-3.0-only"],
                        ["PackageRequireLicenseAcceptance"] = ["true"],
                        ["PackageTags"] = ["WinDivert divert networking packet capture"],
                        ["PackageReadmeFile"] = ["ReadMe.md"],
                    },
                },
            }
        );
    }
}
