# Documentation Audit & Architectural Reconciliation Report

> **Comprehensive Audit, Technical Resolution of Four Architectural Issues, and Cross-Document Verification**  
> *Target System: Local Activity Tracker \| Execution Mode: Documentation Only (No Application Code)*

---

## 1. Audit Findings & Discovery

An initial audit of the planning documents identified four technical discrepancies between the initial specifications and Windows desktop/browser security realities:
1. **Windows Interactive Session Mismatch:** The previous architecture described a `.NET Worker Service` running in the background. Windows Services run in **Session 0**, which is isolated from the interactive desktop and cannot inspect window handles (`HWND`), query `GetForegroundWindow()`, or listen to `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` on the logged-in user's desktop (Session 1+).
2. **Multi-Profile Native Messaging Multiplexing:** Chrome isolates profiles completely and spawns an independent Native Messaging host process for each enrolled profile extension. A single process cannot assume it directly receives stdio streams from multiple independently launched Chrome profiles without an explicit local IPC coordinator.
3. **Localhost API Security Model:** Binding to `127.0.0.1` prevents remote network access, but does not prevent malicious websites executing in the user's browser from issuing cross-origin HTTP requests (e.g., DNS rebinding or CSRF) against the unauthenticated local server.
4. **Non-Chrome Application Scope Overreach:** The database schema and privacy documents included `window_title` in the `application_sessions` table and CSV exports, exceeding the non-negotiable user requirement to collect application identity and duration only.

---

## 2. Summary of the Four Technical Corrections

### Correction 1: Interactive User-Session Coordinator (Session 1+)
- **Resolution:** Retired the Session 0 Windows Service model in favor of the **Interactive User-Session Coordinator** (`LocalActivityTracker.Coordinator.exe`).
- **Placement:** Executes strictly within the signed-in user's interactive logon session (Session 1+) via the user's startup registry key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- **Capabilities:** Direct, native access to `GetForegroundWindow()`, `SetWinEventHook`, `QueryFullProcessImageName`, and `WTSRegisterSessionNotification` (`WTS_SESSION_LOCK` / `UNLOCK`).
- **Durations:** Windows lock and sleep events immediately suspend active URL duration accrual.

### Correction 2: Multi-Host Named Pipe IPC & Targeted Screenshot Routing
- **Resolution:** Architected a **Dual-Protocol Bridge**:
  1. *Stdio Layer:* Each Chrome profile extension talks to its own Native Messaging host instance (`LocalActivityTracker.NativeHost.exe`) via length-prefixed standard I/O (uint32 LE + JSON, max 1 MB).
  2. *Named Pipe IPC Layer:* Each Native Host connects to the central Coordinator via an authenticated Windows Named Pipe (`\\.\pipe\LocalActivityTracker-Coordinator-{SessionId}`).
- **Security & Multiplexing:** Pipe DACL restricts access exclusively to the current user's SID and SYSTEM. Coordinator verifies client PID via `GetNamedPipeClientProcessId`.
- **Targeted Screenshot Routing:** The Coordinator schedules captures and routes `REQUEST_SCREENSHOT` over the specific Named Pipe corresponding to the currently focused Chrome profile.
- **Constraints & Log Hygiene:** Visible tab only (`captureVisibleTab`). Explicit failure statuses (`RESTRICTED_URL`, `WINDOW_OCCLUDED_OR_MINIMIZED`, `CAPTURE_TIMEOUT`). Image base64 payloads are **strictly forbidden** from diagnostic text logs.

### Correction 3: Hardened Localhost API Security Model
- **Interface Binding:** Binds strictly to `http://127.0.0.1:48120` (never `0.0.0.0`).
- **Host Header Validation:** Strictly accepts `127.0.0.1:48120` and `localhost:48120`. Rejects unapproved Host headers with `400 Bad Request` to neutralize DNS rebinding.
- **Origin Header Validation:** Accepts only `http://127.0.0.1:48120` (and `http://localhost:5173` strictly in development mode). Prohibits wildcard CORS (`*`).
- **CSRF Defense:** Requires custom header `X-LAT-Requested-With: LocalActivityTracker` on all mutation requests (`POST`, `PUT`, `DELETE`).
- **Capability Bearer Token:** Coordinator generates a random 256-bit token stored in `%LOCALAPPDATA%\LocalActivityTracker\config\auth.token` (NTFS ACL restricted to current user). Sensitive endpoints require `Authorization: Bearer <token>`.

