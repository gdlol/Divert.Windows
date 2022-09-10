namespace Divert.Windows;

[Flags]
public enum DivertHelperFlags
{
    None = 0,
    NoIPChecksum = 1,
    NoICMPChecksum = 2,
    NoICMPv6Checksum = 4,
    NoTCPChecksum = 8,
    NoUDPChecksum = 16
}
