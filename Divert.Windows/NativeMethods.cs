using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Divert.Windows;

unsafe internal static class NativeMethods
{
    private const string dllName = "WinDivert.dll";

    [DllImport(dllName, SetLastError = true)]
    public static extern HANDLE WinDivertOpen(
        IntPtr filter,
        WINDIVERT_LAYER layer,
        short priority,
        ulong flags);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertRecv(
        HANDLE handle,
        void* pPacket,
        uint packetLen,
        uint* pRecvLen,
        WINDIVERT_ADDRESS* pAddr);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertRecvEx(
        HANDLE handle,
        void* pPacket,
        uint packetLen,
        uint* pRecvLen,
        ulong flags,
        WINDIVERT_ADDRESS* pAddr,
        uint* pAddrLen,
        NativeOverlapped* lpOverlapped);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertSend(
        HANDLE handle,
        void* pPacket,
        uint packetLen,
        uint* pSendLen,
        WINDIVERT_ADDRESS* pAddr);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertSendEx(
        HANDLE handle,
        void* pPacket,
        uint packetLen,
        uint* pSendLen,
        ulong flags,
        WINDIVERT_ADDRESS* pAddr,
        uint addrLen,
        NativeOverlapped* lpOverlapped);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertShutdown(
        HANDLE handle,
        WINDIVERT_SHUTDOWN how);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertClose(
        HANDLE handle);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertSetParam(
        HANDLE handle,
        WINDIVERT_PARAM param,
        ulong value);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertGetParam(
        HANDLE handle,
        WINDIVERT_PARAM param,
        ulong* pValue);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertHelperCalcChecksums(
        void* pPacket,
        uint packetLen,
        WINDIVERT_ADDRESS* pAddr,
        ulong flags);

    [DllImport(dllName, SetLastError = true)]
    public static extern BOOL WinDivertHelperCompileFilter(
        IntPtr filter,
        WINDIVERT_LAYER layer,
        byte* @object,
        uint objLen,
        IntPtr* errorStr,
        uint* errorPos);
}
