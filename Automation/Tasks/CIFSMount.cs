using Cake.Common.Tools.Command;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;
using Mono.Unix.Native;

namespace Automation.Tasks;

internal sealed record class CIFSMountConfig(string RemoteHost, string Share, string UserName, string Password);

// Optionally mounts Bin/ directory to a CIFS share, workaround for slow .exe startup time in WSL2 directories.
public class CIFSMount : FrostingTask<Context>
{
    public const string CIFS_REMOTE_HOST = nameof(CIFS_REMOTE_HOST);
    public const string CIFS_SHARE = nameof(CIFS_SHARE);
    public const string CIFS_USERNAME = nameof(CIFS_USERNAME);
    public const string CIFS_PASSWORD = nameof(CIFS_PASSWORD);

    private static CIFSMountConfig? LoadConfig()
    {
        string? remoteHost = Environment.GetEnvironmentVariable(CIFS_REMOTE_HOST);
        string? share = Environment.GetEnvironmentVariable(CIFS_SHARE);
        string? userName = Environment.GetEnvironmentVariable(CIFS_USERNAME);
        string? password = Environment.GetEnvironmentVariable(CIFS_PASSWORD);
        if (share is null || userName is null || password is null)
        {
            return null;
        }
        return new CIFSMountConfig(
            RemoteHost: remoteHost ?? "host.docker.internal",
            Share: share,
            UserName: userName,
            Password: password
        );
    }

    public override void Run(Context context)
    {
        var config = LoadConfig();
        if (config is null)
        {
            context.Log.Information("CIFS share not configured, skipping.");
            return;
        }

        string mountPath = Context.LocalWindowsDirectory;
        try
        {
            if (DriveInfo.GetDrives().Any(drive => drive.DriveType is DriveType.Network && drive.Name == mountPath))
            {
                context.Log.Information("CIFS share already mounted at {0}, skipping.", mountPath);
                // Already mounted.
                return;
            }

            Directory.CreateDirectory(mountPath);
            uint uid = Syscall.getuid();
            uint gid = Syscall.getgid();
            context.Command(
                ["sudo"],
                ProcessArgumentBuilder
                    .FromStrings(["mount", "--types", "cifs", $"//{config.RemoteHost}/{config.Share}", mountPath])
                    .AppendSwitchSecret(
                        "--options",
                        $"username={config.UserName},password={config.Password},uid={uid},gid={gid}"
                    )
            );
        }
        catch (Exception ex)
        {
            context.Log.Warning("Failed to mount CIFS share: {0}", ex.Message);
        }
    }
}
