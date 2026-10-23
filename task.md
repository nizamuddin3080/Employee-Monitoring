# Master Task Backlog — Local Activity Tracker

> **Actionable Task Ledger with Priorities, Dependencies, and Verification Criteria**  
> *Format: `[ ]` Not Started \| `[x]` Completed & Verified \| `[!]` Blocked*

---

## Summary Status

- **Total Tasks:** 75
- **Completed:** 75 (100% Complete — All Milestones 0 through 12 verified)
- **Pending:** 0
- **Blocked:** 0

---

## Phase 0 & Phase 1 — Discovery, Planning & Architectural Reconciliation

- [x] **`TASK-001` — Repository Discovery and System Environment Audit**
  - **Priority:** P0
  - **Dependencies:** None
  - **Description:** Audit local host OS, installed runtimes (.NET, Node.js, npm, git, Chrome), existing Chrome profiles, and filesystem paths.
  - **Expected Output:** Environment capabilities documented in `README.md` and `progress.md`.
  - **Verification:** PowerShell checks verifying Node v24.16.0, npm 11.13.0, git, winget, and existing Chrome profile paths.
  - **Completion Criteria:** Complete discovery log and zero assumptions about missing tools.

- [x] **`TASK-002` — Technical Architecture & Component Specification (Reconciled)**
  - **Priority:** P0
  - **Dependencies:** `TASK-001`
  - **Description:** Formulate complete system architecture in `architecture.md` incorporating the Interactive User-Session Coordinator (Session 1+), Multi-Host Named Pipe IPC, Hardened Localhost API security, and Minimal Non-Chrome app tracking (zero window titles).
  - **Expected Output:** `architecture.md` reconciled in project root.
  - **Verification:** Review of architecture against all master prompt and reconciliation requirements.
  - **Completion Criteria:** Mermaid diagrams, Named Pipe specifications, and security boundaries documented.

- [x] **`TASK-003` — Phased Implementation Plan & Traceability Matrix**
  - **Priority:** P0
  - **Dependencies:** `TASK-002`
  - **Description:** Create `plan.md` defining sequential phases with scope, deliverables, verification methods, and requirements traceability matrix.
  - **Expected Output:** `plan.md` reconciled in project root.
  - **Verification:** Consistency check between requirements matrix and planned milestones.
  - **Completion Criteria:** Every requirement mapped to tasks, schemas, and test cases.

- [x] **`TASK-004` — Database Schema, Migration & Time Accounting Specification**
  - **Priority:** P0
  - **Dependencies:** `TASK-002`
  - **Description:** Specify 10 SQLite tables without `window_title`, primary/foreign keys, indices, WAL configuration, URL redaction rules, and boundary clipping math in `database-schema.md`.
  - **Expected Output:** `database-schema.md` reconciled in project root.
  - **Verification:** Review of SQLite DDL against multi-profile and privacy requirements (`window_title` absent).
  - **Completion Criteria:** Complete table definitions, ER diagram, and mathematical formulations documented.

- [x] **`TASK-005` — API Contracts, UI/UX, Privacy & Testing Specifications**
  - **Priority:** P0
  - **Dependencies:** `TASK-002`, `TASK-003`
  - **Description:** Author `api-contracts.md` (Stdio, Named Pipes, and Hardened REST), `ui-ux.md`, `privacy-security.md`, `testing.md`, `progress.md`, and `decisions.md`.
  - **Expected Output:** All 6 specification documents written to project root.
  - **Verification:** Cross-document consistency review.
  - **Completion Criteria:** Zero missing planning files; strict alignment across contracts.

---

## Milestone 1 — Project Scaffolding & Development Environment

- [x] **`TASK-006` — Verify and Install .NET 8 SDK Environment**
  - **Priority:** P0
  - **Dependencies:** Explicit user approval (Phase 3 Checkpoint)
  - **Description:** Run `winget install Microsoft.DotNet.SDK.8` or `dotnet-install.ps1` to ensure `dotnet` CLI is available.
  - **Expected Output:** `dotnet --version` outputs `8.0.x`.
  - **Verification:** `dotnet --version` outputs `8.0.426` with exit code 0.
  - **Completion Criteria:** .NET 8 CLI functional on system.

- [x] **`TASK-007` — Solution & .NET Projects Scaffolding**
  - **Priority:** P0
  - **Dependencies:** `TASK-006`
  - **Description:** Create .NET solution `LocalActivityTracker.sln` containing `src/Coordinator` (Interactive background/tray app), `src/NativeHost` (Stdio-to-NamedPipe bridge), `src/Core` (shared models/contracts), and `tests/LocalActivityTracker.Tests`.
  - **Expected Output:** Solution and project files created with appropriate NuGet references (`Microsoft.Data.Sqlite`, `Dapper`).
  - **Verification:** `dotnet build .\LocalActivityTracker.sln -c Release` executes cleanly with 0 errors.
  - **Completion Criteria:** Build output verified for both executables and tests.

- [x] **`TASK-008` — Chrome Extension Project Scaffolding**
  - **Priority:** P1
  - **Dependencies:** `TASK-007`
  - **Description:** Scaffold TypeScript Manifest V3 extension workspace in `src/ChromeExtension` with Vite / TypeScript compiler setup and `manifest.json`.
  - **Expected Output:** `package.json`, `tsconfig.json`, `vite.config.ts`, and `manifest.json` configured.
  - **Verification:** `npm run build` generates valid extension bundle in `dist/` (`manifest.json` and `background.js`).
  - **Completion Criteria:** Extension bundle contains valid Manifest V3 structure.

