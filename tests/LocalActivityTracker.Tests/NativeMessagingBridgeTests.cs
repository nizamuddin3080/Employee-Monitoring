using System.IO.Pipes;
using System.Text.Json;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.NativeHost;
using Xunit;

namespace LocalActivityTracker.Tests;

public class NativeMessagingBridgeTests
{
    [Fact]
    public async Task Bridge_ClientHelloAndEventRelay_SucceedsEndToEnd()
    {
        var pipeName = "LAT_BridgeTest_" + Guid.NewGuid().ToString("N");
        var profileId = Guid.NewGuid().ToString("N");

        using var server = new NamedPipeIpcServer(pipeName: pipeName);
        await server.StartAsync();

        try
        {
            // Anonymous pipes simulating Chrome Extension stdin and stdout
            using var extToHostServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            using var extToHostClient = new AnonymousPipeClientStream(PipeDirection.In, extToHostServer.GetClientHandleAsString());

            using var hostToExtServer = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            using var hostToExtClient = new AnonymousPipeClientStream(PipeDirection.Out, hostToExtServer.GetClientHandleAsString());

            using var bridge = new NativeMessagingBridge(
                extToHostClient,
                hostToExtClient,
                customPipeName: pipeName);

            var serverMessageTcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.MessageReceived += (s, e) => serverMessageTcs.TrySetResult(e.Message);

            bridge.Start();

            // 1. Simulate Extension sending CLIENT_HELLO
            var clientHello = new
            {
                type = "CLIENT_HELLO",
                payload = new
                {
                    profile_id = profileId,
                    profile_name = "Work Profile",
                    extension_version = "1.2.3"
                }
            };

            await NativeMessageStream.WriteMessageAsync(
                extToHostServer,
                JsonSerializer.Serialize(clientHello));

            // 2. Read CLIENT_HELLO_ACK on extension side
            var rawAck = await NativeMessageStream.ReadMessageAsync(hostToExtServer);
            Assert.NotNull(rawAck);

            using var ackDoc = JsonDocument.Parse(rawAck);
            Assert.Equal("CLIENT_HELLO_ACK", ackDoc.RootElement.GetProperty("type").GetString());
            Assert.Equal(profileId, ackDoc.RootElement.GetProperty("payload").GetProperty("profile_id").GetString());

            // 3. Simulate Extension sending TAB_NAVIGATED
            var tabEvent = new
            {
                type = "TAB_NAVIGATED",
                payload = new
                {
                    profile_id = profileId,
                    window_id = 10,
                    tab_id = 20,
                    raw_url = "https://example.com/test",
                    sanitized_url = "https://example.com/test",
                    domain = "example.com",
                    page_title = "Example Domain"
                }
            };

            await NativeMessageStream.WriteMessageAsync(
                extToHostServer,
                JsonSerializer.Serialize(tabEvent));

            // 4. Coordinator receives bridged IPC message
            var coordinatorMessage = await serverMessageTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(IpcMessage.EventTabNavigated, coordinatorMessage.MessageType);
            Assert.Equal(profileId, coordinatorMessage.ProfileId);

            await bridge.StopAsync();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Bridge_OfflineBuffering_BuffersWhenCoordinatorUnavailable()
    {
        var offlinePipeName = "LAT_OfflinePipe_" + Guid.NewGuid().ToString("N");
        var profileId = Guid.NewGuid().ToString("N");

        using var extToHostServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        using var extToHostClient = new AnonymousPipeClientStream(PipeDirection.In, extToHostServer.GetClientHandleAsString());

        using var hostToExtServer = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        using var hostToExtClient = new AnonymousPipeClientStream(PipeDirection.Out, hostToExtServer.GetClientHandleAsString());

        using var bridge = new NativeMessagingBridge(
            extToHostClient,
            hostToExtClient,
            customPipeName: offlinePipeName);

        bridge.Start();

        // 1. Send CLIENT_HELLO (Coordinator is offline)
        var clientHello = new
        {
            type = "CLIENT_HELLO",
            payload = new
            {
                profile_id = profileId,
                profile_name = "Offline Profile",
                extension_version = "1.0.0"
            }
        };

        await NativeMessageStream.WriteMessageAsync(
            extToHostServer,
            JsonSerializer.Serialize(clientHello));

        var rawAck = await NativeMessageStream.ReadMessageAsync(hostToExtServer);
        Assert.NotNull(rawAck);

        using var ackDoc = JsonDocument.Parse(rawAck);
        Assert.Equal("OFFLINE_BUFFERING", ackDoc.RootElement.GetProperty("payload").GetProperty("status").GetString());

        // 2. Send TAB_NAVIGATED: must be queued into offline buffer without throwing
        var tabEvent = new
        {
            type = "TAB_NAVIGATED",
            payload = new
            {
                profile_id = profileId,
                window_id = 1,
                tab_id = 2,
                raw_url = "https://offline.test"
            }
        };

        await NativeMessageStream.WriteMessageAsync(
            extToHostServer,
            JsonSerializer.Serialize(tabEvent));

        // Allow task to process
        await Task.Delay(200);

        Assert.True(bridge.BufferedEventCount > 0);

        await bridge.StopAsync();
    }
}
