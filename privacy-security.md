# Privacy & Security Specification — Local Activity Tracker

> **Data Collection Boundaries, Redaction Policies, Threat Modeling, and Local-Only Enforcement**  
> *Target: Windows 10 & 11 \| 100% Offline Architecture \| Defense-in-Depth*

---

## 1. Absolute Privacy Boundaries (What Is NOT Collected)

Local Activity Tracker is built strictly on privacy-by-design principles. The following capabilities are **explicitly excluded** from the codebase:

| Excluded Capability | Architectural Guarantee |
| :--- | :--- |
| **No Keylogging** | The application contains **zero** low-level keyboard hooks (`SetWindowsHookEx(WH_KEYBOARD_LL)`), raw input listeners, or keystroke counters. |
| **No Form / Input Harvesting** | Extension content scripts do **not** inspect DOM input fields, passwords, credit card numbers, or textarea contents. |
| **No Cookie / Token Theft** | The extension does **not** request `cookies` permission and never inspects HTTP request headers, `Authorization` headers, or local session cookies. |
| **No Clipboard Inspection** | The application contains zero clipboard monitoring APIs (`GetClipboardData`, `AddClipboardFormatListener`). |
| **No Window Titles for Non-Chrome Apps**| Tracking of applications other than Chrome collects **strictly** the executable name (e.g. `EXCEL.EXE`) and application display name (e.g. `Microsoft Excel`). **Window titles, captions, document names, spreadsheets, emails, and chat messages are never collected or stored.** |
| **No Desktop Screenshots** | Screenshots are strictly captured via `chrome.tabs.captureVisibleTab`. The Windows Agent has zero GDI/DirectX desktop capture code (`BitBlt`, `Desktop Duplication`). Other applications and the desktop background are never screenshotted. |
| **No Private / Incognito Tracking** | Chrome's incognito tabs are explicitly excluded by default. The extension respects Chrome's native incognito boundary. |
| **No Cloud / Remote Telemetry** | Zero external analytics, tracking pixels, crash reporters (Sentry, Datadog), or remote HTTP requests. The system operates 100% offline. |

---

## 2. Permitted Data Collection Scope

The application records **only** the following data points locally:
1. **Chrome Profiles:** Profile identifier UUID, user-assigned profile display name, enrollment status, and last seen timestamp.
2. **Chrome URL Activity:** Visited URLs (with sensitive parameters stripped), host domains, page titles, visit start/end timestamps, and active duration in seconds.
3. **Chrome Foreground Duration:** Total time Google Chrome was the foreground active application on Windows.
4. **Authorized Tab Screenshots:** PNG captures of authorized, visible Chrome tabs captured at a user-configured interval (default: 10 minutes) and stored exclusively in `%LOCALAPPDATA%\LocalActivityTracker\screenshots\`.
5. **Non-Chrome Foreground Application Duration:** Process name (e.g., `Code.exe`), application display name (e.g., `Visual Studio Code`), session start/end timestamps, and elapsed duration. **Zero window titles or internal contents.**

---

## 3. URL Parameter Sanitization & Redaction Engine

### 3.1 Blacklisted Parameter Tokens
Web URLs frequently contain sensitive authentication tokens, OAuth authorization codes, session IDs, and reset tokens within query parameters.

The extension and Windows Coordinator execute a deterministic sanitization pipeline before persisting any URL to SQLite. Any query parameter whose key contains or matches the following patterns is stripped or replaced with `[REDACTED]`:

```
token, auth, access_token, id_token, refresh_token, bearer,
password, pwd, pass, secret, api_key, apikey, key, signature,
sig, session, session_id, sid, code, oauth_token, state,
private, credential, csrf, xsrf
```

### 3.2 URI Fragments
All URI fragments (`#access_token=...`, `#id=...`) are completely stripped from recorded URLs.

### 3.3 Redaction Example
```
Raw Visited URL:
https://app.example.com/oauth/callback?code=a8f92bd18c&state=xyz123&client_id=my-app#token=secret999

Persisted & Exported Sanitized URL:
https://app.example.com/oauth/callback?code=[REDACTED]&state=[REDACTED]&client_id=my-app
```

---

## 4. Localhost Dashboard & REST API Security Model

Binding to `127.0.0.1` prevents remote network access but leaves a service vulnerable to browser-based attacks from malicious external websites executing scripts in the user's browser. The local API implements defense-in-depth controls:

### 4.1 Strict Interface Binding
- The embedded Kestrel web server binds **strictly** to `http://127.0.0.1:48120`.
- It never binds to `0.0.0.0` or external network adapters.

### 4.2 Host Header Validation (DNS Rebinding Defense)
- Attackers can create malicious domains (e.g., `attacker.com`) that resolve to `127.0.0.1` to bypass browser origin checks.
- The Coordinator validates that incoming `Host` headers strictly equal `127.0.0.1:48120` or `localhost:48120`. Any request with an unapproved Host header is rejected immediately with `400 Bad Request`.

