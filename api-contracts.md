# API Contracts & Internal Communication Specifications

> **Chrome Native Messaging Stdio Framing, Windows Named Pipe IPC, and Hardened Localhost REST Contracts**  
> *Target: Local Workstation Communication Only \| Strict Zero-Cloud Policy \| Defense-in-Depth*

---

## 1. Architectural Transport Notice

> [!IMPORTANT]
> **NO REMOTE OR CLOUD API EXISTS.**  
> The application uses three separate local communication layers, each with an independent protocol specification:
> 1. **Native Messaging Stdio Layer:** Standard Input/Output (`stdio`) framing between Google Chrome extension instances and their respective Native Host bridge processes.
> 2. **Windows Named Pipe IPC Layer:** Binary framed Windows Named Pipe (`\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}`) between Native Host instances and the Interactive User-Session Coordinator.
> 3. **Localhost HTTP REST Layer:** Hardened loopback REST API (`127.0.0.1:48120`) between the React dashboard and the Interactive Coordinator, protected by Host/Origin checks, CSRF headers, and capability tokens.

---

## 2. Layer 1: Chrome Native Messaging Protocol (Extension ↔ Native Host)

Chrome's Native Messaging protocol uses standard input and standard output with binary length-prefix framing:
- **Length Prefix:** 32-bit unsigned little-endian integer (4 bytes) representing payload byte length.
- **Payload:** UTF-8 encoded JSON string.
- **Chrome Size Limit:** Strictly capped at 1,048,576 bytes (1 MB).

```
+---------------------------+----------------------------------------------+
| 4 Bytes (uint32 LE)       | Variable Length (up to 1,048,576 bytes)      |
| Message Length = N        | UTF-8 JSON String Payload                    |
+---------------------------+----------------------------------------------+
```

### 2.1 Extension → Native Host Messages

#### A. Handshake (`CLIENT_HELLO`)
```json
{
  "type": "CLIENT_HELLO",
  "payload": {
    "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "profile_name": "Default",
    "extension_version": "1.0.0",
    "chrome_version": "130.0.6723.70",
    "client_timestamp_utc": "2026-10-22T09:30:00.000Z"
  }
}
```

#### B. Navigation & Tab Events (`TAB_NAVIGATED`, `TAB_ACTIVATED`, `TAB_CLOSED`)
```json
{
  "type": "TAB_NAVIGATED",
  "payload": {
    "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "window_id": 14205,
    "tab_id": 8921,
    "raw_url": "https://github.com/udaysharmadev/Gravitas?tab=readme",
    "sanitized_url": "https://github.com/udaysharmadev/Gravitas",
    "domain": "github.com",
    "page_title": "GitHub - udaysharmadev/Gravitas",
    "is_incognito": false,
    "timestamp_utc": "2026-10-22T09:31:00.000Z"
  }
}
```

#### C. Screenshot Responses (`SCREENSHOT_CAPTURED`, `SCREENSHOT_FAILED`)
```json
{
  "type": "SCREENSHOT_CAPTURED",
  "payload": {
    "capture_id": "cap-99b3-4f12",
    "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "window_id": 14205,
    "tab_id": 8921,
    "url": "https://github.com/udaysharmadev/Gravitas",
    "timestamp_utc": "2026-10-22T09:40:00.000Z",
    "image_format": "png",
    "image_data_base64": "iVBORw0KGgoAAAANSUhEUgAA..."
  }
}
```
*(Note: `image_data_base64` is processed exclusively in memory and saved to disk. It is strictly excluded from all diagnostic text logs).*

---

## 3. Layer 2: Windows Named Pipe IPC Protocol (Native Host ↔ Coordinator)

The Native Host instances bridge messages to the Interactive Coordinator using Windows Named Pipes.

### 3.1 Pipe Transport Specifications
- **Pipe Address:** `\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}` (where `{SessionId}` is the Windows Terminal Services session ID of the logged-in user).
- **Security Descriptor:** DACL grants access **exclusively** to the current user SID and `NT AUTHORITY\SYSTEM`. Rejects remote/network clients.
- **Framing:** 4-byte unsigned little-endian integer prefix + UTF-8 JSON payload.
- **Max Message Size:** 4,194,304 bytes (4 MB) to accommodate base64 screenshot responses.
- **Client Verification:** The Coordinator invokes `GetNamedPipeClientProcessId` and validates that the client process owner token matches the Coordinator's user SID.

