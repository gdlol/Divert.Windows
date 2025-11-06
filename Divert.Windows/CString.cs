using System.Runtime.InteropServices;

namespace Divert.Windows;

internal sealed class CString(string str) : IDisposable
{
    internal IntPtr Ptr { get; } = Marshal.StringToHGlobalAnsi(str);

    private bool disposed;

    public void Dispose()
    {
        if (!disposed)
        {
            Marshal.FreeHGlobal(Ptr);
            disposed = true;
        }
    }
}