### 4.3 Origin Header Validation & CORS Policy
- Requests from external websites are blocked by validating the `Origin` header.
- Production approved origin: `http://127.0.0.1:48120` or `http://localhost:48120`.
- Development origin: `http://localhost:5173` is permitted **only** when `ASPNETCORE_ENVIRONMENT=Development`.
- Wildcard CORS (`Access-Control-Allow-Origin: *`) is strictly forbidden.

### 4.4 Cross-Site Request Forgery (CSRF) Mitigation
- Standard HTML forms and simple `POST` requests from cross-origin scripts are blocked by requiring a custom HTTP request header on all state-changing endpoints (`POST`, `PUT`, `DELETE`):
  `X-LAT-Requested-With: LocalActivityTracker`
- Browser cross-origin scripts cannot send custom headers without triggering a preflight `OPTIONS` request, which is rejected by the Origin validator.

### 4.5 Local API Capability Token Authorization
- Upon startup, the Coordinator creates a cryptographically secure 256-bit token stored in:
  `%LOCALAPPDATA%\LocalActivityTracker\config\auth.token`
- File permissions on `auth.token` are restricted via NTFS ACLs to the current user only.
- The dashboard UI retrieves this token during bootstrap and includes it in all subsequent requests:
  `Authorization: Bearer <LAT_CAPABILITY_TOKEN>`
- Protects all sensitive operations: screenshot retrieval, data deletion, CSV exports, settings changes, and tracking pause/resume.
- Tokens are never exposed in URL query strings and never written to diagnostic logs.

---

## 5. Windows Interactive Session Isolation & Named Pipe Security

### 5.1 Named Pipe Access Control (DACL)
- Named Pipe: `\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}`
- Created with `PIPE_REJECT_REMOTE_CLIENTS` flag, preventing any network-based connection.
- The Pipe Security Descriptor explicitly grants `GENERIC_READ | GENERIC_WRITE` only to:
  - Current Logged-in User SID
  - `NT AUTHORITY\SYSTEM`
- Any connection attempt from another local user account is rejected at the Windows OS kernel boundary.

### 5.2 Client Process Verification
Upon client connection, the Coordinator invokes `GetNamedPipeClientProcessId` and inspects the client process owner token. If the process does not match the Coordinator's user SID, the pipe is immediately closed.

---

## 6. Local Filesystem Access & NTFS Permissions

All persistent data resides within the user's local application directory:
`%LOCALAPPDATA%\LocalActivityTracker\`

### NTFS Access Control Lists (ACLs)
During installation, PowerShell scripts configure directory security descriptors:
- **Current Logged-in User (`$env:USERNAME`):** `FullControl`
- **NT AUTHORITY\SYSTEM:** `FullControl`
- **BUILTIN\Administrators:** `FullControl`
- **All Other Users (Everyone, Authenticated Users):** `No Access / Deny`

---

## 7. CSV Formula Injection Defense (Spreadsheet Safety)

When CSV files are opened in Microsoft Excel, LibreOffice Calc, or Google Sheets, cell values beginning with special execution characters (`=`, `+`, `-`, `@`, `\t`, `\r`) can be interpreted as executable formulas or Dynamic Data Exchange (DDE) commands.

### Neutralization Implementation
Every string exported into the CSV report (including page titles, URLs, application names, and error messages) is processed by `CsvExportService`:
- If the first character is in `['=', '+', '-', '@', '\t', '\r']`, the string is prepended with a single quotation mark (`'`).
- In spreadsheet software, the single quote forces the cell to render strictly as inert text without executing formulas or displaying the leading quote.
- Double quotes within fields are properly escaped as `""` in accordance with RFC 4180.

---

## 8. Threat Model & Mitigations

| Threat Vector | Severity | Mitigation Strategy |
| :--- | :---: | :--- |
| **DNS Rebinding Attacks** | High | Strict Host header validation (`127.0.0.1:48120` / `localhost:48120` only). |
| **Cross-Origin Browser CSRF** | High | Strict Origin validation, `X-LAT-Requested-With` header check, and Capability Bearer Token. |
| **Cross-User Local Data Snoop** | Medium | NTFS ACLs on `%LOCALAPPDATA%\LocalActivityTracker` restricting access to current user SID. |
| **Named Pipe Spoofing** | Medium | Named pipe created with session-specific name, local-only flag, current-user DACL, and client PID verification. |
| **Malicious URL Formula Injection** | High | Automated RFC 4180 escaping and formula neutralization (`'`) applied on all exported CSV fields. |
| **Extension Tampering / Impersonation**| Low | Native host verifies extension ID in `allowed_origins`. Stdio pipe cannot be accessed remotely. |
| **Remote Network Exfiltration** | None | Agent has zero remote networking code; Kestrel binds strictly to loopback (`127.0.0.1`). |
| **URL Parameter Token Leak** | Medium | Deterministic query parameter sanitizer strips sensitive tokens prior to database persistence. |
| **Non-Chrome Document Privacy Leak** | High | **Zero window titles collected.** Only executable name and foreground duration recorded. |