### 3.2 Named Pipe Message Envelope Structure
Every message on the Named Pipe encapsulates the following standard header:

```json
{
  "protocol_version": "1.0",
  "message_type": "IPC_EVENT_TAB_NAVIGATED",
  "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "correlation_id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "timestamp_utc": "2026-10-22T09:31:00.120Z",
  "payload": { ... },
  "validation_status": "VALID",
  "error_code": null
}
```

### 3.3 Core IPC Message Types

#### A. Host Registration Handshake (`IPC_HANDSHAKE` / `IPC_HANDSHAKE_ACK`)
- **Host → Coordinator:**
```json
{
  "protocol_version": "1.0",
  "message_type": "IPC_HANDSHAKE",
  "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "correlation_id": "init-01",
  "timestamp_utc": "2026-10-22T09:30:00.000Z",
  "payload": {
    "profile_name": "Default",
    "extension_version": "1.0.0",
    "native_host_pid": 14520
  }
}
```
- **Coordinator → Host:**
```json
{
  "protocol_version": "1.0",
  "message_type": "IPC_HANDSHAKE_ACK",
  "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "correlation_id": "init-01",
  "timestamp_utc": "2026-10-22T09:30:00.050Z",
  "payload": {
    "status": "CONNECTED",
    "screenshot_interval_seconds": 600,
    "tracking_paused": false
  }
}
```

#### B. Heartbeat (`IPC_HEARTBEAT`)
Sent every 30 seconds by each connected Native Host to maintain alive status.

#### C. Targeted Screenshot Request (`IPC_REQUEST_SCREENSHOT`)
Dispatched by Coordinator across the specific Named Pipe connected to the foreground profile:
```json
{
  "protocol_version": "1.0",
  "message_type": "IPC_REQUEST_SCREENSHOT",
  "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "correlation_id": "req-shot-8841",
  "timestamp_utc": "2026-10-22T09:40:00.000Z",
  "payload": {
    "timeout_ms": 15000,
    "format": "png"
  }
}
```

#### D. Screenshot Response (`IPC_RESPONSE_SCREENSHOT`)
Returned by Native Host to Coordinator:
```json
{
  "protocol_version": "1.0",
  "message_type": "IPC_RESPONSE_SCREENSHOT",
  "profile_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "correlation_id": "req-shot-8841",
  "timestamp_utc": "2026-10-22T09:40:02.100Z",
  "payload": {
    "status": "SUCCESS",
    "url": "https://github.com/udaysharmadev/Gravitas",
    "image_data_base64": "iVBORw0KGgo...",
    "failure_code": null,
    "failure_reason": null
  }
}
```
If capture fails, `status` contains `RESTRICTED_URL`, `WINDOW_OCCLUDED_OR_MINIMIZED`, or `CAPTURE_TIMEOUT`, and `image_data_base64` is `null`.

---

## 4. Layer 3: Hardened Localhost REST API (Coordinator ↔ Dashboard)

- **Base URL:** `http://127.0.0.1:48120`
- **Network Interface:** Loopback (`127.0.0.1`) only. Binds to no external interfaces.

### 4.1 Security Enforcement Rules
1. **Host Header Check:** `Host` header must strictly match `127.0.0.1:48120` or `localhost:48120`. Any other host is rejected with `400 Bad Request`.
2. **Origin Header Check:** `Origin` must match `http://127.0.0.1:48120` or `http://localhost:48120` (in development mode, `http://localhost:5173` is permitted). Wildcard CORS is prohibited.
3. **CSRF Mitigation:** All `POST`, `PUT`, and `DELETE` requests require:
   `X-LAT-Requested-With: LocalActivityTracker`
   Requests without this header are rejected with `403 Forbidden`.
4. **Local Capability Token:** Sensitive endpoints require:
   `Authorization: Bearer <LAT_CAPABILITY_TOKEN>`
   Where `<LAT_CAPABILITY_TOKEN>` is the 64-character secret read from `%LOCALAPPDATA%\LocalActivityTracker\config\auth.token`.

