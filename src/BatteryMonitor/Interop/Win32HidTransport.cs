namespace BatteryMonitor.Interop;

using System.IO;
using Microsoft.Win32.SafeHandles;

public sealed class Win32HidTransport : IHidTransport
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private bool _isDisposed;

    public Win32HidTransport(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        _handle = Win32HidPInvoke.CreateFile(
            devicePath,
            Win32HidPInvoke.GenericRead | Win32HidPInvoke.GenericWrite,
            Win32HidPInvoke.FileShareRead | Win32HidPInvoke.FileShareWrite,
            IntPtr.Zero,
            Win32HidPInvoke.OpenExisting,
            Win32HidPInvoke.FileFlagOverlapped,
            IntPtr.Zero
        );

        if (_handle.IsInvalid)
        {
            throw new IOException($"Failed to open HID device '{devicePath}'. Win32 Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }

        _stream = new FileStream(_handle, FileAccess.ReadWrite, 4096, isAsync: true);
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(buffer);

        using var reg = cancellationToken.Register(() =>
        {
            if (!_handle.IsClosed && !_handle.IsInvalid)
            {
                Win32HidPInvoke.CancelIoEx(_handle, IntPtr.Zero);
            }
        });

        return await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        using var reg = cancellationToken.Register(() =>
        {
            if (!_handle.IsClosed && !_handle.IsInvalid)
            {
                Win32HidPInvoke.CancelIoEx(_handle, IntPtr.Zero);
            }
        });

        await _stream.WriteAsync(buffer, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    public Task<bool> GetFeatureAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(buffer);

        cancellationToken.ThrowIfCancellationRequested();
        bool success = Win32HidPInvoke.HidD_GetFeature(_handle, buffer, (uint)buffer.Length);
        return Task.FromResult(success);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _stream.Dispose();
        _handle.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await _stream.DisposeAsync();
        _handle.Dispose();
    }
}

public sealed class Win32HidTransportFactory : IHidTransportFactory
{
    public IHidTransport Open(string devicePath)
    {
        return new Win32HidTransport(devicePath);
    }
}
