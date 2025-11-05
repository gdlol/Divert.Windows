using System.IO.Pipes;
using Divert.Windows.AsyncOperation;

namespace Divert.Windows.Tests;

internal sealed class ExecutorDelayPipe : IDisposable
{
    private readonly string name;

    public NamedPipeServerStream Stream { get; }

    public ExecutorDelayPipe()
    {
        name = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(nameof(DivertValueTaskExecutorDelay), name);
        Stream = new NamedPipeServerStream(name, PipeDirection.InOut);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(nameof(DivertValueTaskExecutorDelay), null);
        Stream.Dispose();
    }
}
