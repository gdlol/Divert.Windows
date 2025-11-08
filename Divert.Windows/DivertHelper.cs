using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Divert.Windows;

public static unsafe class DivertHelper
{
    public static bool CalculateChecksums(Span<byte> packet, DivertHelperFlags flags = DivertHelperFlags.None)
    {
        fixed (byte* pPacket = packet)
        {
            return NativeMethods.WinDivertHelperCalcChecksums(pPacket, (uint)packet.Length, null, (ulong)flags);
        }
    }

    public static bool CalculateChecksums(
        Span<byte> packet,
        ref DivertAddress address,
        DivertHelperFlags flags = DivertHelperFlags.None
    )
    {
        fixed (byte* pPacket = packet)
        fixed (DivertAddress* pAddress = &address)
        {
            return NativeMethods.WinDivertHelperCalcChecksums(
                pPacket,
                (uint)packet.Length,
                (WINDIVERT_ADDRESS*)pAddress,
                (ulong)flags
            );
        }
    }

    public static string FormatFilter(Span<byte> filter, DivertLayer layer)
    {
        Memory<byte> buffer = GC.AllocateArray<byte>(ushort.MaxValue, pinned: true);
        using var bufferHandle = buffer.Pin();
        fixed (byte* pFilter = filter)
        {
            bool success = NativeMethods.WinDivertHelperFormatFilter(
                new(pFilter),
                (WINDIVERT_LAYER)layer,
                (byte*)bufferHandle.Pointer,
                (uint)buffer.Length
            );
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return Marshal.PtrToStringAnsi(new IntPtr(bufferHandle.Pointer))!;
        }
    }
}
