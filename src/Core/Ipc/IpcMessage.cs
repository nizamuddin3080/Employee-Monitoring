using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Standard Named Pipe IPC message envelope conforming to api-contracts.md Section 3.2.
/// </summary>
public sealed class IpcMessage
{
    public const string CurrentProtocolVersion = "1.0";

    // Standard IPC message types
    public const string Handshake = "IPC_HANDSHAKE";
    public const string HandshakeAck = "IPC_HANDSHAKE_ACK";
    public const string Heartbeat = "IPC_HEARTBEAT";
    public const string RequestScreenshot = "IPC_REQUEST_SCREENSHOT";
    public const string ResponseScreenshot = "IPC_RESPONSE_SCREENSHOT";
    public const string EventTabNavigated = "IPC_EVENT_TAB_NAVIGATED";
    public const string EventTabActivated = "IPC_EVENT_TAB_ACTIVATED";
    public const string EventTabClosed = "IPC_EVENT_TAB_CLOSED";
    public const string EventWindowFocused = "IPC_EVENT_WINDOW_FOCUSED";
    public const string EventBrowserUnfocused = "IPC_EVENT_BROWSER_UNFOCUSED";

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = CurrentProtocolVersion;

    [JsonPropertyName("message_type")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("timestamp_utc")]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; set; } = "VALID";

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Helper to deserialize the payload into a typed object.
    /// </summary>
    public T? GetPayload<T>(JsonSerializerOptions? options = null)
    {
        if (Payload == null || Payload.Value.ValueKind == JsonValueKind.Null || Payload.Value.ValueKind == JsonValueKind.Undefined)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(Payload.Value.GetRawText(), options);
    }

    /// <summary>
    /// Helper to create an IpcMessage with a typed payload.
    /// </summary>
    public static IpcMessage Create<T>(
        string messageType,
        string profileId,
        T payload,
        string? correlationId = null,
        JsonSerializerOptions? options = null)
    {
        var rawJson = JsonSerializer.Serialize(payload, options);
        var doc = JsonDocument.Parse(rawJson);

        return new IpcMessage
        {
            MessageType = messageType,
            ProfileId = profileId,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            Payload = doc.RootElement.Clone()
        };
    }
}
