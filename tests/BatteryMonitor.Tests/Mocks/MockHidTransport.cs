namespace BatteryMonitor.Tests.Mocks;

using BatteryMonitor.Interop;

public sealed class MockHidTransport : IHidTransport, IHidTransportFactory
{
    private readonly Queue<byte[]> _responses = new();
    private readonly List<byte[]> _writtenPackets = new();

    public IReadOnlyList<byte[]> WrittenPackets => _writtenPackets;
    public bool SimulateTimeout { get; set; }
    public Func<byte[], bool>? GetFeatureHandler { get; set; }

    public void EnqueueResponse(byte[] response)
    {
        _responses.Enqueue(response);
    }

    public void EnqueueResponseHex(string hex)
    {
        _responses.Enqueue(Convert.FromHexString(hex));
    }

    public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        if (SimulateTimeout)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_responses.Count == 0)
        {
            return Task.FromResult(0);
        }

        byte[] resp = _responses.Dequeue();
        int count = Math.Min(buffer.Length, resp.Length);
        Array.Copy(resp, buffer, count);
        return Task.FromResult(count);
    }

    public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writtenPackets.Add(buffer.ToArray());
        return Task.CompletedTask;
    }

    public Task<bool> GetFeatureAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetFeatureHandler != null)
        {
            return Task.FromResult(GetFeatureHandler(buffer));
        }
        return Task.FromResult(false);
    }

    public IHidTransport Open(string devicePath) => this;

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