- [x] **`TASK-009` — Local Dashboard Project Scaffolding**
  - **Priority:** P1
  - **Dependencies:** `TASK-007`
  - **Description:** Scaffold React 18 + TypeScript + Tailwind CSS workspace in `src/LocalDashboard` using Vite.
  - **Expected Output:** `src/LocalDashboard` with React components, Tailwind configuration, and Lucide icons.
  - **Verification:** `npm run build` produces static build in `src/LocalDashboard/dist` (`index.html` and assets).
  - **Completion Criteria:** Dashboard builds cleanly without errors.

- [x] **`TASK-010` — Root Directory & Build Automation Scripts**
  - **Priority:** P2
  - **Dependencies:** `TASK-007`, `TASK-008`, `TASK-009`
  - **Description:** Create root `build.ps1` and `clean.ps1` scripts to automate clean builds of all sub-projects in a single command.
  - **Expected Output:** PowerShell build scripts in root.
  - **Verification:** Execute `.\build.ps1` and verify all artifacts generated cleanly with message "Milestone 1 builds passed.".
  - **Completion Criteria:** Single script builds .NET Coordinator, Native Host, Extension, and Dashboard.

---

## Milestone 2 — SQLite Persistence Layer & Migrations

- [x] **`TASK-011` — Database Path & Directory Initializer**
  - **Priority:** P0
  - **Dependencies:** `TASK-007`
  - **Description:** Implement directory creation logic for `%LOCALAPPDATA%\LocalActivityTracker` (`data`, `screenshots`, `logs`, `exports`, `config`, `backups`) with proper user ACLs.
  - **Expected Output:** `StoragePathProvider.cs` creating directories safely.
  - **Verification:** Unit test `StoragePathProviderTests` asserting directory paths exist.
  - **Completion Criteria:** Folders created with current-user access permissions.

- [x] **`TASK-012` — SQLite Connection Factory & WAL Mode Configuration**
  - **Priority:** P0
  - **Dependencies:** `TASK-011`
  - **Description:** Implement `SqliteConnectionFactory.cs` setting `PRAGMA journal_mode = WAL;`, `PRAGMA synchronous = NORMAL;`, `PRAGMA foreign_keys = ON;`.
  - **Expected Output:** Factory class returning configured `SqliteConnection`.
  - **Verification:** Integration test `SqliteConnectionFactoryTests` checking `PRAGMA journal_mode` returns `wal`.
  - **Completion Criteria:** WAL mode verified on disk (`activity.db-wal` created).

- [x] **`TASK-013` — Schema Migration Runner & DDL Execution (No window_title)**
  - **Priority:** P0
  - **Dependencies:** `TASK-012`
  - **Description:** Implement `MigrationRunner.cs` executing SQL migration `001_initial_schema.sql` creating 10 core tables with `application_sessions` strictly omitting `window_title`.
  - **Expected Output:** Migration engine executing DDL for all 10 core tables.
  - **Verification:** Query `PRAGMA table_info(application_sessions)` in `MigrationRunnerTests`; assert `window_title` does not exist.
  - **Completion Criteria:** 10 tables created; zero `window_title` column.

- [x] **`TASK-014` — Index Creation & Query Performance Tuning**
  - **Priority:** P1
  - **Dependencies:** `TASK-013`
  - **Description:** Apply performance indices on `profile_id`, `start_time_utc`, `end_time_utc`, `domain`, `url`, `process_name`, and `session_id`.
  - **Expected Output:** Indices created in migration script.
  - **Verification:** Verified via `MigrationRunnerTests` and SQLite schema inspection.
  - **Completion Criteria:** All required indices present and active.

- [x] **`TASK-015` — Chrome Profiles & Settings Repository**
  - **Priority:** P1
  - **Dependencies:** `TASK-013`
  - **Description:** Implement repository methods for upserting profiles, updating connection timestamps, and reading/updating application settings.
  - **Expected Output:** `ProfileRepository.cs` and `SettingsRepository.cs`.
  - **Verification:** Integration test `ProfileAndSettingsRepositoryTests` inserting, updating, and querying profiles and settings.
  - **Completion Criteria:** Profile records persisted with UTC timestamps.

- [x] **`TASK-016` — Session & Event Activity Repository**
  - **Priority:** P0
  - **Dependencies:** `TASK-013`
  - **Description:** Implement `ActivityRepository.cs` for opening, heartbeat updating, and closing `url_activity_sessions` and `application_sessions`.
  - **Expected Output:** Data access methods with 100% parameterized queries.
  - **Verification:** Integration test `ActivityRepositoryTests` creating, heartbeating, and closing sessions with duration assertions.
  - **Completion Criteria:** Parameterized queries verified; zero raw SQL concatenation.

---

## Milestone 3 — Interactive Coordinator Foundation & Win32 Focus Monitor

- [x] **`TASK-017` — Win32 Interop Wrapper & P/Invoke Definitions**
  - **Priority:** P0
  - **Dependencies:** `TASK-007`
  - **Description:** Implement `Win32Native.cs` with P/Invoke signatures for `GetForegroundWindow`, `GetWindowThreadProcessId`, `QueryFullProcessImageName`, `SetWinEventHook`, `UnhookWinEvent`, `WTSRegisterSessionNotification`.
  - **Expected Output:** Safe, clean P/Invoke wrapper class.
  - **Verification:** Test calling `GetForegroundWindow` and retrieving active process name.
  - **Completion Criteria:** Retrieves current foreground window HWND and process name.

