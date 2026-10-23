using System.Text.Json.Serialization;

namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Payload sent during initial host registration (IPC_HANDSHAKE).
/// </summary>
public sealed class IpcHandshakePayload
{
    [JsonPropertyName("profile_name")]
    public string ProfileName { get; set; } = "Default";

    [JsonPropertyName("extension_version")]
    public string ExtensionVersion { get; set; } = "1.0.0";

    [JsonPropertyName("native_host_pid")]
    public int NativeHostPid { get; set; } = Environment.ProcessId;
}

/// <summary>
/// Payload returned by Coordinator in response to handshake (IPC_HANDSHAKE_ACK).
/// </summary>
public sealed class IpcHandshakeAckPayload
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "CONNECTED";

    [JsonPropertyName("screenshot_interval_seconds")]
    public int ScreenshotIntervalSeconds { get; set; } = 600;

    [JsonPropertyName("tracking_paused")]
    public bool TrackingPaused { get; set; }
}

/// <summary>
/// Payload for targeted screenshot request (IPC_REQUEST_SCREENSHOT).
/// </summary>
public sealed class IpcScreenshotRequestPayload
{
    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; set; } = 15000;

    [JsonPropertyName("format")]
    public string Format { get; set; } = "png";
}

/// <summary>
/// Payload for screenshot response (IPC_RESPONSE_SCREENSHOT).
/// </summary>
public sealed class IpcScreenshotResponsePayload
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "SUCCESS"; // SUCCESS, RESTRICTED_URL, WINDOW_OCCLUDED_OR_MINIMIZED, CAPTURE_TIMEOUT, FAILED

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("image_data_base64")]
    public string? ImageDataBase64 { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }
}
