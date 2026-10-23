using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Handles binary framing for Chrome Native Messaging (Stdio) and Windows Named Pipe IPC.
/// Frames messages using a 32-bit unsigned little-endian integer length prefix followed by UTF-8 JSON payload.
/// </summary>
public static class NativeMessageStream
{
    public const int DefaultStdioMaxPayloadBytes = 1024 * 1024;        // 1 MB Chrome Native Messaging Limit
    public const int DefaultNamedPipeMaxPayloadBytes = 4 * 1024 * 1024; // 4 MB Named Pipe Limit (Screenshots)

    /// <summary>
    /// Reads a framed string message from the specified stream.
    /// Returns null if the stream reaches EOF before any bytes of the length prefix are read.
    /// </summary>
    public static async Task<string?> ReadMessageAsync(
        Stream stream,
        int maxSizeBytes = DefaultStdioMaxPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthBuffer = new byte[4];
        var bytesRead = 0;

        // Read 4-byte little-endian length prefix
        while (bytesRead < 4)
        {
            var chunk = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), cancellationToken);
            if (chunk == 0)
            {
                if (bytesRead == 0)
                {
                    return null; // Clean EOF
                }
                throw new EndOfStreamException("Stream ended prematurely while reading length prefix.");
            }
            bytesRead += chunk;
        }

        var messageLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBuffer);

        if (messageLength > (uint)maxSizeBytes)
        {
            throw new InvalidDataException(
                $"Message payload size ({messageLength} bytes) exceeded maximum allowed limit of {maxSizeBytes} bytes.");
        }

        if (messageLength == 0)
        {
            return string.Empty;
        }

        var payloadBuffer = new byte[messageLength];
        var payloadRead = 0;

        while (payloadRead < messageLength)
        {
            var chunk = await stream.ReadAsync(
                payloadBuffer.AsMemory(payloadRead, (int)messageLength - payloadRead),
                cancellationToken);

            if (chunk == 0)
            {
                throw new EndOfStreamException("Stream ended prematurely while reading message payload.");
            }

            payloadRead += chunk;
        }

        return Encoding.UTF8.GetString(payloadBuffer);
    }

    /// <summary>
    /// Writes a framed string message to the specified stream and flushes.
    /// </summary>
    public static async Task WriteMessageAsync(
        Stream stream,
        string jsonPayload,
        int maxSizeBytes = DefaultStdioMaxPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(jsonPayload);

        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        if (payloadBytes.Length > maxSizeBytes)
        {
            throw new InvalidDataException(
                $"Message payload size ({payloadBytes.Length} bytes) exceeded maximum allowed limit of {maxSizeBytes} bytes.");
        }

        var lengthBuffer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthBuffer, (uint)payloadBytes.Length);

        await stream.WriteAsync(lengthBuffer, cancellationToken);
        if (payloadBytes.Length > 0)
        {
            await stream.WriteAsync(payloadBytes, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads and deserializes a JSON object from the stream.
    /// </summary>
    public static async Task<T?> ReadJsonAsync<T>(
        Stream stream,
        int maxSizeBytes = DefaultStdioMaxPayloadBytes,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rawJson = await ReadMessageAsync(stream, maxSizeBytes, cancellationToken);
        if (rawJson == null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(rawJson, options);
    }

    /// <summary>
    /// Serializes and writes a JSON object to the stream.
    /// </summary>
    public static async Task WriteJsonAsync<T>(
        Stream stream,
        T value,
        int maxSizeBytes = DefaultStdioMaxPayloadBytes,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rawJson = JsonSerializer.Serialize(value, options);
        await WriteMessageAsync(stream, rawJson, maxSizeBytes, cancellationToken);
    }
}