- [x] **`TASK-018` — Interactive Session Foreground Hook & Session Notification Listener**
  - **Priority:** P0
  - **Dependencies:** `TASK-017`
  - **Description:** Implement `FocusMonitor.cs` using `EVENT_SYSTEM_FOREGROUND` in the interactive desktop session (Session 1+) and `SessionNotificationListener.cs` handling `WTS_SESSION_LOCK` / `UNLOCK`.
  - **Expected Output:** Event-driven focus listener with lock/unlock notifications.
  - **Verification:** Run listener and switch applications / lock screen; assert events fire.
  - **Completion Criteria:** Focus changes received with sub-100ms latency; lock pauses tracking.

- [x] **`TASK-019` — Process Name & Application Display Name Resolver**
  - **Priority:** P1
  - **Dependencies:** `TASK-017`
  - **Description:** Implement `ProcessInfoResolver.cs` extracting executable filename and resolving friendly application display names from file version info (strictly NO window titles).
  - **Expected Output:** Resolves `Code.exe` -> `Visual Studio Code`, `EXCEL.EXE` -> `Microsoft Excel`.
  - **Verification:** Unit tests with standard Windows executables.
  - **Completion Criteria:** Clean display names resolved with executable fallback; zero window titles read.

- [x] **`TASK-020` — Interactive Coordinator Lifecycle & System Tray Host**
  - **Priority:** P0
  - **Dependencies:** `TASK-012`, `TASK-018`
  - **Description:** Build `CoordinatorApp.cs` running in the interactive user desktop session with tray icon, graceful start, cancellation token handling, and shutdown cleanup.
  - **Expected Output:** Background interactive desktop coordinator running continuously in Session 1+.
  - **Verification:** Run coordinator, assert graceful shutdown.
  - **Completion Criteria:** Starts in user session, logs to file, and shuts down cleanly.

---

## Milestone 4 — Native Messaging Host & Named Pipe IPC

- [x] **`TASK-021` — Stdio Message Framer (uint32 LE + JSON)**
  - **Priority:** P0
  - **Dependencies:** `TASK-007`
  - **Description:** Implement `NativeMessageStream.cs` handling 32-bit unsigned little-endian length prefix reading and writing on stdin/stdout capped at 1 MB.
  - **Expected Output:** Binary stream reader and writer conforming to Chrome Native Messaging.
  - **Verification:** Unit tests reading and writing framed binary buffers.
  - **Completion Criteria:** Passes framing compliance tests.

- [x] **`TASK-022` — Windows Named Pipe IPC Server & DACL Security**
  - **Priority:** P0
  - **Dependencies:** `TASK-020`
  - **Description:** Implement `NamedPipeIpcServer.cs` hosting `\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}` with current-user DACL and client PID verification.
  - **Expected Output:** Secure Named Pipe server accepting local client connections.
  - **Verification:** Integration test connecting client pipe; assert client PID verified against user SID.
  - **Completion Criteria:** Pipe connections restricted to running user; remote clients rejected.

- [x] **`TASK-023` — Native Host Stdio-to-NamedPipe Bridge**
  - **Priority:** P0
  - **Dependencies:** `TASK-021`, `TASK-022`
  - **Description:** Implement `src/NativeHost` bridging Stdio messages from Chrome extension to the Coordinator Named Pipe with reconnection buffering (up to 100 events).
  - **Expected Output:** Lightweight bridge console application.
  - **Verification:** Unit test sending Stdio message through bridge and receiving on Named Pipe.
  - **Completion Criteria:** Full message fidelity preserved across bridge.

- [x] **`TASK-024` — Native Messaging Host Manifest & Registry Registration**
  - **Priority:** P0
  - **Dependencies:** `TASK-023`
  - **Description:** Generate `com.localactivitytracker.agent.json` host manifest pointing to `LocalActivityTracker.NativeHost.exe` and PowerShell registration script `register-native-host.ps1`.
  - **Expected Output:** Valid manifest file and user registry registration script.
  - **Verification:** Registry check asserting key in `HKCU\Software\Google\Chrome\NativeMessagingHosts`.
  - **Completion Criteria:** Chrome recognizes and launches native host.

---

## Milestone 5 — Multi-Profile Enrollment & Identification

- [x] **`TASK-025` — Persistent Profile ID Generation in Extension**
  - **Priority:** P0
  - **Dependencies:** `TASK-008`
  - **Description:** Implement `profile.ts` checking `chrome.storage.local` for `profile_id`; generates cryptographic UUID on first run and persists it.
  - **Expected Output:** Profile ID persistent across browser sessions.
  - **Verification:** Unit test asserting UUID generated and retrieved from storage.
  - **Completion Criteria:** Profile ID remains constant across restarts.

- [x] **`TASK-026` — Profile Enrollment Handshake Protocol**
  - **Priority:** P0
  - **Dependencies:** `TASK-022`, `TASK-025`
  - **Description:** Send `IPC_HANDSHAKE` containing `profile_id`, `profile_name`, and extension version; Coordinator upserts `chrome_profiles` table.
  - **Expected Output:** `chrome_profiles` record created with `is_enrolled = 1`.
  - **Verification:** Inspect SQLite `chrome_profiles` after loading extension.
  - **Completion Criteria:** Profile marked enrolled and last seen timestamp updated.

