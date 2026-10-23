# Project Progress Ledger — Local Activity Tracker

> **Execution Tracking, Milestone Status, Verification State, and Governance Ledger**  
> *Current Phase: Phase 1 & 2 (Planning & Specification Reconciled) \| Milestone: Pre-Implementation*

---

## 1. Overall Status Summary

| Attribute | Current Value | Notes |
| :--- | :--- | :--- |
| **Current Phase** | **Phase 5 (Project Complete & Acceptance Sign-Off)** | All 12 Milestones completed, tested, and verified. |
| **Implementation State** | **100% COMPLETE & PRODUCTION-READY** | Full solution built, 114 automated tests passing, one-click installer, startup shortcut, multi-profile isolation, and CSV export verified. |
| **Active Blockers** | **None** | Project ready for production use. |
| **Verified Toolchain** | Node.js v24.16.0, npm 11.13.0, git, .NET 8.0.426 | Full build & xUnit test suite passing via `.\build.ps1`. |
| **Total Tasks** | 75 | 75 complete, 0 pending (100%). |

---

## 2. Milestone Progress Overview

- [x] **Phase 0 — Repository Discovery & Environment Audit** *(Completed)*
- [x] **Phase 1 — Documentation & Technical Planning** *(Completed)*
- [x] **Phase 2 — Architectural Reconciliation & Cross-Document Audit** *(Completed)*
- [x] **Phase 3 — User Approval Checkpoint** *(Completed & Approved)*
- [x] **Milestone 1 — Project Scaffolding & Development Environment** *(Completed & Verified on Windows)*
- [x] **Milestone 2 — SQLite Persistence Layer & Migrations** *(Completed & Verified on Windows)*
- [x] **Milestone 3 — Interactive Coordinator Foundation & Win32 Focus Hook** *(Completed & Verified on Windows)*
- [x] **Milestone 4 — Native Messaging Host & Named Pipe IPC** *(Completed & Verified on Windows)*
- [x] **Milestone 5 — Multi-Profile Enrollment & Identification** *(Completed & Verified on Windows)*
- [x] **Milestone 6 — Active URL & Tab Duration Engine** *(Completed & Verified on Windows)*
- [x] **Milestone 7 — Non-Chrome Application Usage Tracker (Duration Only)** *(Completed & Verified on Windows)*
- [x] **Milestone 8 — Local Screenshot Capture & Targeted Routing Pipeline** *(Completed & Verified on Windows)*
- [x] **Milestone 9 — Hardened Local React Dashboard & API Gateway** *(Completed & Verified on Windows)*
- [x] **Milestone 10 — Custom Date Range Multi-Profile CSV Export** *(Completed & Verified on Windows)*
- [x] **Milestone 11 — Reliability, Crash Recovery & Privacy Hardening** *(Completed & Verified on Windows)*
- [x] **Milestone 12 — Packaging, Setup Scripts & Final Acceptance** *(Completed & Verified on Windows)*

---

## 3. Completed Reconciliation Work Log

- **Correction 1: Windows Interactive Session Placement:**
  - Architected the `LocalActivityTracker.Coordinator` to run strictly in the interactive desktop session (Session 1+), eliminating Session 0 desktop isolation failures.
  - Formulated the dual-protocol Native Messaging bridge via Windows Named Pipes (`\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}`) with per-user DACL and client PID verification.
  - Integrated `WTSRegisterSessionNotification` for instant pause on Windows session lock (`WTS_SESSION_LOCK`).
- **Correction 2: Multi-Profile IPC & Screenshot Routing:**
  - Documented concurrent Named Pipe multiplexing across independent Native Host instances.
  - Specified targeted screenshot routing: Coordinator dispatches `REQUEST_SCREENSHOT` over the specific Named Pipe connected to the currently active foreground profile.
  - Defined explicit failure codes (`RESTRICTED_URL`, `WINDOW_OCCLUDED_OR_MINIMIZED`, `CAPTURE_TIMEOUT`).
  - Prohibited screenshot image payloads from text diagnostic logs.
- **Correction 3: Localhost Dashboard Security:**
  - Restricted Kestrel binding strictly to `127.0.0.1:48120`.
  - Added strict Host header validation (`127.0.0.1:48120` / `localhost:48120`) to defeat DNS rebinding.
  - Added strict Origin header validation and banned wildcard CORS.
  - Added CSRF defense requiring `X-LAT-Requested-With: LocalActivityTracker` on mutation requests.
  - Specified Capability Bearer Token (`auth.token`) authentication.
