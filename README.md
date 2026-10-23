# Local Activity Tracker (Windows + Multi-Profile Chrome)

> **Local-First, Privacy-Respecting Activity & URL Monitoring for Windows 10 & 11**  
> *Track Chrome URLs, active tab durations, non-Chrome foreground application usage, authorized tab screenshots, and multi-profile CSV reports — strictly offline.*

---

## 🚀 Status: Production-Ready (100% Implemented & Verified)

- **Total Tasks:** 75 of 75 Completed (100%)
- **Automated Tests:** 114 Passing Tests (0 Failures)
- **Architecture:** 100% Offline-First & Pure Localhost (`127.0.0.1:48120`)
- **Privacy Invariant:** Strict Minimal Non-Chrome Tracking (**Zero Window Titles, Captions, or Document Contents**)
- **Platform:** Windows 10 & 11 (Interactive Desktop Session 1+)

---

## 1. Quick Start & Installation

### Option A: One-Click PowerShell Installer
Run PowerShell as your normal user (in your interactive desktop session):

```powershell
# 1. Run the one-click installer
.\scripts\install.ps1 -LaunchAfterInstall
```

What `install.ps1` does automatically:
1. Builds the .NET solution, compiles Chrome extension, and bundles the React Dashboard.
2. Stages files to `%LOCALAPPDATA%\LocalActivityTracker` (`bin`, `data`, `screenshots`, `logs`, `exports`, `config`, `backups`).
3. Registers the Chrome Native Messaging Host in `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.native_host`.
4. Creates a Windows Startup shortcut in your Start Menu Startup folder so tracking resumes seamlessly across reboots.
5. Hardens Windows NTFS ACLs, restricting access exclusively to your Windows user and `SYSTEM`.
6. Launches the Interactive Coordinator background tray app.

### Option B: Manual Build & Execution
```powershell
# Build entire system and run all 114 tests
.\build.ps1

# Start the Coordinator
.\scripts\start.ps1

# Stop the Coordinator
.\scripts\stop.ps1

# Uninstall cleanly (optional: add -PurgeData to erase database & screenshots)
.\scripts\uninstall.ps1
```

---

## 2. Multi-Profile Chrome Extension Enrollment

To track activity across multiple Chrome profiles on the same workstation:

1. Open **Google Chrome** in the profile you wish to enroll (e.g. `Default`, `Work Profile`).
2. Navigate to `chrome://extensions/` in the address bar.
3. Toggle ON **Developer mode** in the top-right corner.
4. Click **Load unpacked** and select the built extension folder:
   - Development: `<repo>\src\ChromeExtension\dist`
   - Installed: `%LOCALAPPDATA%\LocalActivityTracker\extension`
5. Click the **Local Activity Tracker** icon in the Chrome toolbar.
6. Verify the status displays **CONNECTED** with a unique Profile UUID.
7. Repeat steps 1–6 for each distinct Chrome profile you want to monitor.

All enrolled profiles independently stream tab state via their dedicated Native Messaging host instances across Windows Named Pipes into your single shared local SQLite database.

---

## 3. Local Dashboard Access

