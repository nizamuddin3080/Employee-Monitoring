using System.Buffers.Binary;
using System.Text;
using LocalActivityTracker.Core.Ipc;
using Xunit;

namespace LocalActivityTracker.Tests;

public class NativeMessageStreamTests
{
    [Fact]
    public async Task ReadWriteMessage_RoundtripSucceeds()
    {
        using var stream = new MemoryStream();
        var originalMessage = "{\"type\":\"TEST_EVENT\",\"data\":12345}";

        await NativeMessageStream.WriteMessageAsync(stream, originalMessage);

        stream.Position = 0;
        var readMessage = await NativeMessageStream.ReadMessageAsync(stream);

        Assert.Equal(originalMessage, readMessage);
    }

    [Fact]
    public async Task ReadMessage_OnCleanEof_ReturnsNull()
    {
        using var emptyStream = new MemoryStream();
        var result = await NativeMessageStream.ReadMessageAsync(emptyStream);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadMessage_TruncatedLengthPrefix_ThrowsEndOfStreamException()
    {
        // Arrange: only 2 bytes instead of 4
        using var stream = new MemoryStream(new byte[] { 0x05, 0x00 });

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await NativeMessageStream.ReadMessageAsync(stream);
        });
    }

    [Fact]
    public async Task ReadMessage_TruncatedPayload_ThrowsEndOfStreamException()
    {
        // Arrange: prefix says 10 bytes, but only 3 bytes follow
        using var stream = new MemoryStream();
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, 10);
        stream.Write(prefix);
        stream.Write(new byte[] { 0x41, 0x42, 0x43 }); // 3 bytes
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await NativeMessageStream.ReadMessageAsync(stream);
        });
    }

    [Fact]
    public async Task ReadMessage_ExceedingMaxPayloadLimit_ThrowsInvalidDataException()
    {
        // Arrange: length prefix specifies 2 MB on a 1 MB max stream
        using var stream = new MemoryStream();
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, 2 * 1024 * 1024);
        stream.Write(prefix);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await NativeMessageStream.ReadMessageAsync(stream, NativeMessageStream.DefaultStdioMaxPayloadBytes);
        });
    }

    [Fact]
    public async Task WriteMessage_ExceedingMaxPayloadLimit_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream();
        var oversizedPayload = new string('A', 2 * 1024 * 1024);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await NativeMessageStream.WriteMessageAsync(stream, oversizedPayload, NativeMessageStream.DefaultStdioMaxPayloadBytes);
        });
    }
}