- **Correction 4: Minimal Non-Chrome Application Data (NO Window Titles):**
  - **Completely removed `window_title`** from `database-schema.md` (table definition & ER diagram), `architecture.md`, `api-contracts.md`, `ui-ux.md`, `privacy-security.md`, `task.md`, `testing.md`, `decisions.md`, and `requirements-traceability.md`.
  - Confirmed non-Chrome tracking collects process executable name, application display name, and timestamps/duration only.
- **Formal Audit Deliverable:**
  - Authored `documentation-audit.md` detailing the entire reconciliation review.

- **Milestone 1 Host Execution & Verification:**
  - Installed Microsoft .NET 8.0 SDK (v8.0.426) locally via `dotnet-install.ps1` into `%LOCALAPPDATA%\Microsoft\dotnet` and added to User PATH.
  - Verified `dotnet --version` outputs `8.0.426`.
  - Built `LocalActivityTracker.sln` with `src/Coordinator`, `src/NativeHost`, `src/Core`, and `tests/LocalActivityTracker.Tests` (0 warnings, 0 errors).
  - Built `src/ChromeExtension` via TypeScript/Vite generating `dist/manifest.json` and `dist/background.js`.
  - Built `src/LocalDashboard` via React 18 / Tailwind / Vite generating `dist/index.html` and bundled assets.
  - Hardened `clean.ps1` to preserve `node_modules` and verified `.\build.ps1` executes end-to-end with exit code 0.

- **Milestone 2 SQLite Persistence Layer & Migrations:**
  - Added `Microsoft.Data.Sqlite` to `src/Core/Core.csproj` and configured xUnit testing packages in `tests/LocalActivityTracker.Tests/`.
  - Implemented `StoragePathProvider.cs` managing `%LOCALAPPDATA%\LocalActivityTracker` directories (`data`, `screenshots`, `logs`, `exports`, `config`, `backups`).
  - Implemented `SqliteConnectionFactory.cs` enforcing `PRAGMA journal_mode = WAL;`, `synchronous = NORMAL;`, `foreign_keys = ON;`, `busy_timeout = 5000;`.
  - Authored `001_initial_schema.sql` creating all 10 core tables cleanly omitting `window_title` with performance indices and default settings seed.
  - Implemented `MigrationRunner.cs` recording version 1 in `schema_migrations` with SHA256 checksums and transaction safety.
  - Implemented `ProfileRepository.cs`, `SettingsRepository.cs`, and `ActivityRepository.cs` with 100% parameterized queries.
  - Authored 9 automated unit/integration tests (`StoragePathProviderTests`, `SqliteConnectionFactoryTests`, `MigrationRunnerTests`, `ProfileAndSettingsRepositoryTests`, `ActivityRepositoryTests`) passing with 100% success rate.
  - Integrated automated test execution into `.\build.ps1`.

