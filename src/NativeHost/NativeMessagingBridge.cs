using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using LocalActivityTracker.Core.Ipc;

namespace LocalActivityTracker.NativeHost;

/// <summary>
/// Bidirectional bridge between Chrome Extension Standard I/O (uint32 LE + JSON, 1MB limit)
/// and the Interactive Coordinator Windows Named Pipe (uint32 LE + JSON, 4MB limit).
/// Features offline message buffering (up to 100 events) with automatic reconnection.
/// </summary>
public sealed class NativeMessagingBridge : IDisposable
{
    private const int MaxOfflineBufferSize = 100;

    private readonly Stream _stdioIn;
    private readonly Stream _stdioOut;
    private readonly string? _customPipeName;
    private readonly int? _customSessionId;
    private readonly SemaphoreSlim _stdioWriteLock = new(1, 1);
    private readonly ConcurrentQueue<IpcMessage> _offlineBuffer = new();
    private readonly object _lock = new();

    private INamedPipeIpcClient? _pipeClient;
    private string _profileId = "unknown";
    private string _profileName = "Default";
    private string _extensionVersion = "1.0.0";

    private CancellationTokenSource? _bridgeCts;
    private Task? _stdioReadTask;
    private Task? _reconnectTask;
    private bool _isRunning;
    private bool _isDisposed;

    public bool IsConnectedToCoordinator => _pipeClient?.IsConnected ?? false;
    public int BufferedEventCount => _offlineBuffer.Count;

