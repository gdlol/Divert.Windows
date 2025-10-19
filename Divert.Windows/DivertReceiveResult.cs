namespace Divert.Windows;

public readonly struct DivertReceiveResult(int length, Memory<DivertAddress> addresses)
{
    public int Length => length;
    public Memory<DivertAddress> Addresses => addresses;
}
