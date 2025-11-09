using System.IO.Pipes;

namespace Divert.Windows.Tests;

internal sealed class ExecutorDelayPipe : IDisposable
{
    private const string DIVERT_WINDOWS_TESTS = nameof(DIVERT_WINDOWS_TESTS);

    private readonly string name;

    public NamedPipeServerStream Stream { get; }

    public ExecutorDelayPipe()
    {
        name = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(DIVERT_WINDOWS_TESTS, name);
        Stream = new NamedPipeServerStream(name, PipeDirection.InOut);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DIVERT_WINDOWS_TESTS, null);
        Stream.Dispose();
    }
}
