namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Service contract for a Named Pipe client connecting to the central Interactive Coordinator.
/// </summary>
public interface INamedPipeIpcClient : IDisposable
{
    event EventHandler<IpcMessage>? MessageReceived;
    event EventHandler? Connected;
    event EventHandler? Disconnected;

    bool IsConnected { get; }
    string ProfileId { get; }
    string PipeName { get; }

    Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<IpcHandshakeAckPayload?> PerformHandshakeAsync(IpcHandshakePayload handshake, CancellationToken cancellationToken = default);
    Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default);
}
