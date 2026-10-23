# Requirements Traceability Matrix — Local Activity Tracker

> **Exhaustive Mapping of Non-Negotiable Requirements to Architecture, Schema, Tasks, and Test Cases**  
> *Phase 2 Reconciliation Deliverable \| Complete Bidirectional Traceability*

---

## 1. Traceability Matrix

| Req # | Requirement Scope | Architecture Component | Database Schema Component | Implementation Task ID | Verification / Test Case | Status |
| :---: | :--- | :--- | :--- | :--- | :--- | :---: |
| **REQ-01** | **Local-First Architecture**<br/>No cloud, VPS, remote DB, or external API | Interactive Coordinator (`src/Coordinator`), Local Kestrel (`127.0.0.1:48120`) | Local SQLite (`activity.db`) in `%LOCALAPPDATA%` | `TASK-001`, `TASK-049`, `TASK-069` | `E2E-06`: Offline test with Wi-Fi/Ethernet disabled | Reconciled & Verified |
| **REQ-02** | **Multi-Profile Chrome Tracking**<br/>Track multiple enrolled Chrome profiles independently | Chrome Extension (`profile.ts`), Native Host, Named Pipe Server | `chrome_profiles` table (`profile_id`, `profile_name`) | `TASK-025`, `TASK-026`, `TASK-028`, `TASK-072` | `E2E-01`: Concurrent browsing over Named Pipes | Reconciled & Verified |
| **REQ-03** | **Active URL Time Tracking**<br/>Only accrue when tab is active and Chrome is in foreground | Activity Arbiter (`ActivityArbiter.cs`), Extension Tab Tracker | `url_activity_sessions` table (`active_duration_seconds`) | `TASK-031`, `TASK-034`, `TASK-036` | `DurationCalculatorTests.cs`, `E2E-02`: Background tab exclusion | Reconciled & Verified |
| **REQ-04** | **Chrome Foreground Duration**<br/>Track total browser foreground time separately from URLs | Win32 Focus Hook (`SetWinEventHook`), Activity Arbiter | `browser_sessions` table (`duration_seconds`) | `TASK-035` | `DurationCalculatorTests.cs`: Foreground focus duration | Reconciled & Verified |
| **REQ-05** | **Non-Chrome App Tracking (Duration Only)**<br/>Executable & duration only; zero window titles | Win32 Hook, Process Resolver (`ProcessInfoResolver.cs`) | `application_sessions` table (**NO `window_title`**) | `TASK-038`, `TASK-039`, `TASK-040`, `TASK-041` | `ApplicationTrackerTests.cs`, `E2E-02`: Assert no titles stored | Reconciled & Verified |
| **REQ-06** | **Periodic Tab Screenshots**<br/>Configurable interval (default 10m), visible tab only | Coordinator Scheduler, Targeted Named Pipe Router | `screenshot_metadata` table, `%LOCALAPPDATA%\screenshots\` | `TASK-043`, `TASK-044`, `TASK-045`, `TASK-047` | `E2E-03`: Screenshot generation & gallery rendering | Reconciled & Verified |
| **REQ-07** | **Honest Screenshot Failure Reporting**<br/>Record restricted pages and failures without dropping | Extension error handler, Screenshot Router | `screenshot_metadata.capture_status` ('RESTRICTED') | `TASK-046` | `E2E-04`: Capture attempt on `chrome://extensions` | Reconciled & Verified |
| **REQ-08** | **Strict Anti-Surveillance Boundaries**<br/>No keylogging, no password/token, no window titles, no desktop capture | Architecture Policy, Win32 API restriction, URL Sanitizer | Stripped URLs in SQLite; zero raw inputs; zero window titles | `TASK-032`, `TASK-041`, `TASK-048`, `TASK-067` | Code scan: Absence of `WH_KEYBOARD_LL`, `BitBlt`, window titles | Reconciled & Verified |
| **REQ-09** | **Local SQLite Data Store**<br/>Indexed tables, parameterized queries, WAL mode | `SqliteConnectionFactory`, `DataRepository`, Migrations | 10 SQLite tables, WAL journaling, indices | `TASK-011`, `TASK-012`, `TASK-013`, `TASK-014` | `SqliteRepositoryTests.cs`: WAL mode & schema verify | Reconciled & Verified |
| **REQ-10** | **Hardened Local React Dashboard**<br/>Offline UI for overview, timeline, gallery, app usage | React 18, Vite, Tailwind CSS, Hardened Kestrel API | Loopback API (`127.0.0.1:48120`), Bearer token, CSRF check | `TASK-049` - `TASK-056` | `LocalApiSecurityTests.cs`: Host, Origin, CSRF, Token tests | Reconciled & Verified |
| **REQ-11** | **Master CSV Export (Unified Single File)**<br/>Combine all profiles with separate sections | `CsvExportService.cs`, UI Export Modal | Sliced SQLite queries across profiles | `TASK-057`, `TASK-058`, `TASK-063`, `TASK-073` | `E2E-05`: Single CSV file validation in Excel | Reconciled & Verified |
| **REQ-12** | **Custom Date Range & Boundary Clipping**<br/>Local timezone converted to UTC; clipped durations | Boundary Clipping Engine (`DateBoundaryClipping.cs`) | Query with `MAX(start, range_start)`, `MIN(end, range_end)` | `TASK-059` | `DateBoundaryClippingTests.cs`: Multi-boundary tests | Reconciled & Verified |
| **REQ-13** | **CSV Security & Formula Injection Defense**<br/>Neutralize `=`, `+`, `-`, `@`; RFC 4180 escaping | CSV Formatter (`CsvFormatter.cs`) | String escaping prior to file streaming | `TASK-060`, `TASK-061` | `CsvExportServiceTests.cs`: Injection payloads | Reconciled & Verified |
| **REQ-14** | **Interactive Session Lock & Recovery**<br/>WTS Session lock/unlock, sleep/resume, crash repair | `SessionNotificationListener`, `PowerLifecycleManager`, `RecoveryService` | `is_completed`, `session_status = 'RECOVERED'` | `TASK-064`, `TASK-065` | `CrashRecoveryServiceTests.cs`: Lock & crash tests | Reconciled & Verified |
| **REQ-15** | **Windows 10/11 Packaging & Installer**<br/>PowerShell setup, Native Messaging registration | `scripts/install.ps1`, `register-native-host.ps1` | Registry: `HKCU\Software\Google\Chrome` | `TASK-070`, `TASK-071` | Clean run of `install.ps1` on Windows workstation | Reconciled & Verified |