- **Milestone 3 Interactive Coordinator Foundation & Win32 Focus Monitor:**
  - Implemented `src/Core/Win32/Win32Native.cs` with complete P/Invoke definitions for `GetForegroundWindow`, `GetWindowThreadProcessId`, `OpenProcess`, `QueryFullProcessImageName`, `CloseHandle`, `SetWinEventHook`, `UnhookWinEvent`, `WTSRegisterSessionNotification`, `WTSUnRegisterSessionNotification`, window messaging, and shell notification icons.
  - Implemented `src/Core/Win32/ProcessInfoResolver.cs`: extracts executable name and friendly application display names with thread-safe caching. Strictly enforces zero collection of window titles, document names, or captions (verified via unit test reflection invariant).
  - Implemented `src/Core/Win32/FocusMonitor.cs`: runs `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` on a dedicated background message pump thread in the active user's interactive desktop session (Session 1+).
  - Implemented `src/Core/Win32/SessionNotificationListener.cs`: creates a hidden top-level window registering `WTSRegisterSessionNotification` for `WTS_SESSION_LOCK`/`UNLOCK` and `WM_POWERBROADCAST` sleep/suspend notifications.
  - Implemented `src/Core/Win32/SystemTray/Win32TrayIconManager.cs`: lightweight pure Win32 system tray notification icon (`Shell_NotifyIcon`) without WinForms/WPF runtime overhead.
  - Implemented `src/Coordinator/CoordinatorApp.cs` & `src/Coordinator/Program.cs`: verifies interactive user session (`SessionId > 0`), runs automatic SQLite migrations, executes crash recovery for dangling sessions on startup, manages focus and lock listeners, logs to console and `%LOCALAPPDATA%\LocalActivityTracker\logs\`, and supports graceful shutdown.
  - Expanded test suite to 32 automated unit/integration tests covering `Win32NativeTests`, `ProcessInfoResolverTests`, `FocusMonitorTests`, `SessionNotificationListenerTests`, and `CoordinatorAppTests` — all 32 tests passing (0 failures).

- **Milestone 4 Native Messaging Host & Named Pipe IPC:**
  - Implemented `src/Core/Ipc/NativeMessageStream.cs` handling 32-bit unsigned little-endian length prefix reading/writing with 1 MB cap on Stdio and 4 MB cap on Named Pipes.
  - Implemented `src/Core/Ipc/NamedPipeSecurityHelper.cs` constructing current-user + SYSTEM DACL security descriptors and verifying client PID token ownership against the logged-in user SID.
  - Implemented `src/Core/Ipc/NamedPipeIpcServer.cs` multiplexing multiple Chrome profile Native Host instances over `\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}`, managing handshakes, heartbeats, and targeted message routing.
  - Implemented `src/Core/Ipc/NamedPipeIpcClient.cs` providing robust connection management, handshake correlation, and bidirectional messaging.
  - Implemented `src/NativeHost/NativeMessagingBridge.cs` bridging Chrome Extension standard I/O to the Coordinator Named Pipe with 100-event offline memory buffering and exponential reconnect backoff.
  - Created `register-native-host.ps1` and `unregister-native-host.ps1` automating per-user Chrome Native Messaging host registration in `HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.agent`.
  - Expanded test suite to 41 automated unit/integration tests covering framing limits, partial stream recovery, client PID validation, bidirectional IPC messaging, and offline buffering.

- **Milestone 5 Multi-Profile Enrollment & Identification:**
  - Implemented persistent UUIDv4 generation via `crypto.randomUUID()` stored in `chrome.storage.local` (`src/ChromeExtension/src/profile.ts`).
  - Implemented extension action popup (`popup.html` & `src/ChromeExtension/src/popup.ts`) displaying Profile ID, editable profile name, connection status badge, and diagnostic state.
  - Implemented background connection lifecycle (`src/ChromeExtension/src/background.ts`) connecting to `com.localactivitytracker.agent`, dispatching `CLIENT_HELLO`, maintaining heartbeats, and handling exponential reconnection backoff.
  - Implemented `ChromeProfileScanner.cs` inspecting `%LOCALAPPDATA%\Google\Chrome\User Data\Local State` to detect local Chrome profiles and identify unmonitored profiles for honest coverage reporting.
  - Multi-profile pipe multiplexing wired into `CoordinatorApp.cs` and `NamedPipeIpcServer.cs` updating `chrome_profiles` table on handshake and pipe disconnect.
  - Authored 7 automated tests in `ChromeProfileScannerTests.cs`.

- **Milestone 6 Active URL & Tab Duration Engine:**
  - Implemented privacy URL sanitizers: client-side `src/ChromeExtension/src/url-sanitizer.ts` and server-side `src/Core/Privacy/UrlSanitizer.cs` stripping sensitive query params (`token`, `auth`, `password`, `key`, `secret`, `sig`, `session`, `jwt`, `code`, `access_token`), credential userinfo (`user:pass@`), and fragment tokens. Added domain and hostname extraction.
  - Implemented Chrome tab listeners in `background.ts`: `chrome.tabs.onActivated`, `onUpdated`, `onRemoved`, `chrome.windows.onFocusChanged`. Strictly excluded incognito tabs (`tab.incognito === true`).
  - Implemented `ActivityArbiter.cs`:
    - Accrues active URL time strictly during the intersection of Chrome OS desktop focus and tab selection.
    - Pauses active URL tracking instantly when desktop focus moves away from Chrome or window switches.
    - Accrues overall Chrome foreground time in `browser_sessions` independently of individual URL sessions.
    - Guarantees background tabs receive zero active duration.
    - Opens and closes non-Chrome `application_sessions` on desktop unfocus without collecting window titles.
    - Includes periodic 15-second heartbeat timer flushing active sessions to SQLite to prevent data loss.
    - Automatically pauses active sessions on `WTS_SESSION_LOCK` and power suspend, resuming cleanly on unlock.
  - Added `InsertTabEventAsync`, `OpenUrlSessionAsync`, `CloseUrlSessionAsync`, `OpenBrowserSessionAsync`, and `CloseBrowserSessionAsync` to `ActivityRepository.cs`.
  - Authored 11 automated tests in `ActivityArbiterTests.cs` and 9 tests in `UrlSanitizerTests.cs`.
  - Expanded total test suite to **59 passing tests** (0 failures).

- **Milestone 7 Non-Chrome Application Usage Tracker (Duration Only):**
  - Enhanced `ActivityArbiter.cs` with seamless non-Chrome focus tracking across arbitrary external executables (e.g., `Code.exe`, `EXCEL.EXE`, `slack.exe`, `notepad.exe`).
  - Added `BrowserSession` tracking to accurately maintain overall Chrome OS foreground duration independently of tab URL sessions.
  - Implemented strict zero-overlap duration accounting (`DurationSeconds = EndTimeUtc - StartTimeUtc`) ensuring non-overlapping interval boundaries and zero double-counting.
  - Strictly enforced zero collection of window titles, window captions, document names, or clipboard/keystroke data across models, database schema, and runtime resolvers.
  - Enforced explicit `chrome.exe` exclusion in `ActivityRepository.OpenApplicationSessionAsync` (throws `InvalidOperationException`) and added SQL query filters (`AND LOWER(process_name) != 'chrome.exe'`) in `GetApplicationSessionsInRangeAsync`.
  - Authored comprehensive test suite `NonChromeApplicationTrackingTests.cs` verifying `TASK-038`, `TASK-039`, `TASK-040`, `TASK-041`, and `TASK-042`.
  - Expanded total test suite to **64 passing tests** (0 failures).

- **Milestone 8 Local Screenshot Capture & Targeted Routing Pipeline:**
  - Implemented `IScreenshotScheduler.cs` and `ScreenshotScheduler.cs` in `src/Core/Screenshots/`.
  - Configurable periodic timer (default 600s / 10m, reading `screenshot_interval_seconds` from `ISettingsRepository`).
  - Targeted routing: dispatches `IpcMessage.RequestScreenshot` (`IPC_REQUEST_SCREENSHOT`) exclusively over the Named Pipe connected to the currently active foreground Chrome profile.
  - Extension visible tab handler in `background.ts` captures via `chrome.tabs.captureVisibleTab(null, { format: 'png' })`.
  - Safe decoding & storage: verifies PNG 8-byte magic header, sanitizes profile ID to prevent directory traversal, writes file to `%LOCALAPPDATA%\LocalActivityTracker\screenshots\{profile_id}_{timestamp}_{guid}.png`.
  - Truthful failure recording: captures errors like `RESTRICTED_URL`, `WINDOW_OCCLUDED_OR_MINIMIZED`, and `CAPTURE_TIMEOUT` in `screenshot_metadata`.
  - Anti-Capture Safeguards (`TASK-048`): strictly skips captures if Chrome is not foregrounded or session is locked; zero GDI/DirectX desktop capture APIs exist in the codebase.
  - Logging privacy: strictly zero base64 or raw image bytes in text logs.
  - Authored comprehensive test suite `ScreenshotSchedulerTests.cs` (7 new tests).
  - Expanded total test suite to **71 passing tests** (0 failures).

- **Milestone 9 Hardened Local React Dashboard & API Gateway:**
  - Implemented `LocalCapabilityTokenProvider.cs` generating 256-bit CSPRNG bearer token in `%LOCALAPPDATA%\LocalActivityTracker\config\auth.token`.
  - Implemented `ApiSecurityMiddleware.cs` enforcing strict loopback IPv4 check, Host/Origin whitelist, CSRF header (`X-LAT-Requested-With: LocalActivityTracker`), and Bearer token authentication.
  - Implemented `CoordinatorApiHost.cs` using Kestrel Minimal API on `127.0.0.1:48120` hosting REST endpoints and serving bundled SPA.
  - Built React 18 / Tailwind CSS / Lucide SPA in `src/LocalDashboard` (`Navbar`, `OverviewView`, `ActivityView`, `ApplicationsView`, `ScreenshotsView`, `ProfilesView`, `SettingsView`).
  - Authored comprehensive test suite `ApiGatewayAndSecurityTests.cs` (6 tests). Total test suite expanded to **77 passing tests**.

- **Milestone 10 Custom Date Range Multi-Profile CSV Export:**
  - Implemented `CsvFormatter.cs` with RFC-4180 quote escaping and formula injection defense prepending `'` to `=`, `+`, `-`, `@`, `\t`, `\r`.
  - Implemented `CsvExportRequest.cs` and `CsvExportResult.cs` with JSON property mapping.
  - Implemented `ICsvExportService.cs` and `CsvExportService.cs` in `src/Core/Export/`:
    - Mathematical boundary clipping engine for sessions crossing start/end range dates. Clipped sessions flagged `CLIPPED_BOUNDARY`.
    - Native UTF-8 Byte Order Mark (BOM `0xEF, 0xBB, 0xBF`) generation for seamless Microsoft Excel compatibility.
    - 5 dedicated sections: `# SECTION: REPORT_METADATA`, `# SECTION: PROFILE_SUMMARY`, `# SECTION: CHROME_PROFILE: {profile_id} ({name})`, `# SECTION: SCREENSHOT_METADATA`, and `# SECTION: NON_CHROME_APPLICATION_USAGE`.
    - Strict privacy guarantee: Section 5 contains strictly executable name, friendly application name, and duration only (**ZERO window titles**).
  - Implemented `POST /api/export/csv`, `GET /api/export/download`, and `POST /api/export/reveal` in `CoordinatorApiHost.cs`.
  - Created interactive `ExportView.tsx` component in `src/LocalDashboard/src/components/ExportView.tsx` with date pickers, profile checkboxes, section toggles, progress indicator, direct file download, and Windows Explorer reveal.
  - Authored comprehensive test suite `CsvExportEngineTests.cs` (14 new tests). Total automated test suite expanded to **91 passing tests** (0 failures).

