namespace BatteryMonitor.Interop;

public interface IHidTransport : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Reads an Input Report from the HID device into buffer. Returns number of bytes read.
    /// </summary>
    Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken);

    /// <summary>
    /// Writes an Output Report to the HID device.
    /// </summary>
    Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a Feature Report from the HID device. buffer[0] must contain the target Report ID.
    /// </summary>
    Task<bool> GetFeatureAsync(byte[] buffer, CancellationToken cancellationToken);
}

public interface IHidTransportFactory
{
    IHidTransport Open(string devicePath);
}
