using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

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

    public static bool DecrementTtl(Span<byte> packet)
    {
        fixed (byte* pPacket = packet)
        {
            return NativeMethods.WinDivertHelperDecrementTTL(pPacket, (uint)packet.Length);
        }
    }

    public static ReadOnlySpan<byte> CompileFilter(
        DivertFilter filter,
        DivertLayer layer,
        int bufferLength = ushort.MaxValue
    )
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var s = new CString(filter.Clause);
        Span<byte> buffer = GC.AllocateArray<byte>(bufferLength, pinned: true);
        var pBuffer = Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer));
        IntPtr errorStr;
        uint errorPos;
        bool success = NativeMethods.WinDivertHelperCompileFilter(
            s.Pointer,
            (WINDIVERT_LAYER)layer,
            (byte*)pBuffer,
            (uint)buffer.Length,
            &errorStr,
            &errorPos
        );
        if (!success)
        {
            string? errorString = Marshal.PtrToStringAnsi(errorStr);
            throw new ArgumentException(
                $"{errorString} ({errorPos}): ...{filter.Clause[(int)errorPos..]}",
                nameof(filter)
            );
        }

        return buffer;
    }

    private static bool EvaluateFilter(IntPtr filter, ReadOnlySpan<byte> packet, in DivertAddress address)
    {
        fixed (byte* pPacket = packet)
        fixed (DivertAddress* pAddress = &address)
        {
            bool success = NativeMethods.WinDivertHelperEvalFilter(
                filter,
                pPacket,
                (uint)packet.Length,
                (WINDIVERT_ADDRESS*)pAddress
            );
            if (!success)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is not (int)WIN32_ERROR.ERROR_SUCCESS)
                {
                    throw new Win32Exception(error);
                }
            }
            return success;
        }
    }

    public static bool EvaluateFilter(ReadOnlySpan<byte> filter, ReadOnlySpan<byte> packet, in DivertAddress address)
    {
        fixed (byte* pFilter = filter)
        fixed (byte* pPacket = packet)
        fixed (DivertAddress* pAddress = &address)
        {
            return EvaluateFilter(new IntPtr(pFilter), packet, address);
        }
    }

    public static bool EvaluateFilter(DivertFilter filter, ReadOnlySpan<byte> packet, in DivertAddress address)
    {
        using var s = new CString(filter.Clause);
        fixed (byte* pPacket = packet)
        fixed (DivertAddress* pAddress = &address)
        {
            return EvaluateFilter(s.Pointer, packet, address);
        }
    }

    public static string FormatFilter(Span<byte> filter, DivertLayer layer, int maxLength = ushort.MaxValue)
    {
        Span<byte> buffer = GC.AllocateArray<byte>(maxLength, pinned: true);
        var pBuffer = Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer));
        fixed (byte* pFilter = filter)
        {
            bool success = NativeMethods.WinDivertHelperFormatFilter(
                new(pFilter),
                (WINDIVERT_LAYER)layer,
                (byte*)pBuffer,
                (uint)buffer.Length
            );
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return Marshal.PtrToStringAnsi(new IntPtr(pBuffer))!;
        }
    }
}
