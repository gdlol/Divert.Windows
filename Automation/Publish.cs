using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.NuGet.Push;
using Cake.Frosting;

namespace Automation;

[IsDependentOn(typeof(Pack))]
public class Publish : FrostingTask<Context>
{
    public override void Run(Context context)
    {
        string package = Directory.GetFiles(Context.PackagesDirectory).Single();
        string apiKey =
            Environment.GetEnvironmentVariable("NUGET_API_KEY")
            ?? throw new InvalidOperationException("NUGET_API_KEY is not set.");
        string source = Environment.GetEnvironmentVariable("NUGET_SOURCE") ?? "https://api.nuget.org/v3/index.json";
        context.DotNetNuGetPush(
            package,
            new DotNetNuGetPushSettings
            {
                Source = source,
                ApiKey = apiKey,
                SkipDuplicate = true,
            }
        );
    }
}