- **Milestone 11 Reliability, Crash Recovery & Privacy Hardening:**
  - `SessionNotificationListener.cs` and `CoordinatorApp.cs` handle Windows session lock (`WTS_SESSION_LOCK`/`UNLOCK`) and power suspend/resume (`PBT_APMSUSPEND`), closing active sessions and pausing duration accrual.
  - Implemented `ActivityRepository.RecoverDanglingSessionsAsync()` repairing dangling unclosed sessions on startup, reconciling end time with `last_heartbeat_utc`, and marking `session_status = 'RECOVERED_AFTER_CRASH'`.
  - Implemented `DatabaseMaintenanceService.cs` (`IDatabaseMaintenanceService`) with zero-locking SQLite online backups (`VACUUM INTO`), data retention purges unlinking physical PNGs before record deletion, and total data purges with SQLite `VACUUM`.
  - Exposed REST maintenance endpoints `POST /api/data/backup` and `POST /api/data/purge` in `CoordinatorApiHost.cs`, and added UI controls in Dashboard `SettingsView.tsx`.
  - Expanded URL parameter redaction blacklist in both `UrlSanitizer.cs` and `url-sanitizer.ts` across 40+ real-world sensitive auth/token/secret patterns.
  - Implemented `StoragePathProvider.EnforceStrictUserAcl()` and authored `scripts/harden-storage-acl.ps1`, stripping inheritance and restricting `%LOCALAPPDATA%\LocalActivityTracker` exclusively to current user and SYSTEM.
  - Conducted static code audit `docs/zero-telemetry-audit.md` confirming 100% offline isolation, zero outbound HTTP clients, and no external CDNs or telemetry.
  - Authored comprehensive test suite `ReliabilityAndPrivacyHardeningTests.cs` (20 new tests). Total automated test suite expanded to **111 passing tests** (0 failures).

