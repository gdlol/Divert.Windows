namespace Divert.Windows;

[Flags]
public enum DivertFlags
{
    None = 0,
    Sniff = 0x0001,
    Drop = 0x0002,
    ReceiveOnly = 0x0004,
    ReadOnly = ReceiveOnly,
    SendOnly = 0x0008,
    WriteOnly = SendOnly,
    NoInstall = 0x0010,
    Fragments = 0x0020,
}