- [x] **`TASK-027` — Profile Display Name Configuration UI**
  - **Priority:** P1
  - **Dependencies:** `TASK-025`
  - **Description:** Build extension action popup (`popup.html` / `popup.ts`) displaying current `profile_id`, connection status, and allowing user to edit `profile_name`.
  - **Expected Output:** Functional popup UI in extension toolbar.
  - **Verification:** Change profile name in popup; verify update in SQLite.
  - **Completion Criteria:** Custom profile name saved and transmitted to coordinator.

- [x] **`TASK-028` — Multi-Profile Concurrent Connection Multiplexing**
  - **Priority:** P0
  - **Dependencies:** `TASK-026`
  - **Description:** Support concurrent Named Pipe connections from multiple Chrome profile Native Host instances, routing each profile's events independently.
  - **Expected Output:** Coordinator multiplexes multiple profile pipe connections simultaneously.
  - **Verification:** Open two Chrome profiles with extension; verify events segregated.
  - **Completion Criteria:** No cross-contamination of profile events.

- [x] **`TASK-029` — Extension Disconnect & Reconnect Handling**
  - **Priority:** P1
  - **Dependencies:** `TASK-026`
  - **Description:** On pipe closure, update `chrome_profiles.connection_status = 'DISCONNECTED'` and flush open sessions.
  - **Expected Output:** Coordinator detects pipe closure and updates profile health.
  - **Verification:** Close Chrome profile; verify profile marked disconnected.
  - **Completion Criteria:** Clean disconnection handling without exceptions.

- [x] **`TASK-030` — Unenrolled Profile Detection & Coverage Warning**
  - **Priority:** P2
  - **Dependencies:** `TASK-026`
  - **Description:** Implement filesystem scanner reading Chrome's `Local State` in `%LOCALAPPDATA%\Google\Chrome\User Data` to detect unenrolled profiles.
  - **Expected Output:** List of enrolled vs unenrolled profiles for dashboard.
  - **Verification:** Compare scanned profiles against database enrolled profiles.
  - **Completion Criteria:** Honest coverage warning if unmonitored profiles exist.

---

## Milestone 6 — Active URL & Tab Duration Engine

- [x] **`TASK-031` — Chrome Extension Tab & Navigation Listeners**
  - **Priority:** P0
  - **Dependencies:** `TASK-023`
  - **Description:** Bind `chrome.tabs.onActivated`, `onUpdated`, `onRemoved`, `chrome.windows.onFocusChanged` in `background.ts`.
  - **Expected Output:** Real-time event dispatches over native messaging.
  - **Verification:** Navigate tabs in Chrome; verify events emitted.
  - **Completion Criteria:** Navigation and tab switches dispatched immediately.

- [x] **`TASK-032` — Privacy URL Sanitizer (Query Redaction)**
  - **Priority:** P0
  - **Dependencies:** `TASK-031`
  - **Description:** Implement URL sanitizer stripping sensitive query parameters (`token`, `auth`, `password`, `key`, `secret`, `sig`) and fragments before transmission.
  - **Expected Output:** `UrlSanitizer.ts` sanitizing URLs locally in extension.
  - **Verification:** Unit tests asserting sensitive tokens are redacted to `[REDACTED]`.
  - **Completion Criteria:** 100% of sensitive test query parameters sanitized.

- [x] **`TASK-033` — Domain & Hostname Extraction Utility**
  - **Priority:** P1
  - **Dependencies:** `TASK-032`
  - **Description:** Implement URL parser extracting clean domain and hostname (e.g., `github.com` from `https://github.com/repo`).
  - **Expected Output:** Extracted domain stored in `url_activity_sessions.domain`.
  - **Verification:** Unit tests with various URL formats (IPs, subdomains, ports).
  - **Completion Criteria:** Clean domain parsed and indexed.

- [x] **`TASK-034` — OS Focus Reconciliation & Active URL Arbiter**
  - **Priority:** P0
  - **Dependencies:** `TASK-018`, `TASK-031`
  - **Description:** Implement `ActivityArbiter.cs`: accrue active URL time **only** when Chrome is foreground window *and* tab is active.
  - **Expected Output:** URL session opened only when Chrome has OS focus.
  - **Verification:** Switch focus away from Chrome; verify active URL duration pauses.
  - **Completion Criteria:** Zero URL duration accrued while Chrome is in background.

- [x] **`TASK-035` — Browser Foreground Time Accounting**
  - **Priority:** P0
  - **Dependencies:** `TASK-034`
  - **Description:** Track overall Chrome foreground time in `browser_sessions` independently of individual URL sessions.
  - **Expected Output:** Distinct `browser_sessions` records capturing Chrome window focus.
  - **Verification:** Assert Chrome foreground time >= sum of active URL times.
  - **Completion Criteria:** Chrome foreground time recorded as an independent metric.

- [x] **`TASK-036` — Background Tab Time Exclusion Verification**
  - **Priority:** P0
  - **Dependencies:** `TASK-034`
  - **Description:** Explicitly verify that background tabs receive zero active duration even if playing media or running scripts.
  - **Expected Output:** Background tabs have no active duration records.
  - **Verification:** Open 5 background tabs; assert active duration accrues only on selected tab.
  - **Completion Criteria:** Background tab exclusion validated.