---

### 4.2 Endpoint Specifications

#### A. Overview & KPI Metrics
`GET /api/overview`
- **Headers:** `Authorization: Bearer <token>`
- **Response:**
```json
{
  "date_local": "2026-10-22",
  "chrome_foreground_seconds": 18450,
  "active_url_seconds": 14210,
  "non_chrome_app_seconds": 10200,
  "enrolled_profiles_count": 2,
  "total_url_visits": 348,
  "screenshots_successful": 18,
  "screenshots_failed": 2,
  "last_event_time_utc": "2026-10-22T09:48:12.000Z",
  "is_tracking_paused": false
}
```

#### B. Chrome Activity Timeline
`GET /api/activity`
- **Query Parameters:** `startDate`, `endDate`, `profileId`, `domain`, `page`, `pageSize`.
- **Response:**
```json
{
  "total_records": 1240,
  "page": 1,
  "page_size": 50,
  "records": [
    {
      "session_id": "7f1e9480-4567",
      "profile_id": "f47ac10b-58cc",
      "profile_name": "Default",
      "url": "https://github.com/udaysharmadev/Gravitas",
      "domain": "github.com",
      "page_title": "GitHub - udaysharmadev/Gravitas",
      "start_time_utc": "2026-10-22T09:15:00.000Z",
      "end_time_utc": "2026-10-22T09:25:30.000Z",
      "duration_seconds": 630,
      "session_status": "COMPLETED"
    }
  ]
}
```

#### C. Non-Chrome Application Usage (Duration Only — NO Window Titles)
`GET /api/applications`
- **Query Parameters:** `date` (YYYY-MM-DD).
- **Security:** `Authorization: Bearer <token>`
- **Response:**
```json
[
  {
    "application_display_name": "Visual Studio Code",
    "process_name": "Code.exe",
    "total_duration_seconds": 7200,
    "session_count": 8,
    "sessions": [
      {
        "session_id": "app-01-abc",
        "start_time_utc": "2026-10-22T07:00:00.000Z",
        "end_time_utc": "2026-10-22T08:30:00.000Z",
        "duration_seconds": 5400,
        "session_status": "COMPLETED"
      }
    ]
  }
]
```
*(Note: Contains strictly `application_display_name`, `process_name`, timestamps, and duration. `window_title` is never included).*

#### D. Screenshot Gallery & Image Delivery
`GET /api/screenshots`
- **Response:** Paginated metadata records.

`GET /api/screenshots/{id}/image`
- **Security:** Requires valid capability token.
- **Response:** `image/png` stream from `%LOCALAPPDATA%\LocalActivityTracker\screenshots\`.

`DELETE /api/screenshots/{id}`
- **Security:** Requires `Authorization: Bearer <token>` and `X-LAT-Requested-With: LocalActivityTracker`.
- **Response:** `204 No Content`.

#### E. Master CSV Export
`POST /api/export/csv`
- **Security:** Requires `Authorization: Bearer <token>` and `X-LAT-Requested-With: LocalActivityTracker`.
- **Payload:**
```json
{
  "start_date": "2026-10-01",
  "end_date": "2026-10-22",
  "profile_ids": ["ALL"],
  "include_url_activity": true,
  "include_profile_summary": true,
  "include_screenshot_metadata": true,
  "include_non_chrome_apps": true
}
```
- **Response:**
```json
{
  "status": "SUCCESS",
  "file_name": "activity_report_2026-10-01_to_2026-10-22.csv",
  "file_path": "C:\\Users\\h\\AppData\\Local\\LocalActivityTracker\\exports\\activity_report_2026-10-01_to_2026-10-22.csv",
  "record_counts": {
    "profiles_included": 2,
    "url_sessions_exported": 842,
    "screenshot_records_exported": 128,
    "application_sessions_exported": 64
  }
}
```

#### F. Settings & State Controls
- `POST /api/settings`
- `POST /api/tracking/pause`
- `POST /api/tracking/resume`
- `POST /api/data/backup`
- `POST /api/data/purge`
All state-changing endpoints enforce `X-LAT-Requested-With` and capability bearer token authentication.
