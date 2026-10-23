using System.Diagnostics;
using LocalActivityTracker.Core.Ipc;
using Xunit;

namespace LocalActivityTracker.Tests;

public class NamedPipeIpcTests
{
    [Fact]
    public async Task NamedPipeIpc_HandshakeAndBidirectionalMessaging_Succeeds()
    {
        var pipeName = "LAT_TestPipe_" + Guid.NewGuid().ToString("N");
        var profileId = Guid.NewGuid().ToString("N");

        using var server = new NamedPipeIpcServer(pipeName: pipeName);
        await server.StartAsync();

        try
        {
            using var client = new NamedPipeIpcClient(profileId, pipeName: pipeName);

            var serverReceivedMessageTcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientReceivedMessageTcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientConnectedTcs = new TaskCompletionSource<ClientConnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientDisconnectedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            server.ClientConnected += (s, e) => clientConnectedTcs.TrySetResult(e);
            server.ClientDisconnected += (s, e) => clientDisconnectedTcs.TrySetResult(e.ProfileId);
            server.MessageReceived += (s, e) => serverReceivedMessageTcs.TrySetResult(e.Message);
            client.MessageReceived += (s, msg) => clientReceivedMessageTcs.TrySetResult(msg);

            // Connect
            var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));
            Assert.True(connected);

            // Handshake
            var ack = await client.PerformHandshakeAsync(new IpcHandshakePayload
            {
                ProfileName = "TestProfile",
                ExtensionVersion = "1.0.0",
                NativeHostPid = Environment.ProcessId
            });

            Assert.NotNull(ack);
            Assert.Equal("CONNECTED", ack.Status);

            // Verify Server registered client
            var connectedEvent = await clientConnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(profileId, connectedEvent.ProfileId);
            Assert.Contains(profileId, server.ConnectedProfileIds);

            // Client -> Server messaging
            var testEvent = IpcMessage.Create(
                IpcMessage.EventTabNavigated,
                profileId,
                new { url = "https://github.com", domain = "github.com" });

            await client.SendMessageAsync(testEvent);

            var receivedByServer = await serverReceivedMessageTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(IpcMessage.EventTabNavigated, receivedByServer.MessageType);
            Assert.Equal(profileId, receivedByServer.ProfileId);

            // Server -> Client targeted messaging
            var serverCommand = IpcMessage.Create(
                IpcMessage.RequestScreenshot,
                profileId,
                new IpcScreenshotRequestPayload { TimeoutMs = 5000, Format = "png" });

            var sent = await server.SendMessageAsync(profileId, serverCommand);
            Assert.True(sent);

            var receivedByClient = await clientReceivedMessageTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(IpcMessage.RequestScreenshot, receivedByClient.MessageType);

            // Disconnect
            await client.DisconnectAsync();

            var disconnectedProfile = await clientDisconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(profileId, disconnectedProfile);
            Assert.DoesNotContain(profileId, server.ConnectedProfileIds);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