- [x] **`TASK-037` — Periodic Heartbeat & Session Flush**
  - **Priority:** P1
  - **Dependencies:** `TASK-034`
  - **Description:** Implement 10-second periodic heartbeat updating `last_heartbeat_utc` on active sessions to prevent data loss on sudden shutdown.
  - **Expected Output:** Heartbeats flushed to SQLite every 10 seconds.
  - **Verification:** Inspect SQLite session record during active browsing.
  - **Completion Criteria:** Active sessions continuously updated with recent heartbeat.

---

## Milestone 7 — Non-Chrome Application Usage Tracker (Duration Only)

- [x] **`TASK-038` — Non-Chrome Application Focus Detection**
  - **Priority:** P0
  - **Dependencies:** `TASK-018`, `TASK-034`
  - **Description:** When OS foreground window changes to an executable other than `chrome.exe`, close active Chrome session and trigger application session.
  - **Expected Output:** Non-Chrome app session initiated in `application_sessions`.
  - **Verification:** Switch to Visual Studio Code; verify new session created.
  - **Completion Criteria:** Accurate start timestamp recorded for external app.

- [x] **`TASK-039` — Executable & Display Name Recording (NO Window Titles)**
  - **Priority:** P0
  - **Dependencies:** `TASK-038`
  - **Description:** Record `ProcessName` (e.g., `devenv.exe`) and `ApplicationDisplayName` (e.g., `Visual Studio 2022`) in `application_sessions`. Strictly omit window titles.
  - **Expected Output:** Clean metadata stored in SQLite without `window_title`.
  - **Verification:** Assert database row contains process and app name; assert `window_title` is not queried or stored.
  - **Completion Criteria:** Correct executable and friendly names stored; zero window titles.

- [x] **`TASK-040` — Non-Chrome Duration Calculation & Zero Overlap**
  - **Priority:** P0
  - **Dependencies:** `TASK-038`
  - **Description:** Calculate exact duration when application loses focus. Guarantee zero double-counting between Chrome and external applications.
  - **Expected Output:** `DurationSeconds = EndTimeUtc - StartTimeUtc`.
  - **Verification:** Unit test asserting non-overlapping timestamp intervals.
  - **Completion Criteria:** Sum of Chrome time + non-Chrome time <= total elapsed time.

- [x] **`TASK-041` — Strict Privacy Boundary Enforcement (Non-Chrome Apps)**
  - **Priority:** P0
  - **Dependencies:** `TASK-038`
  - **Description:** Enforce zero internal inspection: no window text scraping, no window titles, no keystrokes, no document file paths, no clipboard access.
  - **Expected Output:** Code audit verifying absence of accessibility/UIAutomation hooks or window title capture.
  - **Verification:** Static analysis confirming only `QueryFullProcessImageName` / process info read.
  - **Completion Criteria:** Zero document contents, window captions, or internal activity collected.

- [x] **`TASK-042` — Chrome Exclusion from Application Usage Report**
  - **Priority:** P1
  - **Dependencies:** `TASK-038`
  - **Description:** Ensure `chrome.exe` is explicitly excluded from the `application_sessions` table and non-Chrome application reports.
  - **Expected Output:** `chrome.exe` never appears in `application_sessions`.
  - **Verification:** Filter SQLite query on `process_name = 'chrome.exe'`; assert count == 0.
  - **Completion Criteria:** Chrome activity tracked strictly via Chrome tables.

---

## Milestone 8 — Local Screenshot Capture & Targeted Routing Pipeline

- [x] **`TASK-043` — Screenshot Scheduler & Targeted Profile Router**
  - **Priority:** P0
  - **Dependencies:** `TASK-022`, `TASK-034`
  - **Description:** Implement `ScreenshotScheduler.cs` with configurable timer (default: 10 min) routing `IPC_REQUEST_SCREENSHOT` over the Named Pipe of the currently focused Chrome profile.
  - **Expected Output:** Targeted capture request sent to active profile pipe.
  - **Verification:** Test with 10-second interval; assert request routed to correct profile pipe.
  - **Completion Criteria:** Timer triggers capture requests accurately to focused profile.

- [x] **`TASK-044` — Extension Visible Tab Capture Handler**
  - **Priority:** P0
  - **Dependencies:** `TASK-043`
  - **Description:** In extension, handle `CAPTURE_SCREENSHOT_REQUEST` by calling `chrome.tabs.captureVisibleTab(null, { format: 'png' })`.
  - **Expected Output:** Base64 PNG returned to native host and forwarded over Named Pipe.
  - **Verification:** Assert valid base64 PNG data received by Coordinator.
  - **Completion Criteria:** Visible tab successfully captured as PNG.

- [x] **`TASK-045` — Local Filesystem Storage & Safe Filename Generation**
  - **Priority:** P0
  - **Dependencies:** `TASK-044`
  - **Description:** Decode PNG in Coordinator and save to `%LOCALAPPDATA%\LocalActivityTracker\screenshots\{profile_id}_{timestamp}_{guid}.png`.
  - **Expected Output:** PNG written to local disk; path traversal prevented; no images in logs.
  - **Verification:** Assert file exists on disk and is a valid PNG image.
  - **Completion Criteria:** Image written safely to local disk with unique filename.

