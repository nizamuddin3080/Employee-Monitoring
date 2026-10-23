namespace LocalActivityTracker.Core.Ipc;

public sealed class IpcMessageReceivedEventArgs : EventArgs
{
    public string ProfileId { get; }
    public IpcMessage Message { get; }

    public IpcMessageReceivedEventArgs(string profileId, IpcMessage message)
    {
        ProfileId = profileId;
        Message = message;
    }
}

public sealed class ClientConnectedEventArgs : EventArgs
{
    public string ProfileId { get; }
    public uint ClientPid { get; }
    public IpcHandshakePayload Handshake { get; }

    public ClientConnectedEventArgs(string profileId, uint clientPid, IpcHandshakePayload handshake)
    {
        ProfileId = profileId;
        ClientPid = clientPid;
        Handshake = handshake;
    }
}

public sealed class ClientDisconnectedEventArgs : EventArgs
{
    public string ProfileId { get; }

    public ClientDisconnectedEventArgs(string profileId)
    {
        ProfileId = profileId;
    }
}

/// <summary>
/// Service contract for the Windows Named Pipe server hosting communication with multi-profile Native Hosts.
/// </summary>
public interface INamedPipeIpcServer : IDisposable
{
    event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    bool IsRunning { get; }
    string PipeName { get; }
    IReadOnlyList<string> ConnectedProfileIds { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<bool> SendMessageAsync(string profileId, IpcMessage message, CancellationToken cancellationToken = default);
    Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default);
}
