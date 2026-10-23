using System.Diagnostics;
using System.IO.Pipes;

namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Client connecting from a Chrome profile's Native Host instance to the Coordinator's authenticated Named Pipe.
/// </summary>
public sealed class NamedPipeIpcClient : INamedPipeIpcClient
{
    private readonly string _pipeName;
    private readonly string _profileId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _lock = new();

    private NamedPipeClientStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private TaskCompletionSource<IpcHandshakeAckPayload>? _handshakeTcs;
    private bool _isConnected;
    private bool _isDisposed;

    public event EventHandler<IpcMessage>? MessageReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public bool IsConnected => _isConnected && _stream != null && _stream.IsConnected;
    public string ProfileId => _profileId;
    public string PipeName => _pipeName;

    public NamedPipeIpcClient(string profileId, string? pipeName = null, int? sessionId = null)
    {
        _profileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        var targetSessionId = sessionId ?? Process.GetCurrentProcess().SessionId;
        _pipeName = pipeName ?? $"LocalActivityTracker-Coordinator-{targetSessionId}";
    }

    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NamedPipeIpcClient));
            }

            if (_isConnected)
            {
                return true;
            }
        }

        try
        {
            var stream = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            await stream.ConnectAsync(linkedCts.Token);

            lock (_lock)
            {
                _stream = stream;
                _isConnected = true;
                _receiveCts = new CancellationTokenSource();
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
            }

            Connected?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            await DisconnectAsync();
            return false;
        }
    }

    public async Task<IpcHandshakeAckPayload?> PerformHandshakeAsync(
        IpcHandshakePayload handshake,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Cannot perform handshake while disconnected from Coordinator.");
        }

        var handshakeMessage = IpcMessage.Create(
            IpcMessage.Handshake,
            _profileId,
            handshake);

        var tcs = new TaskCompletionSource<IpcHandshakeAckPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _handshakeTcs = tcs;

        try
        {
            await SendMessageAsync(handshakeMessage, cancellationToken);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _handshakeTcs = null;
        }
    }

    public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_stream == null || !_isConnected)
        {
            throw new InvalidOperationException("Client is not connected to Coordinator Named Pipe.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await NativeMessageStream.WriteJsonAsync(
                _stream,
                message,
                NativeMessageStream.DefaultNamedPipeMaxPayloadBytes,
                cancellationToken: cancellationToken);
        }
        catch
        {
            _ = DisconnectAsync();
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task DisconnectAsync()
    {
        lock (_lock)
        {
            if (!_isConnected && _stream == null)
            {
                return Task.CompletedTask;
            }

            _isConnected = false;
        }

        try
        {
            _receiveCts?.Cancel();
        }
        catch
        {
            // Suppress cancellation exceptions
        }

        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // Suppress disposal errors
        }

        _stream = null;
        _receiveCts?.Dispose();
        _receiveCts = null;

        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _stream != null && _isConnected)
        {
            try
            {
                var message = await NativeMessageStream.ReadJsonAsync<IpcMessage>(
                    _stream,
                    NativeMessageStream.DefaultNamedPipeMaxPayloadBytes,
                    cancellationToken: cancellationToken);

                if (message == null)
                {
                    // Clean EOF from coordinator
                    break;
                }

                if (message.MessageType == IpcMessage.HandshakeAck)
                {
                    var ack = message.GetPayload<IpcHandshakeAckPayload>() ?? new IpcHandshakeAckPayload();
                    _handshakeTcs?.TrySetResult(ack);
                    continue;
                }

                MessageReceived?.Invoke(this, message);
            }
            catch
            {
                break;
            }
        }

        await DisconnectAsync();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _writeLock.Dispose();
    }
}