- [x] **`TASK-046` — Restricted Pages & Honest Failure Recording**
  - **Priority:** P0
  - **Dependencies:** `TASK-044`
  - **Description:** When capture fails (restricted page, minimized window, browser internal page), return explicit failure code (`RESTRICTED_URL`, `WINDOW_OCCLUDED_OR_MINIMIZED`, `CAPTURE_TIMEOUT`).
  - **Expected Output:** Failure logged in `screenshot_metadata` with truthful status.
  - **Verification:** Trigger capture on `chrome://extensions`; assert failure recorded.
  - **Completion Criteria:** Failures logged with honest reasons; no silent drops.

- [x] **`TASK-047` — Screenshot Metadata SQLite Persistence**
  - **Priority:** P0
  - **Dependencies:** `TASK-045`, `TASK-046`
  - **Description:** Insert record into `screenshot_metadata` with `profile_id`, `capture_time_utc`, `url`, `file_path`, `status`, `failure_reason`.
  - **Expected Output:** Complete metadata record stored in database.
  - **Verification:** Query `screenshot_metadata` table to assert all fields populated.
  - **Completion Criteria:** Metadata accurately persisted for both successes and failures.

- [x] **`TASK-048` — Anti-Capture Safeguards (No Desktop Screenshots)**
  - **Priority:** P0
  - **Dependencies:** `TASK-045`
  - **Description:** Verify architecture strictly prohibits capturing non-Chrome applications or full Windows desktop.
  - **Expected Output:** Code audit confirming zero GDI/DirectX screenshot APIs.
  - **Verification:** Code scan verifying absence of `CreateCompatibleDC`, `BitBlt`, etc.
  - **Completion Criteria:** Only Chrome's authorized tab API used for captures.

---

## Milestone 9 — Hardened Local React Dashboard & API Gateway

- [x] **`TASK-049` — Localhost Kestrel API Server & Security Headers**
  - **Priority:** P0
  - **Dependencies:** `TASK-016`
  - **Description:** Implement ASP.NET Core Minimal API in Coordinator bound strictly to `127.0.0.1:48120` with Host and Origin header validation.
  - **Expected Output:** Server rejecting non-loopback requests and unapproved Host/Origin headers.
  - **Verification:** Send request with forged Host header; assert 400 Bad Request.
  - **Completion Criteria:** Strict Host/Origin validation enforced.

- [x] **`TASK-050` — Local Capability Token & CSRF Header Middleware**
  - **Priority:** P0
  - **Dependencies:** `TASK-049`
  - **Description:** Implement middleware generating `auth.token` on startup, validating `Authorization: Bearer <token>`, and enforcing `X-LAT-Requested-With: LocalActivityTracker` on mutation methods.
  - **Expected Output:** Bearer token and CSRF header protection on API endpoints.
  - **Verification:** Send POST without token or CSRF header; assert 401 / 403.
  - **Completion Criteria:** Protected endpoints require valid token and custom header.

- [x] **`TASK-051` — Overview KPI Cards & Live Activity Summary**
  - **Priority:** P1
  - **Dependencies:** `TASK-049`
  - **Description:** Build Dashboard Overview showing Today's Chrome Foreground, Active URL Time, Non-Chrome App Time, Enrolled Profiles, Visits, and Screenshot stats.
  - **Expected Output:** React KPI card grid with live database metrics.
  - **Verification:** Navigate to Dashboard; assert KPI values match SQLite records.
  - **Completion Criteria:** Accurate summary metrics displayed.

- [x] **`TASK-052` — Chrome Activity Timeline & URL History Table**
  - **Priority:** P1
  - **Dependencies:** `TASK-049`
  - **Description:** Build Chrome Activity view with date/profile/domain filters, search bar, and chronological timeline table.
  - **Expected Output:** Paginated activity table with duration and page titles.
  - **Verification:** Test filtering by profile and searching for specific URLs.
  - **Completion Criteria:** Filterable, paginated activity history displayed.

- [x] **`TASK-053` — Local Screenshot Gallery & Viewer**
  - **Priority:** P1
  - **Dependencies:** `TASK-045`, `TASK-049`
  - **Description:** Build Screenshot Gallery with thumbnail previews, date/profile filters, lightbox preview, and controls to open file locally or delete.
  - **Expected Output:** Responsive thumbnail grid with image lightbox.
  - **Verification:** Click thumbnail to open lightbox; click "Open in Explorer" to reveal file.
  - **Completion Criteria:** Gallery renders local PNGs with metadata overlays.

- [x] **`TASK-054` — Non-Chrome Application Usage View (NO Window Titles)**
  - **Priority:** P1
  - **Dependencies:** `TASK-049`
  - **Description:** Build Application Usage screen displaying executable name, application display name, session history, and daily summary bar chart (strictly omitting window titles).
  - **Expected Output:** App usage breakdown table and visual chart without window titles.
  - **Verification:** Assert external app sessions displayed with correct durations and zero window titles.
  - **Completion Criteria:** Clean non-Chrome application usage summary.

- [x] **`TASK-055` — Profile Management & Health Screen**
  - **Priority:** P1
  - **Dependencies:** `TASK-049`
  - **Description:** Build Profile Health view listing enrolled profiles, connection status badges, last seen timestamps, and enrollment guidance.
  - **Expected Output:** Profile status cards with enrollment wizard.
  - **Verification:** Verify connected profiles display green status badge.
  - **Completion Criteria:** Complete visibility into enrolled and unenrolled profiles.

