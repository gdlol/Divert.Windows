using System.Runtime.InteropServices;

namespace Divert.Windows;

internal readonly struct SafeHandleReference<T>(T safeHandle) : IDisposable
    where T : SafeHandle
{
    public void Dispose()
    {
        safeHandle.DangerousRelease();
    }
}

internal static class SafeHandleExtensions
{
    public static SafeHandleReference<T> Reference<T>(this T safeHandle, out IntPtr handle)
        where T : SafeHandle
    {
        bool success = false;
        safeHandle.DangerousAddRef(ref success);
        handle = safeHandle.DangerousGetHandle();
        return new SafeHandleReference<T>(safeHandle);
    }
}
