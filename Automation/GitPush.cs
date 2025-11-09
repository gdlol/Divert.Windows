using Cake.Frosting;
using LibGit2Sharp;
using Git = LibGit2Sharp;

namespace Automation;

public class GitPush : FrostingTask<Context>
{
    private const string GIT_TOKEN = nameof(GIT_TOKEN);

    public override void Run(Context context)
    {
        string token =
            Environment.GetEnvironmentVariable(GIT_TOKEN)
            ?? throw new InvalidOperationException($"Environment variable {GIT_TOKEN} is not set.");
        using var repo = new Git.Repository(Context.ProjectRoot);
        string currentBranch = repo.Head.FriendlyName;

        var remote = repo.Network.Remotes["origin"] ?? throw new InvalidOperationException();
        var pushRefSpec = $"+refs/heads/{currentBranch}:refs/heads/{currentBranch}";
        PushStatusError? error = null;
        var options = new Git.PushOptions
        {
            CredentialsProvider = (_, _, _) =>
                new Git.UsernamePasswordCredentials { Username = "git", Password = token },
            OnPushStatusError = (pushStatusErrors) => error = pushStatusErrors,
        };
        repo.Network.Push(remote, pushRefSpec, options);
        if (error is not null)
        {
            throw new InvalidOperationException(
                $"Error pushing to remote. Reference: {error.Reference}, Message: {error.Message}"
            );
        }
    }
}
