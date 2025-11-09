using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Frosting;

namespace Automation;

public class Build : FrostingTask<Context>
{
    public override void Run(Context context)
    {
        context.CleanDirectory(Context.LocalWindowsDirectory);
        context.DotNetBuild(
            Context.ProjectRoot,
            new()
            {
                MSBuildSettings = new()
                {
                    TreatAllWarningsAs = MSBuildTreatAllWarningsAs.Error,
                    Properties =
                    {
                        ["DivertWindowsTests"] = ["true"], // Enable DivertValueTaskExecutorDelay
                    },
                },
            }
        );
    }
}