Open your browser to:
**[http://127.0.0.1:48120](http://127.0.0.1:48120)**

### Dashboard Features:
- **Overview:** Live KPI cards for Chrome Foreground Time, Active URL Time, Non-Chrome App Time, Enrolled Profiles, and Screenshot health.
- **Chrome Activity:** Filterable, paginated chronological URL history with domain and active viewing durations.
- **App Usage (Privacy-Safe):** Non-Chrome desktop applications (VS Code, Excel, Word, Slack) showing executable name and duration only (**ZERO window titles**).
- **Screenshots:** Thumbnail grid and full lightbox viewer for periodic visible tab captures (default: 10 minutes).
- **Profiles:** Health audit showing connected profiles, unmonitored profiles discovered from Chrome's `Local State`, and enrollment status.
- **Export CSV:** Master CSV report generator with custom date picker, profile selector, formula injection defense, and direct download / Explorer reveal.
- **Settings:** Configurable screenshot interval (60s–3600s), data retention selector, online database backup (`VACUUM INTO`), and safe data purge tools.

---

## 4. Key Architectural Invariants

### 1. Interactive Desktop Session (Session 1+)
The Coordinator runs inside the logged-in user's interactive desktop session, using Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` and `WTSRegisterSessionNotification(WTS_SESSION_LOCK)`. It is explicitly **not** a Session 0 service, preventing window handle isolation failures.

### 2. Dual-Sourced Focus Reconciliation & Duration Accuracy
Active URL viewing time accrues **only** at the mathematical intersection of:
$$\text{ForegroundWindow} == \text{Chrome HWND} \quad \land \quad \text{TabState} == \text{ACTIVE}$$
Background tabs and background profile windows accrue zero active URL time.

### 3. Non-Chrome Minimal Privacy Guarantee
For non-Chrome applications, only the executable process name (e.g. `Code.exe`) and friendly display name (`Visual Studio Code`) are recorded. Window titles, captions, document names, and internal UI contents are strictly eliminated from the database schema, domain models, REST API, React UI, and CSV export.

### 4. Excel-Safe CSV Formula Injection Neutralization
Fields starting with `=`, `+`, `-`, `@`, `\t`, or `\r` are automatically prepended with a single quote (`'`), neutralizing CSV formula injection when opened in Microsoft Excel. Files begin with a UTF-8 Byte Order Mark (`0xEF, 0xBB, 0xBF`) for clean Unicode rendering.

### 5. 100% Offline & Zero Telemetry
The application contains **zero external HTTP clients**, no remote analytics or tracking SDKs, no cloud backends, and no external CDNs. All assets and dependencies are bundled locally.

---

## 5. Technology Stack

- **Coordinator:** C# / .NET 8 Web SDK (Kestrel Minimal API on loopback `127.0.0.1:48120`, Win32 native hooks, System Tray).
- **Native Host:** C# / .NET 8 stdio-to-NamedPipe bridge with 100-event offline buffer.
- **Storage:** SQLite 3 in WAL mode via `Microsoft.Data.Sqlite` with 5000ms busy timeout.
- **Chrome Extension:** TypeScript, Manifest V3, Chrome Tabs / Windows / Storage / NativeMessaging APIs.
- **Dashboard:** React 18, TypeScript, Tailwind CSS, Vite, Lucide Icons.

---

## 6. Verification & Test Suite

Run the full automated test suite:
```powershell
dotnet test .\tests\LocalActivityTracker.Tests\LocalActivityTracker.Tests.csproj -c Release
```
```
Test run for ...\LocalActivityTracker.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Passed!  - Failed: 0, Passed: 114, Skipped: 0, Total: 114, Duration: 2 s
```

---

## 7. Documentation Index

| Document | Purpose |
| :--- | :--- |
| [`architecture.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/architecture.md) | Technical architecture, Named Pipe IPC, focus arbitration, API security. |
| [`plan.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/plan.md) | Phased implementation roadmap and verification milestones. |
| [`task.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/task.md) | Comprehensive task ledger (`TASK-001` through `TASK-075`). |
| [`database-schema.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/database-schema.md) | SQLite 10-table schema (no `window_title`), WAL settings, indices. |
| [`api-contracts.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/api-contracts.md) | Stdio protocols, Named Pipe framing, capability bearer token, REST endpoints. |
| [`privacy-security.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/privacy-security.md) | Privacy policies, anti-surveillance boundaries, loopback security. |
| [`docs/zero-telemetry-audit.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/docs/zero-telemetry-audit.md) | Static code audit verifying 100% offline isolation. |
| [`decisions.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/decisions.md) | Architecture Decision Records (ADR-001 through ADR-012). |
| [`requirements-traceability.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/requirements-traceability.md) | Full requirements-to-tests traceability matrix. |
| [`progress.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/progress.md) | Milestone completion and verification ledger. |
