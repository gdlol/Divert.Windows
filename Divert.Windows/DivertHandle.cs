using Microsoft.Win32.SafeHandles;

namespace Divert.Windows;

internal sealed class DivertHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DivertHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        this.handle = handle;
    }

    protected override bool ReleaseHandle() => NativeMethods.WinDivertClose(handle);
}