- **Milestone 12 Packaging, Setup Scripts & End-to-End Acceptance:**
  - Authored `scripts/install.ps1`: One-click installer staging binaries to `%LOCALAPPDATA%\LocalActivityTracker\bin`, configuring Native Messaging registry key in HKCU, generating Windows Startup shortcut, and enforcing NTFS ACLs.
  - Authored `scripts/uninstall.ps1`: Clean uninstaller gracefully terminating processes, removing HKCU registry keys, deleting startup shortcut, and offering optional `-PurgeData` switch.
  - Authored `scripts/start.ps1` and `scripts/stop.ps1` helper utilities.
  - Authored `EndToEndAcceptanceTests.cs` validating multi-profile concurrent tracking isolation, custom-date-range CSV export with formula injection defense and zero window titles, and installer script syntax integrity.
  - Finalized `README.md` and `task.md`.
  - Full solution build passes with **114 automated tests passing** (0 failures).

---

## 4. Pending Work

- **None** — All 75 tasks across Milestones 0 through 12 are 100% completed, verified, and signed off.

---

## 5. Active Decisions Awaiting Final Approval

- **ADR-001 (Reconciled):** Interactive User-Session Coordinator (.NET 8 Tray/Background App in Session 1+).
- **ADR-003 (Reconciled):** Dual-Protocol Architecture: Stdio Native Messaging + Windows Named Pipes IPC.
- **ADR-004 (Reconciled):** Multi-Profile Multiplexing & Targeted Named Pipe Screenshot Routing.
- **ADR-011 (New):** Localhost Dashboard & REST API Security Model (Host/Origin validation, CSRF headers, Capability Bearer Token).
- **ADR-012 (New):** Minimal Non-Chrome Application Tracking (Process Executable & Duration Only; Zero Window Titles).