---

## 2. Cross-Document Consistency Review

- [x] **Interactive User Session Placement:** Coordinator runs in Session 1+ across all documents (`README.md`, `architecture.md`, `plan.md`, `task.md`, `testing.md`, `decisions.md`).
- [x] **Named Pipe IPC vs Stdio:** Stdio framing (Chrome Extension ↔ Native Host) and Named Pipe framing (Native Host ↔ Coordinator) are clearly separated and documented with independent message schemas in `api-contracts.md` and `architecture.md`.
- [x] **Localhost Dashboard Security:** Host validation, Origin validation, CSRF headers (`X-LAT-Requested-With`), and Capability Bearer Tokens are defined consistently across `architecture.md`, `api-contracts.md`, `privacy-security.md`, `ui-ux.md`, `task.md`, `testing.md`, and `decisions.md`.
- [x] **Minimal Non-Chrome Application Tracking:** `window_title` has been completely eliminated from `database-schema.md`, `architecture.md`, `api-contracts.md`, `ui-ux.md`, `privacy-security.md`, `task.md`, `testing.md`, and `decisions.md`.
- [x] **Requirements ↔ Task Backlog:** Every single requirement REQ-01 through REQ-15 has dedicated tasks in `task.md`.
- [x] **Tasks ↔ Acceptance Criteria:** Every task in `task.md` specifies an explicit test requirement and completion criteria.
