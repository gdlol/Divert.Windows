using System.Net;
using System.Runtime.InteropServices;

namespace Divert.Windows;

[StructLayout(LayoutKind.Sequential)]
unsafe public struct DivertAddress
{
    public struct NetworkData
    {
        public uint InterfaceIndex;
        public uint SubInterfaceIndex;
    }

    public struct FlowData
    {
        public ulong EndpointId;
        public ulong ParentEndpointId;
        public uint ProcessId;
        public IPAddress LocalAddress;
        public IPAddress RemoteAddress;
        public ushort LocalPort;
        public ushort RemotePort;
        public byte Protocol;
    }

    public struct SocketData
    {
        public ulong EndpointId;
        public ulong ParentEndpointId;
        public uint ProcessId;
        public IPAddress LocalAddress;
        public IPAddress RemoteAddress;
        public ushort LocalPort;
        public ushort RemotePort;
        public byte Protocol;
    }

    public struct ReflectData
    {
        public long Timestamp;
        public uint ProcessId;
        public DivertLayer Layer;
        public DivertFlags Flags;
        public short Priority;
    }

    private WINDIVERT_ADDRESS address;

    internal WINDIVERT_ADDRESS Struct => address;

    internal DivertAddress(WINDIVERT_ADDRESS address)
    {
        this.address = address;
    }

    public DivertAddress(int interfaceIndex, int subInterfaceIndex)
    {
        address = new WINDIVERT_ADDRESS
        {
            Network = new WINDIVERT_DATA_NETWORK
            {
                IfIdx = checked((uint)interfaceIndex),
                SubIfIdx = checked((uint)subInterfaceIndex)
            }
        };
    }

    public long Timestamp
    {
        get { return address.Timestamp; }
        set { address.Timestamp = value; }
    }

    public DivertLayer Layer
    {
        get { return (DivertLayer)address.Layer; }
        set { address.Layer = (byte)value; }
    }

    public DivertEvent Event
    {
        get { return (DivertEvent)address.Event; }
        set { address.Layer = (byte)value; }
    }

    private bool GetBit(WINDIVERT_ADDRESS_BITS bit)
    {
        return (address.Bits & bit) != 0;
    }

    private void SetBit(WINDIVERT_ADDRESS_BITS bit, bool value)
    {
        if (value)
        {
            address.Bits |= bit;
        }
        else
        {
            address.Bits &= ~bit;
        }
    }

    public bool IsSniffed
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.Sniffed); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.Sniffed, value); }
    }

    public bool IsOutbound
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.Outbound); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.Outbound, value); }
    }

    public bool IsLoopback
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.Loopback); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.Loopback, value); }
    }

    public bool IsImpostor
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.Impostor); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.Impostor, value); }
    }

    public bool IsIPv6
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.IPv6); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.IPv6, value); }
    }

    public bool IsIPChecksumValid
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.IPChecksum); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.IPChecksum, value); }
    }

    public bool IsTCPChecksumValid
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.TCPChecksum); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.TCPChecksum, value); }
    }

    public bool IsUDPChecksumValid
    {
        get { return GetBit(WINDIVERT_ADDRESS_BITS.UDPChecksum); }
        set { SetBit(WINDIVERT_ADDRESS_BITS.UDPChecksum, value); }
    }

    public NetworkData GetNetworkData()
    {
        return Layer switch
        {
            DivertLayer.Network or DivertLayer.Forward => new NetworkData
            {
                InterfaceIndex = address.Network.IfIdx,
                SubInterfaceIndex = address.Network.SubIfIdx
            },
            _ => throw new InvalidOperationException($"{nameof(Layer)}: {Layer}"),
        };
    }

    private IPAddress GetIPAddress(Span<byte> bytes)
    {
        bytes.Reverse();
        var address = new IPAddress(bytes);
        if (!IsIPv6)
        {
            address = address.MapToIPv4();
        }
        return address;
    }

    public FlowData GetFlowData()
    {
        switch (Layer)
        {
            case DivertLayer.Flow:
                var flow = address.Flow;
                return new FlowData
                {
                    EndpointId = address.Flow.EndpointId,
                    ParentEndpointId = address.Flow.ParentEndpointId,
                    ProcessId = address.Flow.ProcessId,
                    LocalAddress = GetIPAddress(new Span<byte>(flow.LocalAddr, 16)),
                    RemoteAddress = GetIPAddress(new Span<byte>(flow.RemoteAddr, 16)),
                    LocalPort = address.Flow.LocalPort,
                    RemotePort = address.Flow.RemotePort,
                    Protocol = address.Flow.Protocol
                };
            default:
                throw new InvalidOperationException(Layer.ToString());
        }
    }

    public SocketData GetSocketData()
    {
        switch (Layer)
        {
            case DivertLayer.Socket:
                var socket = address.Socket;
                return new SocketData
                {
                    EndpointId = address.Socket.EndpointId,
                    ParentEndpointId = address.Socket.ParentEndpointId,
                    ProcessId = address.Socket.ProcessId,
                    LocalAddress = GetIPAddress(new Span<byte>(socket.LocalAddr, 16)),
                    RemoteAddress = GetIPAddress(new Span<byte>(socket.RemoteAddr, 16)),
                    LocalPort = address.Socket.LocalPort,
                    RemotePort = address.Socket.RemotePort,
                    Protocol = address.Socket.Protocol
                };
            default:
                throw new InvalidOperationException(Layer.ToString());
        }
    }

    public ReflectData GetReflectData()
    {
        return Layer switch
        {
            DivertLayer.Reflect => new ReflectData
            {
                Timestamp = address.Reflect.Timestamp,
                ProcessId = address.Reflect.ProcessId,
                Layer = (DivertLayer)address.Reflect.Layer,
                Flags = (DivertFlags)address.Reflect.Flags,
                Priority = address.Reflect.Priority
            },
            _ => throw new InvalidOperationException(Layer.ToString()),
        };
    }
}
