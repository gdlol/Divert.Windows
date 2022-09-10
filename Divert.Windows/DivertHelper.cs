using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Divert.Windows;

unsafe public static class DivertHelper
{
    public static void CalculateChecksums(Span<byte> packet, DivertHelperFlags flags)
    {
        fixed (byte* pPacket = packet)
        {
            bool success = NativeMethods.WinDivertHelperCalcChecksums(
                pPacket, (uint)packet.Length,
                null,
                (ulong)flags);
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }
}
