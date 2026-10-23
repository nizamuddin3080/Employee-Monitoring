using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text.Json;

namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Authenticated Windows Named Pipe IPC server running within the Interactive User-Session Coordinator.
/// Restricts access to current-user DACL and client process token SID, multiplexing multiple Chrome profiles.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NamedPipeIpcServer : INamedPipeIpcServer
{
    private sealed class ConnectedClientSession : IDisposable
    {
        public string ProfileId { get; }
        public uint ClientPid { get; }
        public NamedPipeServerStream Stream { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        public ConnectedClientSession(string profileId, uint clientPid, NamedPipeServerStream stream)
        {
            ProfileId = profileId;
            ClientPid = clientPid;
            Stream = stream;
        }

        public void Dispose()
        {
            try
            {
                Stream.Dispose();
            }
            catch
            {
                // Suppress disposal errors
            }

            WriteLock.Dispose();
        }
    }

    private readonly string _pipeName;
    private readonly ConcurrentDictionary<string, ConnectedClientSession> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _isRunning;
    private bool _isDisposed;

    public event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public bool IsRunning => _isRunning;
    public string PipeName => _pipeName;
    public IReadOnlyList<string> ConnectedProfileIds => _clients.Keys.ToList();

    public NamedPipeIpcServer(string? pipeName = null, int? sessionId = null)
    {
        var targetSessionId = sessionId ?? Process.GetCurrentProcess().SessionId;
        _pipeName = pipeName ?? $"LocalActivityTracker-Coordinator-{targetSessionId}";
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NamedPipeIpcServer));
            }

            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();
            _isRunning = true;
            _listenerTask = Task.Run(() => AcceptClientsLoopAsync(_cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? ctsToCancel;
        Task? listenerToWait;

        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            ctsToCancel = _cts;
            listenerToWait = _listenerTask;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch
            {
                // Suppress cancellation exceptions
            }
        }

        // Close all active client connections
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();

        if (listenerToWait != null)
        {
            try
            {
                await listenerToWait;
            }
            catch
            {
                // Suppress task cancellation exceptions
            }
        }

        ctsToCancel?.Dispose();
    }

    public async Task<bool> SendMessageAsync(string profileId, IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(profileId, out var client))
        {
            return false;
        }

        await client.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await NativeMessageStream.WriteJsonAsync(
                client.Stream,
                message,
                NativeMessageStream.DefaultNamedPipeMaxPayloadBytes,
                cancellationToken: cancellationToken);

            return true;
        }
        catch
        {
            DisconnectClient(profileId);
            return false;
        }
        finally
        {
            client.WriteLock.Release();
        }
    }

    public async Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        var sendTasks = _clients.Keys.Select(profileId => SendMessageAsync(profileId, message, cancellationToken));
        await Task.WhenAll(sendTasks);
    }

    private async Task AcceptClientsLoopAsync(CancellationToken cancellationToken)
    {
        var pipeSecurity = NamedPipeSecurityHelper.CreateCurrentUserPipeSecurity();

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;

            try
            {
                serverStream = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 65536,
                    outBufferSize: 65536,
                    pipeSecurity: pipeSecurity);

                await serverStream.WaitForConnectionAsync(cancellationToken);

                // Verify client process token ownership
                if (!NamedPipeSecurityHelper.VerifyClientProcessOwner(serverStream.SafePipeHandle, out var clientPid))
                {
                    serverStream.Dispose();
                    continue;
                }

                // Process client connection asynchronously
                _ = HandleClientSessionAsync(serverStream, clientPid, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                serverStream?.Dispose();
                break;
            }
            catch
            {
                serverStream?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Brief backoff before next accept attempt
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientSessionAsync(NamedPipeServerStream stream, uint clientPid, CancellationToken cancellationToken)
    {
        string? profileId = null;

        try
        {
            // 1. Initial Handshake phase
            var initialMessage = await NativeMessageStream.ReadJsonAsync<IpcMessage>(
                stream,
                NativeMessageStream.DefaultNamedPipeMaxPayloadBytes,
                cancellationToken: cancellationToken);

            if (initialMessage == null || initialMessage.MessageType != IpcMessage.Handshake || string.IsNullOrWhiteSpace(initialMessage.ProfileId))
            {
                stream.Dispose();
                return;
            }

            profileId = initialMessage.ProfileId;
            var handshakePayload = initialMessage.GetPayload<IpcHandshakePayload>() ?? new IpcHandshakePayload();

            var session = new ConnectedClientSession(profileId, clientPid, stream);
            _clients.AddOrUpdate(profileId, session, (_, existing) =>
            {
                existing.Dispose();
                return session;
            });

            // 2. Send Handshake Acknowledgment
            var ack = IpcMessage.Create(
                IpcMessage.HandshakeAck,
                profileId,
                new IpcHandshakeAckPayload
                {
                    Status = "CONNECTED",
                    ScreenshotIntervalSeconds = 600,
                    TrackingPaused = false
                },
                correlationId: initialMessage.CorrelationId);

            await NativeMessageStream.WriteJsonAsync(
                stream,
                ack,
                NativeMessageStream.DefaultNamedPipeMaxPayloadBytes,
                cancellationToken: cancellationToken);

            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(profileId, clientPid, handshakePayload));

            // 3. Receive message pump
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await NativeMessageStream.ReadJsonAsync<IpcMessage>(
                    stream,
                    NativeMessageStream.DefaultNamedPipeMaxPayloadBytes,
                    cancellationToken: cancellationToken);

                if (message == null)
                {
                    // Clean EOF from client
                    break;
                }

                if (message.MessageType == IpcMessage.Heartbeat)
                {
                    // Acknowledge heartbeat if needed or keep alive
                    continue;
                }

                MessageReceived?.Invoke(this, new IpcMessageReceivedEventArgs(profileId, message));
            }
        }
        catch
        {
            // Suppress disconnection/stream failure exceptions
        }
        finally
        {
            if (profileId != null)
            {
                DisconnectClient(profileId);
            }
            else
            {
                stream.Dispose();
            }
        }
    }

    private void DisconnectClient(string profileId)
    {
        if (_clients.TryRemove(profileId, out var session))
        {
            session.Dispose();
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(profileId));
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopAsync().GetAwaiter().GetResult();
    }
}