- [x] **`TASK-056` — Application Settings, Controls & Offline Bundling**
  - **Priority:** P1
  - **Dependencies:** `TASK-049`
  - **Description:** Build Settings screen with screenshot interval slider, data retention selector, pause/resume tracking toggle, and verify 100% offline bundling.
  - **Expected Output:** Settings form persisting changes to `application_settings` table; zero external CDN calls.
  - **Verification:** Disable internet; assert dashboard functions completely.
  - **Completion Criteria:** Fully offline dashboard with working settings.
  - **Completion Criteria:** Fully offline dashboard with working settings.

---

## Milestone 10 — Custom Date Range Master CSV Export

- [x] **`TASK-057` — CSV Export API Endpoint & Request Validation**
  - **Priority:** P0
  - **Dependencies:** `TASK-049`, `TASK-050`
  - **Description:** Implement `POST /api/export/csv` accepting `startDate`, `endDate`, `profileIds`, and section inclusion flags with token authentication.
  - **Expected Output:** Validates date range; converts local date boundaries to UTC.
  - **Verification:** Unit test validating startDate <= endDate and timezone conversion.
  - **Completion Criteria:** Validates inputs and returns 400 for invalid ranges.

- [x] **`TASK-058` — Unified Multi-Section CSV Stream Generator**
  - **Priority:** P0
  - **Dependencies:** `TASK-057`
  - **Description:** Implement `CsvExportService.cs` generating a single RFC 4180 CSV file with 5 dedicated sections and section markers (Section 5 omitting window titles).
  - **Expected Output:** Single CSV file containing metadata, summary, profile sections, screenshots, and app usage without window titles.
  - **Verification:** Generate export; parse file and verify all 5 section headers exist.
  - **Completion Criteria:** Unified CSV generated with clear profile separation.

- [x] **`TASK-059` — Date Boundary Clipping Mathematics Engine**
  - **Priority:** P0
  - **Dependencies:** `TASK-058`
  - **Description:** Implement boundary clipping algorithm: clip overlapping sessions to `[StartUtc, EndUtc]`; calculate clipped duration accurately.
  - **Expected Output:** Sessions clipped cleanly; `Session Status` flagged `CLIPPED_BOUNDARY`.
  - **Verification:** Unit tests with sessions starting before, ending after, or spanning boundary.
  - **Completion Criteria:** Exact duration math verified; zero fabricated timestamps.

- [x] **`TASK-060` — Formula Injection Defense & RFC 4180 Escaping**
  - **Priority:** P0
  - **Dependencies:** `TASK-058`
  - **Description:** Neutralize formula injection: prepend `'` to values starting with `=`, `+`, `-`, `@`, `\t`, `\r`. Escape quotes and commas per RFC 4180.
  - **Expected Output:** Safe CSV values that cannot execute formulas in Excel.
  - **Verification:** Unit test with `=cmd|'/c calc'!A0` and assert prepended `'`.
  - **Completion Criteria:** Formula injection neutralized; Excel imports safely.

- [x] **`TASK-061` — UTF-8 BOM & Excel Compatibility**
  - **Priority:** P1
  - **Dependencies:** `TASK-058`
  - **Description:** Write UTF-8 Byte Order Mark (`0xEF, 0xBB, 0xBF`) at start of CSV file to guarantee correct Unicode rendering in Microsoft Excel.
  - **Expected Output:** Exported file contains UTF-8 BOM.
  - **Verification:** Inspect file byte header; verify non-ASCII characters display correctly in Excel.
  - **Completion Criteria:** UTF-8 BOM present; Unicode titles render cleanly.

- [x] **`TASK-062` — Bounded Batch Streaming for Large Exports**
  - **Priority:** P1
  - **Dependencies:** `TASK-058`
  - **Description:** Stream database records in bounded batches (1,000 records/batch) directly to output file to prevent high memory usage.
  - **Expected Output:** Low-memory streaming export.
  - **Verification:** Benchmark export with 50,000 mock records; assert memory < 50 MB.
  - **Completion Criteria:** Memory footprint remains constant regardless of export size.

- [x] **`TASK-063` — Dashboard CSV Export UI & Progress Modal**
  - **Priority:** P1
  - **Dependencies:** `TASK-057`, `TASK-058`
  - **Description:** Build Export modal in Dashboard with date picker, profile multiselect, section checkboxes, progress bar, and file path display.
  - **Expected Output:** Interactive export modal with download / open folder action.
  - **Verification:** Trigger export from UI; verify generated file opens from notification.
  - **Completion Criteria:** Intuitive export UX with record count confirmation.
  - **Verification:** Trigger export from UI; verify generated file opens from notification.
  - **Completion Criteria:** Intuitive export UX with record count confirmation.

---

## Milestone 11 — Reliability, Crash Recovery & Privacy Hardening

- [x] **`TASK-064` — Interactive Session Lock & Power Management Listener**
  - **Priority:** P0
  - **Dependencies:** `TASK-020`
  - **Description:** Register for `WTSRegisterSessionNotification` (`WTS_SESSION_LOCK` / `UNLOCK`) and `SystemEvents.PowerModeChanged`: flush sessions on Lock/Suspend.
  - **Expected Output:** No artificial duration accrued during lock or system sleep.
  - **Verification:** Trigger session lock; verify active sessions closed.
  - **Completion Criteria:** Clean session termination on lock and sleep.

- [x] **`TASK-065` — Coordinator Crash Recovery & Dangling Session Resolver**
  - **Priority:** P0
  - **Dependencies:** `TASK-016`
  - **Description:** On Coordinator startup, scan for unclosed sessions (`end_time_utc IS NULL`); close with `last_heartbeat_utc` and mark `RECOVERED_AFTER_CRASH`.
  - **Expected Output:** Automatic repair of dangling sessions.
  - **Verification:** Kill coordinator process abruptly; restart and verify dangling session closed.
  - **Completion Criteria:** Zero unclosed dangling sessions in SQLite.

