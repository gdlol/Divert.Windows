namespace Divert.Windows;

public readonly struct DivertReceiveResult(int dataLength, int addressLength)
{
    /// <summary>
    /// Gets the length of the received data.
    /// </summary>
    public int DataLength { get; } = dataLength;

    /// <summary>
    /// Gets the length of the addresses.
    /// </summary>
    public int AddressLength { get; } = addressLength;
}
