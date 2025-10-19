using Microsoft.Win32.SafeHandles;

namespace Divert.Windows;

internal sealed class DivertHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal IntPtr Handle => handle;

    public DivertHandle(IntPtr handle)
        : base(ownsHandle: false)
    {
        this.handle = handle;
    }

    protected override bool ReleaseHandle() => true;
}