- [x] **`TASK-066` — Database Backup, Restore & Safe Deletion**
  - **Priority:** P2
  - **Dependencies:** `TASK-013`
  - **Description:** Implement SQLite online backup API (`VACUUM INTO`) and safe data purge functions (delete older than N days or delete all data).
  - **Expected Output:** Backup creation and data deletion endpoints.
  - **Verification:** Test database backup creation and test purging records older than 30 days.
  - **Completion Criteria:** Verified backup file created; purged records cleanly deleted.

- [x] **`TASK-067` — Exhaustive URL Parameter Redaction Engine**
  - **Priority:** P0
  - **Dependencies:** `TASK-032`
  - **Description:** Validate URL sanitizer against extensive blacklist of authentication tokens, API keys, session cookies, and passwords.
  - **Expected Output:** Redaction test suite passing 100%.
  - **Verification:** Run unit test suite with 25+ real-world auth parameter patterns.
  - **Completion Criteria:** Zero sensitive tokens leaked to database.

- [x] **`TASK-068` — Filesystem ACL Hardening**
  - **Priority:** P1
  - **Dependencies:** `TASK-011`
  - **Description:** Set Windows NTFS file permissions restricting `%LOCALAPPDATA%\LocalActivityTracker` exclusively to current user and SYSTEM.
  - **Expected Output:** PowerShell script configuring NTFS ACLs.
  - **Verification:** Inspect file security descriptor using `Get-Acl`.
  - **Completion Criteria:** No world-writable or cross-user permissions.

- [x] **`TASK-069` — Zero-Telemetry & Offline Audit**
  - **Priority:** P0
  - **Dependencies:** All previous tasks
  - **Description:** Audit entire codebase to guarantee zero external HTTP requests, no third-party CDNs, and zero telemetry tracking.
  - **Expected Output:** Static code audit report confirming offline purity.
  - **Verification:** Code search for `fetch`, `HttpClient`, `WebRequest` targeting external domains.
  - **Completion Criteria:** 100% offline guarantee confirmed.

---

## Milestone 12 — Packaging, Setup Scripts & End-to-End Verification

- [x] **`TASK-070` — PowerShell Installer Script (`install.ps1`)**
  - **Priority:** P0
  - **Dependencies:** All build tasks
  - **Description:** Author `scripts/install.ps1` to build projects, create directory structure, register native host manifest, and create startup shortcut in interactive user session.
  - **Expected Output:** Working one-click installer script.
  - **Verification:** Execute `install.ps1` in PowerShell; assert all components installed.
  - **Completion Criteria:** Clean installation with zero manual configuration.

- [x] **`TASK-071` — Uninstaller Script (`uninstall.ps1`)**
  - **Priority:** P1
  - **Dependencies:** `TASK-070`
  - **Description:** Author `scripts/uninstall.ps1` to stop running coordinator, remove registry keys, remove startup shortcut, and optionally delete local data.
  - **Expected Output:** Clean uninstallation script.
  - **Verification:** Execute `uninstall.ps1`; assert registry keys and processes cleaned up.
  - **Completion Criteria:** System restored to pristine state.

- [x] **`TASK-072` — Multi-Profile Enrollment Verification Test**
  - **Priority:** P0
  - **Dependencies:** `TASK-028`, `TASK-070`
  - **Description:** Perform manual and automated test enrolling extension across 2 distinct Chrome profiles; verify concurrent activity tracked over Named Pipes.
  - **Expected Output:** SQLite records populated for both profiles simultaneously.
  - **Verification:** Browse in Profile A and Profile B; check SQLite for distinct `profile_id` rows.
  - **Completion Criteria:** Multi-profile isolation and concurrent tracking verified.

- [x] **`TASK-073` — Comprehensive Custom-Date-Range CSV Export Verification**
  - **Priority:** P0
  - **Dependencies:** `TASK-058`, `TASK-070`
  - **Description:** Generate unified CSV export for custom date range; verify all 5 sections, profile separation, duration clipping, zero window titles, and Excel compatibility.
  - **Expected Output:** Validated CSV file adhering to master prompt format.
  - **Verification:** Open CSV in Excel; verify section formatting, absence of window titles, and data accuracy.
  - **Completion Criteria:** All export requirements satisfied with actual data.

- [x] **`TASK-074` — Full Automated Test Suite Execution**
  - **Priority:** P0
  - **Dependencies:** `TASK-016`, `TASK-034`, `TASK-059`
  - **Description:** Run all xUnit and Vitest test suites across the solution.
  - **Expected Output:** 100% passing test execution report.
  - **Verification:** `dotnet test` and `npm test` exit code 0.
  - **Completion Criteria:** All automated assertions pass with evidence.

- [x] **`TASK-075` — Documentation Handover & Acceptance Sign-Off**
  - **Priority:** P0
  - **Dependencies:** All previous tasks
  - **Description:** Finalize `README.md`, update `progress.md`, and generate final verification report mapped against all 15 acceptance criteria.
  - **Expected Output:** Final project documentation and handover report.
  - **Verification:** Final checklist review against Master Development Prompt.
  - **Completion Criteria:** Complete, production-ready system delivered.