    public NativeMessagingBridge(
        Stream stdioIn,
        Stream stdioOut,
        string? customPipeName = null,
        int? customSessionId = null)
    {
        _stdioIn = stdioIn ?? throw new ArgumentNullException(nameof(stdioIn));
        _stdioOut = stdioOut ?? throw new ArgumentNullException(nameof(stdioOut));
        _customPipeName = customPipeName;
        _customSessionId = customSessionId;
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NativeMessagingBridge));
            }

            if (_isRunning)
            {
                return;
            }

            _bridgeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isRunning = true;
            _stdioReadTask = Task.Run(() => StdioReadLoopAsync(_bridgeCts.Token), CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
        }

        _bridgeCts?.Cancel();

        if (_pipeClient != null)
        {
            await _pipeClient.DisconnectAsync();
            _pipeClient.Dispose();
            _pipeClient = null;
        }

        if (_stdioReadTask != null)
        {
            try
            {
                await _stdioReadTask;
            }
            catch
            {
                // Suppress cancellation exceptions
            }
        }

        _bridgeCts?.Dispose();
    }

    private async Task StdioReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? rawMessage;

            try
            {
                rawMessage = await NativeMessageStream.ReadMessageAsync(
                    _stdioIn,
                    NativeMessageStream.DefaultStdioMaxPayloadBytes,
                    cancellationToken);

                if (rawMessage == null)
                {
                    // Clean EOF from Chrome (extension closed or browser exit)
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            await ProcessInboundStdioMessageAsync(rawMessage, cancellationToken);
        }

        await StopAsync();
    }

    private async Task ProcessInboundStdioMessageAsync(string rawMessage, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;

            var msgType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            if (string.IsNullOrEmpty(msgType))
            {
                return;
            }

            // Handle CLIENT_HELLO from extension
            if (msgType.Equals("CLIENT_HELLO", StringComparison.OrdinalIgnoreCase))
            {
                await HandleClientHelloAsync(root, cancellationToken);
                return;
            }

            // Map Extension event to Coordinator IpcMessage
            var ipcType = MapExtensionTypeToIpcType(msgType);
            var payload = root.TryGetProperty("payload", out var pProp) ? pProp.Clone() : (JsonElement?)null;

            var correlationId = Guid.NewGuid().ToString("N");
            if (payload.HasValue && payload.Value.TryGetProperty("capture_id", out var capProp))
            {
                correlationId = capProp.GetString() ?? correlationId;
            }

            var ipcMessage = new IpcMessage
            {
                MessageType = ipcType,
                ProfileId = _profileId,
                CorrelationId = correlationId,
                TimestampUtc = DateTime.UtcNow,
                Payload = payload
            };

            await SendOrBufferIpcMessageAsync(ipcMessage, cancellationToken);
        }
        catch
        {
            // Suppress message processing errors
        }
    }

    private async Task HandleClientHelloAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (root.TryGetProperty("payload", out var p))
        {
            if (p.TryGetProperty("profile_id", out var pidProp) && !string.IsNullOrWhiteSpace(pidProp.GetString()))
            {
                _profileId = pidProp.GetString()!;
            }
            if (p.TryGetProperty("profile_name", out var pnameProp) && !string.IsNullOrWhiteSpace(pnameProp.GetString()))
            {
                _profileName = pnameProp.GetString()!;
            }
            if (p.TryGetProperty("extension_version", out var verProp) && !string.IsNullOrWhiteSpace(verProp.GetString()))
            {
                _extensionVersion = verProp.GetString()!;
            }
        }

        // Initialize and connect Named Pipe client
        await EnsureConnectedAsync(cancellationToken);

        // Acknowledge CLIENT_HELLO to Chrome extension
        var ackObj = new
        {
            type = "CLIENT_HELLO_ACK",
            payload = new
            {
                status = IsConnectedToCoordinator ? "CONNECTED" : "OFFLINE_BUFFERING",
                profile_id = _profileId,
                screenshot_interval_seconds = 600,
                server_timestamp_utc = DateTime.UtcNow.ToString("o")
            }
        };

        await WriteToStdioAsync(JsonSerializer.Serialize(ackObj), cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient != null && _pipeClient.IsConnected)
        {
            return;
        }

        _pipeClient?.Dispose();
        _pipeClient = new NamedPipeIpcClient(_profileId, _customPipeName, _customSessionId);
        _pipeClient.MessageReceived += OnCoordinatorMessageReceived;
        _pipeClient.Disconnected += OnCoordinatorDisconnected;

        var connected = await _pipeClient.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken);
        if (connected)
        {
            await _pipeClient.PerformHandshakeAsync(new IpcHandshakePayload
            {
                ProfileName = _profileName,
                ExtensionVersion = _extensionVersion,
                NativeHostPid = Environment.ProcessId
            }, cancellationToken);

            // Flush offline buffered messages
            await FlushOfflineBufferAsync(cancellationToken);
        }
        else
        {
            StartReconnectLoop();
        }
    }

    private void StartReconnectLoop()
    {
        lock (_lock)
        {
            if (_reconnectTask != null && !_reconnectTask.IsCompleted)
            {
                return;
            }

            _reconnectTask = Task.Run(async () =>
            {
                var delayMs = 1000;
                while (_isRunning && _bridgeCts != null && !_bridgeCts.IsCancellationRequested)
                {
                    if (IsConnectedToCoordinator)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(delayMs, _bridgeCts.Token);

                        _pipeClient?.Dispose();
                        var client = new NamedPipeIpcClient(_profileId, _customPipeName, _customSessionId);
                        client.MessageReceived += OnCoordinatorMessageReceived;
                        client.Disconnected += OnCoordinatorDisconnected;
                        _pipeClient = client;

                        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(2), _bridgeCts.Token);
                        if (connected)
                        {
                            var ack = await client.PerformHandshakeAsync(new IpcHandshakePayload
                            {
                                ProfileName = _profileName,
                                ExtensionVersion = _extensionVersion,
                                NativeHostPid = Environment.ProcessId
                            }, _bridgeCts.Token);

                            if (ack != null && ack.Status.Equals("CONNECTED", StringComparison.OrdinalIgnoreCase))
                            {
                                var notifyObj = new
                                {
                                    type = "COORDINATOR_STATUS_CHANGED",
                                    payload = new
                                    {
                                        status = "CONNECTED",
                                        profile_id = _profileId
                                    }
                                };
                                await WriteToStdioAsync(JsonSerializer.Serialize(notifyObj), _bridgeCts.Token);

                                await FlushOfflineBufferAsync(_bridgeCts.Token);
                                break;
                            }
                            else
                            {
                                await client.DisconnectAsync();
                            }
                        }
                    }
                    catch
                    {
                        // Backoff up to 5 seconds
                        delayMs = Math.Min(delayMs * 2, 5000);
                    }
                }
            });
        }
    }

    private async Task SendOrBufferIpcMessageAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        if (IsConnectedToCoordinator && _pipeClient != null)
        {
            try
            {
                await _pipeClient.SendMessageAsync(message, cancellationToken);
                return;
            }
            catch
            {
                // Disconnected on send, fall back to buffer
                StartReconnectLoop();
            }
        }

        // Buffer offline event
        if (_offlineBuffer.Count >= MaxOfflineBufferSize)
        {
            _offlineBuffer.TryDequeue(out _); // Drop oldest event on capacity limit
        }

        _offlineBuffer.Enqueue(message);
        StartReconnectLoop();
    }

    private async Task FlushOfflineBufferAsync(CancellationToken cancellationToken)
    {
        while (_pipeClient != null && _pipeClient.IsConnected && _offlineBuffer.TryDequeue(out var msg))
        {
            try
            {
                await _pipeClient.SendMessageAsync(msg, cancellationToken);
            }
            catch
            {
                _offlineBuffer.Enqueue(msg);
                StartReconnectLoop();
                break;
            }
        }
    }

    private void OnCoordinatorMessageReceived(object? sender, IpcMessage msg)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Map Coordinator message to Extension command
                if (msg.MessageType == IpcMessage.RequestScreenshot)
                {
                    var reqObj = new
                    {
                        type = "CAPTURE_SCREENSHOT_REQUEST",
                        payload = new
                        {
                            capture_id = msg.CorrelationId,
                            timeout_ms = 15000,
                            format = "png"
                        }
                    };

                    await WriteToStdioAsync(JsonSerializer.Serialize(reqObj), CancellationToken.None);
                }
            }
            catch
            {
                // Suppress relay exceptions
            }
        });
    }

    private void OnCoordinatorDisconnected(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var notifyObj = new
                {
                    type = "COORDINATOR_STATUS_CHANGED",
                    payload = new
                    {
                        status = "DISCONNECTED",
                        profile_id = _profileId
                    }
                };
                await WriteToStdioAsync(JsonSerializer.Serialize(notifyObj), CancellationToken.None);
            }
            catch
            {
                // Suppress stdio write error during disconnect
            }
        });

        StartReconnectLoop();
    }

    private async Task WriteToStdioAsync(string json, CancellationToken cancellationToken)
    {
        await _stdioWriteLock.WaitAsync(cancellationToken);
        try
        {
            await NativeMessageStream.WriteMessageAsync(
                _stdioOut,
                json,
                NativeMessageStream.DefaultStdioMaxPayloadBytes,
                cancellationToken);
        }
        finally
        {
            _stdioWriteLock.Release();
        }
    }

    private static string MapExtensionTypeToIpcType(string extType)
    {
        return extType.ToUpperInvariant() switch
        {
            "TAB_NAVIGATED" => IpcMessage.EventTabNavigated,
            "TAB_ACTIVATED" => IpcMessage.EventTabActivated,
            "TAB_CLOSED" => IpcMessage.EventTabClosed,
            "WINDOW_FOCUSED" => IpcMessage.EventWindowFocused,
            "SCREENSHOT_CAPTURED" => IpcMessage.ResponseScreenshot,
            "SCREENSHOT_FAILED" => IpcMessage.ResponseScreenshot,
            _ => "IPC_EVENT_" + extType.ToUpperInvariant()
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopAsync().GetAwaiter().GetResult();
        _stdioWriteLock.Dispose();
    }
}
