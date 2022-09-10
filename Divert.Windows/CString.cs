using System.Runtime.InteropServices;

namespace Divert.Windows;

internal class CString : IDisposable
{
    internal IntPtr Ptr { get; }

    public CString(string str)
    {
        Ptr = Marshal.StringToHGlobalAnsi(str);
    }

    private bool disposed;

    public void Dispose()
    {
        if (!disposed)
        {
            Marshal.FreeHGlobal(Ptr);
            disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~CString()
    {
        Dispose();
    }
}