### Correction 4: Minimal Non-Chrome Application Data (Zero Window Titles)
- **Scope Restriction:** Non-Chrome tracking collects **strictly** the process executable name (e.g. `EXCEL.EXE`), application display name (e.g. `Microsoft Excel`), and start/end foreground timestamps and duration.
- **Schema & DDL Removal:** Completely eliminated `window_title` from `database-schema.md` (`application_sessions` table and ER diagram).
- **UI & CSV Removal:** Removed window title columns from `ui-ux.md`, `api-contracts.md`, and CSV export Section 5. Window titles, document names, spreadsheets, emails, and chat captions are never inspected, stored, or exported.

---

## 3. Files Modified

All 12 existing Markdown documentation files were updated in place:

1. [`README.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/README.md)
2. [`architecture.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/architecture.md)
3. [`api-contracts.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/api-contracts.md)
4. [`database-schema.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/database-schema.md)
5. [`privacy-security.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/privacy-security.md)
6. [`ui-ux.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/ui-ux.md)
7. [`plan.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/plan.md)
8. [`task.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/task.md)
9. [`testing.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/testing.md)
10. [`progress.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/progress.md)
11. [`decisions.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/decisions.md)
12. [`requirements-traceability.md`](file:///c:/Users/h/Music/local%20Monitoring%20system/requirements-traceability.md)

---

## 4. Summary of Sections Changed

| Document | Primary Sections Revised | Nature of Changes |
| :--- | :--- | :--- |
| `README.md` | Architecture diagram, Main Features, Tech Stack | Replaced Worker Service with Interactive Session Coordinator; added Named Pipe IPC and API security; removed window titles. |
| `architecture.md` | §1, §2, §3, §4, §5, §6, Diagrams | Redesigned process topology for Session 1+; detailed Named Pipe IPC protocol; added screenshot routing and localhost API security; removed window titles. |
| `api-contracts.md` | §1, §2, §3, §4 | Formally split Stdio protocol from Named Pipe IPC protocol; documented IPC handshake, correlation IDs, and screenshot routing; added Host/Origin/CSRF/Token security specs; removed `window_title`. |
| `database-schema.md` | §2 ER Diagram, §3.7 `application_sessions`, §5.1 SQL | Completely deleted `window_title` column from DDL, ER diagram, and export queries; documented initial migration strategy. |
| `privacy-security.md` | §1, §2, §4, §5, §8 Threat Model | Removed window captions from permitted scope; added full Localhost Security Model (Host, Origin, CSRF, Token); documented Named Pipe DACLs and client PID verification. |
| `ui-ux.md` | §2.1, §3.4, §3.7 | Removed window titles from Non-Chrome App Usage table and CSV export modal; added dashboard capability token bootstrap UX. |
| `plan.md` | §2 Milestones 1, 3, 4, 7, 9, 10, §3 Matrix | Updated milestones for Interactive Coordinator, Named Pipe IPC, Host/Origin/Token API security, and duration-only non-Chrome app tracking. |
| `task.md` | `TASK-007`, `TASK-013`, `TASK-017`–`024`, `TASK-038`–`042`, `TASK-049`–`050`, `TASK-054` | Reconciled task descriptions, dependencies, and completion criteria with the 4 corrections. |
| `testing.md` | §2.1, §3.1, §3.2, §3.3, §4, §5 | Added test suites for Named Pipe IPC security, Localhost API security (Host, CSRF, Token), and Non-Chrome app tracking asserting zero window titles. |
| `decisions.md` | ADR-001, ADR-003, ADR-004, ADR-006, ADR-011, ADR-012 | Reconciled existing ADRs and authored ADR-011 (Local API Security) and ADR-012 (Zero Window Titles). |
| `requirements-traceability.md` | Table 1 (REQ-01 to REQ-15), §2 Review | Mapped all 4 reconciled requirements across architectural components, schema, tasks, and test suites. |
| `progress.md` | §1, §2, §3, §4, §5 | Recorded completion of reconciliation phase; confirmed implementation is paused awaiting approval. |

---

## 5. Architecture Decision Records (ADR) Ledger

- **ADR-001 (Superseded & Reconciled):** Interactive User-Session Coordinator Architecture (Session 1+). Supersedes Session 0 Windows Service.
- **ADR-003 (Reconciled):** Dual-Protocol Architecture: Native Messaging Stdio (Extension ↔ Native Host) & Windows Named Pipes IPC (Native Host ↔ Coordinator).
- **ADR-004 (Reconciled):** Multi-Profile Multiplexing & Targeted Named Pipe Connection Routing.
- **ADR-006 (Reconciled):** Local Screenshot Pipeline & Targeted IPC Routing.
- **ADR-011 (New):** Localhost Dashboard & REST API Security Model (Host/Origin validation, CSRF headers, and Capability Bearer Token).
- **ADR-012 (New):** Minimal Non-Chrome Application Tracking (Process Executable & Duration Only; Complete Elimination of Window Titles).

---

## 6. Task Backlog & Dependency Mapping

Key reconciled tasks in `task.md`:
- `TASK-007`: Scaffold `src/Coordinator`, `src/NativeHost`, `src/ChromeExtension`, `src/LocalDashboard`, `tests/LocalActivityTracker.Tests`.
- `TASK-013`: Schema migration executing DDL for 10 tables cleanly omitting `window_title`.
- `TASK-018`: Interactive session foreground hook (`SetWinEventHook`) and session notification listener (`WTSRegisterSessionNotification`).
- `TASK-022`: Windows Named Pipe IPC Server with current-user DACL and client PID verification.
- `TASK-023`: Native Host Stdio-to-NamedPipe bridge with offline buffering.
- `TASK-039`: Non-Chrome application tracking recording process executable name and friendly name only (strictly omitting window titles).
- `TASK-043`: Screenshot scheduler in Coordinator routing targeted requests over the foreground profile's Named Pipe.
- `TASK-049`: Localhost Kestrel API Server enforcing strict Host and Origin header validation.
- `TASK-050`: Local capability bearer token middleware and CSRF header verification (`X-LAT-Requested-With`).

---

## 7. Requirements Traceability Verification

Every prompt requirement and reconciliation mandate maps cleanly across:
- **Architecture:** Interactive Coordinator (Session 1+), Native Host, Named Pipe Server, Hardened Kestrel API.
- **Database Schema:** 10 SQLite tables (`activity.db`, WAL mode), with `window_title` eliminated from `application_sessions`.
- **API Contracts:** Stdio schemas (uint32 LE + JSON max 1 MB), Named Pipe schemas (uint32 LE + JSON max 4 MB), Localhost REST endpoints with Bearer token authentication.
- **Testing:** Automated tests for duration math, boundary clipping, formula injection, Named Pipe DACLs, Host/CSRF/Token security, and zero window titles.

---

## 8. Remaining Unresolved Decisions & Technical Risks

1. **.NET 8 SDK Installation:**
   - *Status:* Verified available in winget (`Microsoft.DotNet.SDK.8` version 8.0.426). Milestone 1 will install it via `winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements`.
2. **Chrome Profile Discovery vs Enrollment:**
   - *Constraint:* Chrome's security model prohibits an extension in Profile A from inspecting Profile B.
   - *Resolution:* Documented honest coverage reporting. The Dashboard will display all detected profiles from `%LOCALAPPDATA%\Google\Chrome\User Data` and visibly flag any unenrolled profiles as unmonitored.

---

## 9. Documentation-Only Verification Results

The following documentation-only verifications were executed and confirmed:
- **File Presence:** All 12 required Markdown files and `documentation-audit.md` verified on disk in workspace root.
- **No Stale Terminology:** Searches for `window_title` confirm it is completely absent from all database schemas, table definitions, ER diagrams, and export specifications.
- **No Stale References:** References to Session 0 or conventional Windows Services have been completely eliminated and replaced with the Interactive User-Session Coordinator.
- **Framing Separation:** Stdio framing and Named Pipe IPC framing are cleanly distinguished with independent message schemas.
- **Security Traceability:** Local API security controls (Host, Origin, CSRF, Token) are cross-referenced across Architecture, Contracts, Privacy, UI, Plan, Tasks, and Tests.

---

## 10. Confirmation of Zero Code Changes

> [!IMPORTANT]
> **CONFIRMATION:**  
> **No application source code, scripts, binaries, migrations, UI components, or executable programs were written, modified, or executed.**  
> The workspace remains in a pristine, documentation-only state.

---

## 11. Final Stop Condition & Next Recommended Action

The planning and documentation reconciliation is complete. In accordance with Phase 3 directives:

**WE STOP HERE.**

Awaiting your explicit authorization to begin **Milestone 1** (Project Scaffolding & Development Environment).
