# Architecture Decision Records (ADRs) — Local Activity Tracker

> **Formal Record of Architectural Choices, Alternatives Considered, and Governance Rationale**  
> *Standard: Nygard ADR Template \| Status: Reconciled & Awaiting Final User Approval*

---

## Index of Decisions

- [ADR-001: Interactive User-Session Coordinator Architecture (Session 1+)](#adr-001-interactive-user-session-coordinator-architecture-session-1) *(Supersedes Session 0 Worker Service)*
- [ADR-002: Local Dashboard Stack (React 18 + TypeScript + Tailwind)](#adr-002-local-dashboard-stack-react-18--typescript--tailwind)
- [ADR-003: Dual-Protocol Architecture: Native Messaging Stdio & Windows Named Pipes IPC](#adr-003-dual-protocol-architecture-native-messaging-stdio--windows-named-pipes-ipc)
- [ADR-004: Multi-Profile Multiplexing & Connection Routing Strategy](#adr-004-multi-profile-multiplexing--connection-routing-strategy)
- [ADR-005: Focus Reconciliation & Duration Arbitration Engine](#adr-005-focus-reconciliation--duration-arbitration-engine)
- [ADR-006: Local Screenshot Pipeline & Targeted IPC Routing](#adr-006-local-screenshot-pipeline--targeted-ipc-routing)
- [ADR-007: Local Database Engine & Concurrency (SQLite WAL Mode)](#adr-007-local-database-engine--concurrency-sqlite-wal-mode)
- [ADR-008: Master CSV Export Structure & Formula Injection Defense](#adr-008-master-csv-export-structure--formula-injection-defense)
- [ADR-009: URL Query Parameter Sanitization & Privacy Strategy](#adr-009-url-query-parameter-sanitization--privacy-strategy)
- [ADR-010: Zero-Telemetry and Local-Only Enforcement](#adr-010-zero-telemetry-and-local-only-enforcement)
- [ADR-011: Localhost Dashboard & REST API Security Model](#adr-011-localhost-dashboard--rest-api-security-model) *(New)*
- [ADR-012: Minimal Non-Chrome Application Tracking (Zero Window Titles)](#adr-012-minimal-non-chrome-application-tracking-zero-window-titles) *(New)*

---

### ADR-001: Interactive User-Session Coordinator Architecture (Session 1+)

- **Date:** 2026-10-22
- **Status:** Approved (Reconciled)
- **Context:** The application requires access to the active user's desktop windows, `GetForegroundWindow()`, `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, and `WTSRegisterSessionNotification`. A conventional Windows Service executes in Session 0, which is isolated from the interactive desktop and cannot monitor desktop window focus or lock events.
- **Options Considered:**
  1. *Interactive User-Session Coordinator (Selected):* A background/system-tray application executing strictly in the active user's interactive logon session (Session 1+).
  2. *Session 0 Windows Service with IPC to a Helper:* Unnecessary complexity, introduces dual-process service overhead and security risks across session boundaries.
  3. *Pure Chrome Extension without desktop agent:* Cannot detect when non-Chrome applications have focus; cannot monitor Windows lock/sleep states.
- **Decision:** Implement `LocalActivityTracker.Coordinator.exe` running strictly in the interactive user session (Session 1+), launched via per-user Windows Startup (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- **Consequences:** Direct, low-latency access to Win32 desktop hooks and session lock events; eliminates Session 0 isolation bugs; simplifies process management.

---

### ADR-002: Local Dashboard Stack (React 18 + TypeScript + Tailwind)

- **Date:** 2026-10-22
- **Status:** Approved
- **Context:** The local dashboard requires interactive timelines, metric cards, a screenshot lightbox gallery, and an export configuration interface.
- **Options Considered:**
  1. *React 18 + TypeScript + Tailwind CSS (Selected):* Modern, accessible component ecosystem, static bundling via Vite, zero runtime dependencies.
  2. *Server-Side Rendered Razor / Blazor:* Heavier setup, less fluid client-side filtering for large timelines and galleries.
- **Decision:** Build a single-page React app with Vite and Tailwind CSS. The compiled static bundle is served by the Coordinator's embedded Kestrel server on `127.0.0.1:48120`.
- **Consequences:** Clean separation of concerns; offline-first; blazing fast client-side responsiveness.

---

### ADR-003: Dual-Protocol Architecture: Native Messaging Stdio & Windows Named Pipes IPC

- **Date:** 2026-10-22
- **Status:** Approved (Reconciled)
- **Context:** Chrome launches independent Native Messaging host processes per profile. Chrome MV3 extensions cannot open Named Pipes directly. Furthermore, one single process cannot automatically receive stdio streams from multiple independently launched Chrome profiles.
- **Options Considered:**
  1. *Dual-Protocol Bridge (Selected):* Chrome extension communicates with a lightweight Native Host bridge (`LocalActivityTracker.NativeHost.exe`) over standard I/O (stdio uint32 LE framing). The Native Host immediately bridges messages to the central Coordinator via Windows Named Pipes (`\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}`).
  2. *Single Process attempting stdio multiplexing:* Technically impossible on Windows because Chrome creates isolated stdin/stdout pipes for each extension instance.
  3. *Local WebSockets between extension and Coordinator:* Requires persistent open TCP ports, adds port collision risks, and lacks kernel-enforced user SID authentication.
- **Decision:** Implement a dual-protocol architecture: Stdio between Extension and Native Host; Windows Named Pipes between Native Host instances and the central Coordinator.
- **Consequences:** Conforms 100% to Chrome's extension security model while allowing the single Coordinator process to centralize duration accounting, SQLite storage, and screenshot scheduling.

---

### ADR-004: Multi-Profile Multiplexing & Connection Routing Strategy

- **Date:** 2026-10-22
- **Status:** Approved (Reconciled)
- **Context:** Multiple Chrome profiles run concurrently. The Coordinator must uniquely identify each profile, route screenshot requests to the correct profile, and prevent cross-profile event contamination.
- **Options Considered:**
  1. *Named Pipe Connection Handshake & Authentication (Selected):* Each Native Host instance connects to the Coordinator's pipe server and sends `IPC_HANDSHAKE` with its persistent `profile_id` (UUID generated in `chrome.storage.local`). The Coordinator verifies the client process token via `GetNamedPipeClientProcessId` and maps the pipe handle to that profile.
  2. *Dynamic Host Manifest Generation:* Complex, error-prone registry churn.
- **Decision:** Use Named Pipe connection registration with kernel-level PID validation. The Coordinator maintains a dictionary of active profile pipe channels for targeted request routing.
- **Consequences:** Robust multi-profile concurrency; zero cross-talk; secure process identity verification.

---

### ADR-005: Focus Reconciliation & Duration Arbitration Engine

- **Date:** 2026-10-22
- **Status:** Approved
- **Context:** Active URL duration must accrue *only* when a specific tab is selected *and* Chrome is the active foreground application on the Windows desktop. Background tabs, background profiles, and minimized browsers must not accrue time.
- **Options Considered:**
  1. *Dual-Sourced Focus Reconciliation (Selected):* The Coordinator uses Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` and `WTSRegisterSessionNotification(SessionLock)` to monitor OS-level foreground changes instantly. Tab state is received over Named Pipes. Active URL time accrues only during the intersection of Chrome OS focus and tab selection.
  2. *Chrome Window Focus Alone:* `chrome.windows.onFocusChanged` is unreliable when switching between desktop applications.
- **Decision:** Implement event-driven dual-sourced arbitration via Win32 hooks and Named Pipe tab events.
- **Consequences:** Sub-100ms latency, zero polling CPU overhead, 100% mathematically accurate duration accounting.

---

### ADR-006: Local Screenshot Pipeline & Targeted IPC Routing

- **Date:** 2026-10-22
- **Status:** Approved (Reconciled)
- **Context:** Screenshots must capture only authorized, visible Chrome tabs at a configurable interval (default: 10m). Desktop backgrounds and other applications must never be captured.
- **Options Considered:**
  1. *Targeted Pipe Dispatch & `chrome.tabs.captureVisibleTab` (Selected):* Coordinator timer fires, determines which profile currently holds foreground focus, and dispatches `REQUEST_SCREENSHOT` across that profile's Named Pipe. Extension captures visible tab and returns base64 image.
  2. *Broadcast to all connected profiles:* Violates visibility requirement; hidden windows cannot be captured cleanly by Chrome.
  3. *Windows Desktop Capture (BitBlt):* Violates privacy requirements; captures confidential documents in other apps.
- **Decision:** Dispatch screenshot requests strictly to the focused profile's pipe. Record honest statuses (`SUCCESS`, `RESTRICTED_URL`, `WINDOW_OCCLUDED_OR_MINIMIZED`) for every interval. Base64 images are never written to text logs.
- **Consequences:** Absolute privacy compliance; no desktop screenshots; truthful capture audit logs.

---

### ADR-007: Local Database Engine & Concurrency (SQLite WAL Mode)

- **Date:** 2026-10-22
- **Status:** Approved
- **Context:** High-frequency tracking events, screenshot metadata, and concurrent dashboard reads must operate without locking conflicts.
- **Options Considered:**
  1. *SQLite with Write-Ahead Logging (WAL) (Selected):* Single file, zero-configuration, readers never block writers, writers never block readers, full ACID durability.
  2. *Client-Server Database (PostgreSQL / MySQL):* Requires running background daemon services and violates local-first mandate.
- **Decision:** Use SQLite 3 via `Microsoft.Data.Sqlite` configured with `PRAGMA journal_mode = WAL;` and `PRAGMA synchronous = NORMAL;`.
- **Consequences:** Exceptional performance, zero background database server overhead, reliable file-based backups via `VACUUM INTO`.

---

### ADR-008: Master CSV Export Structure & Formula Injection Defense

- **Date:** 2026-10-22
- **Status:** Approved
- **Context:** A custom date range export must combine all enrolled profiles into a single CSV file, with distinct profile sections, while being safe to open in spreadsheet software.
- **Options Considered:**
  1. *Single Structured CSV with Section Headers & Formula Neutralization (Selected):* Single file containing 5 sections. Values starting with `=`, `+`, `-`, `@`, `\t`, `\r` are prefixed with `'` to neutralize formula injection.
  2. *Unsanitized CSV:* Critical security vulnerability allowing remote code execution if a visited page title contains a formula payload.
- **Decision:** Generate a single consolidated RFC 4180 CSV with UTF-8 BOM, structured section markers, date boundary clipping, and formula neutralization.
- **Consequences:** Single-file convenience, 100% Excel compatibility, and complete immunity against CSV injection attacks.

---

### ADR-009: URL Query Parameter Sanitization & Privacy Strategy

- **Date:** 2026-10-22
- **Status:** Approved
- **Context:** URLs frequently contain OAuth tokens, API keys, passwords, and session identifiers that must not be stored in SQLite or exported.
- **Options Considered:**
  1. *Deterministic Token Blacklist Sanitizer (Selected):* Strip sensitive query parameter keys (`token`, `auth`, `password`, `key`, `secret`, `code`, `sig`, etc.) and all URL fragments (`#hash`) both inside the extension and prior to database persistence.
  2. *Stripping All Query Parameters:* Breaks useful domain context.
- **Decision:** Implement deterministic blacklisted parameter sanitization while preserving non-sensitive query keys.
- **Consequences:** Protects user privacy and security without destroying legitimate browsing context.

---

### ADR-010: Zero-Telemetry and Local-Only Enforcement

- **Date:** 2026-10-22
- **Status:** Approved
- **Context:** The application must never communicate with remote servers, external APIs, cloud services, or telemetry endpoints.
- **Options Considered:**
  1. *Complete Network Isolation (Selected):* Embed all frontend dependencies locally; bind the Kestrel server strictly to `127.0.0.1`; do not include any remote HTTP client calls or telemetry SDKs.
  2. *Optional Cloud Sync:* Explicitly forbidden.
- **Decision:** Mandate 100% offline-only operation. Static code audits enforce zero outbound network requests.
- **Consequences:** Total privacy and data sovereignty for the user; works seamlessly on air-gapped workstations.

---

### ADR-011: Localhost Dashboard & REST API Security Model

- **Date:** 2026-10-22
- **Status:** Approved (New)
- **Context:** Binding to `127.0.0.1` restricts network exposure to the local computer, but does not prevent malicious websites executing in the user's browser from initiating cross-origin requests (e.g. DNS rebinding or CSRF) against the local API.
- **Options Considered:**
  1. *Defense-in-Depth Local Security (Selected):* 
     - Strict Host header validation (`127.0.0.1:48120` / `localhost:48120`).
     - Strict Origin header validation (reject unapproved browser origins).
     - Custom header requirement (`X-LAT-Requested-With: LocalActivityTracker`) on state-changing requests.
     - Capability Bearer Token (`auth.token` stored in user-restricted `%LOCALAPPDATA%`).
  2. *CORS Alone:* Insufficient because simple HTML forms and DNS rebinding bypass basic CORS checks.
  3. *Unauthenticated Localhost Server:* Vulnerable to local data deletion or screenshot exfiltration by malicious tabs.
- **Decision:** Implement strict Host/Origin checking, CSRF custom header enforcement, and Capability Bearer Token authorization.
- **Consequences:** Complete protection against cross-origin browser attacks and DNS rebinding while preserving frictionless local dashboard usage.

---

### ADR-012: Minimal Non-Chrome Application Tracking (Zero Window Titles)

- **Date:** 2026-10-22
- **Status:** Approved (New)
- **Context:** Tracking of applications other than Google Chrome must strictly answer *which application was foregrounded and for how long* without inspecting the user's confidential work (e.g. document titles, financial spreadsheet names, email subjects, or chat captions).
- **Options Considered:**
  1. *Process Executable & Display Name Only (Selected):* Record only `process_name` (e.g. `EXCEL.EXE`), `application_display_name` (e.g. `Microsoft Excel`), start timestamp, end timestamp, and duration. **Completely eliminate `window_title` from the database schema, API contracts, dashboard UI, and CSV export.**
  2. *Recording Window Titles with Partial Redaction:* High risk of leaking sensitive document names or contact names; complex and error-prone regex heuristics.
- **Decision:** Collect only executable name, application display name, and foreground duration. Strictly omit window titles.
- **Consequences:** Guarantees absolute privacy for confidential non-Chrome desktop workflows; simplifies database schema; eliminates document title leakage risks.
