# UI / UX Specification — Local Activity Tracker

> **Local Dashboard Design, Layouts, Interactive Workflows, and Accessibility Standards**  
> *Framework: React 18 + TypeScript + Tailwind CSS \| Local Offline Web App (`127.0.0.1:48120`)*

---

## 1. Visual Design Foundation & Principles

- **Design Philosophy:** Clean, professional, high-density desktop utility optimized for readability, information hierarchy, and immediate situational awareness.
- **Color Palette (Dark & Light Theme Support):**
  - **Background Base:** Slate-950 (`#020617`) / White (`#ffffff`)
  - **Surface Elevated:** Slate-900 (`#0f172a`) / Slate-50 (`#f8fafc`)
  - **Card / Border:** Slate-800 (`#1e293b`) / Slate-200 (`#e2e8f0`)
  - **Primary Brand / Action:** Indigo-600 (`#4f46e5`) / Indigo-500 (`#6366f1`)
  - **Success / Connected:** Emerald-500 (`#10b981`)
  - **Warning / Partial:** Amber-500 (`#f59e0b`)
  - **Destructive / Error:** Rose-500 (`#f43f5e`)
  - **Text Primary:** Slate-50 (`#f8fafc`) / Slate-900 (`#0f172a`)
  - **Text Muted:** Slate-400 (`#94a3b8`) / Slate-500 (`#64748b`)
- **Typography:** System font stack (`Segoe UI`, `-apple-system`, `BlinkMacSystemFont`, `Roboto`, `sans-serif`) for native Windows integration without external Google Fonts.

---

## 2. Navigation Architecture & Global Layout

The local dashboard features a persistent left navigation sidebar and a top contextual utility bar:

```
+---------------------------------------------------------------------------------------+
|  LOGO  Local Activity Tracker       [● TRACKING ACTIVE]  [Pause]  [Export CSV]  2026-10-22|
+--------------+------------------------------------------------------------------------+
| 📊 Overview  |                                                                        |
| 🌐 Chrome    |  MAIN CONTENT VIEWPORT                                                 |
| 🖼️ Gallery   |                                                                        |
| 💻 Apps      |  (Dynamically rendered based on active route)                          |
| 👥 Profiles  |                                                                        |
| ⚙️ Settings  |                                                                        |
+--------------+------------------------------------------------------------------------+
| %LOCALAPPDATA%\LocalActivityTracker\activity.db                     v1.0.0 (Offline)  |
+---------------------------------------------------------------------------------------+
```

### 2.1 Security Integration in Dashboard UI
- **Local Capability Token:** During dashboard initialization, the React client loads its local capability bearer token and attaches `Authorization: Bearer <LAT_CAPABILITY_TOKEN>` to all REST queries.
- **CSRF Defense:** All mutation queries (`POST`, `PUT`, `DELETE`) automatically attach the `X-LAT-Requested-With: LocalActivityTracker` header via an Axios/Fetch interceptor.
- **Host/Origin Safeguards:** The dashboard functions exclusively when accessed from `http://127.0.0.1:48120` or `http://localhost:48120`.

---

## 3. Screen Specifications

### 3.1 Overview Screen
The primary landing dashboard displaying high-level computing metrics for the selected day.

- **Status Banner:**
  - Dynamic pill indicator: `● TRACKING ACTIVE` (green) or `⏸ TRACKING PAUSED` (amber).
  - Quick action buttons: `Pause / Resume Tracking`, `Instant CSV Export`.
- **KPI Metric Grid (4 Cards):**
  1. **Chrome Foreground Time:** Total time Google Chrome was the focused Windows application (e.g., `4h 32m`).
  2. **Active URL Viewing Time:** Total time a specific web tab was selected and actively in foreground (e.g., `3h 48m`).
  3. **Non-Chrome Application Time:** Cumulative time in tools like VS Code, Excel, Slack (e.g., `2h 15m`).
  4. **Screenshots & Health:** Successful captures vs failed attempts (e.g., `24 / 24 Captures (100%)`).
- **Today's Profile Breakdown Table:**
  - Columns: Profile Name, Active URL Time, Chrome Foreground Time, Visits, Last Event.
- **Recent URL Activity Stream:**
  - Micro-feed of the last 10 visited URLs with domain favicon placeholder, page title, and duration badge.

---

### 3.2 Chrome Activity Screen
Deep-dive chronological view and search tool for web browsing history.

- **Filter Bar:**
  - Date Picker (Defaults to Today; supports Custom Date Range).
  - Profile Dropdown (All Profiles, or filter to specific profile).
  - Domain Search Input (Instant filtering on hostname).
  - Minimum Duration Slider (Filter out brief redirects < 3s).
- **Activity Table:**
  - Columns:
    - Time (Start – End in local 12h/24h format).
    - Profile Badge (e.g. `[Default]`, `[Work]`).
    - Domain (e.g. `github.com`).
    - Page Title (Clickable to open URL details modal).
    - URL (Monospace font, truncated with hover tooltip).
    - Duration (Formatted: `12m 45s`).
    - Status (`COMPLETED`, `CLIPPED`, `ACTIVE`).
- **URL Details Modal:**
  - Shows full un-truncated URL, full page title, timestamp interval, and exact millisecond duration.

---

### 3.3 Screenshot Gallery Screen
Visual audit tool for captured Chrome tab screenshots.

- **Controls Bar:**
  - Date filter, Profile filter, and Capture Status filter (`All`, `Successful Only`, `Restricted / Failed`).
  - Refresh button.
- **Thumbnail Grid:**
  - Responsive cards with 16:9 aspect ratio thumbnails.
  - Card Footer: Profile tag, Capture timestamp (`14:20`), Domain (`github.com`).
  - Status Badge: `Success` (green), `Restricted URL` (amber), `Occluded` (gray), `Timeout` (rose).
- **Image Lightbox / Modal:**
  - Clicking any thumbnail opens high-resolution preview.
  - Metadata Sidebar: Associated URL, Profile Name, Capture Timestamp, Exact File Path.
  - Action Buttons:
    - `Open File in Windows Explorer` (Invokes local shell reveal).
    - `Delete Screenshot` (Prompts confirmation, deletes file and database row).

---

### 3.4 Non-Chrome Application Usage Screen (Duration Only — NO Window Titles)
Tracks foreground time for Windows desktop applications (Visual Studio Code, Excel, Word, Teams, etc.).

- **Privacy Disclosure Banner:**
  - > *Notice: Local Activity Tracker only records application executable names and foreground duration. Window titles, document names, spreadsheets, emails, typed text, and files are strictly never inspected or collected.*
- **Daily App Distribution Chart:**
  - Horizontal bar chart showing top applications sorted by foreground duration.
- **Application Session Table:**
  - Columns:
    - Application Display Name (e.g., `Visual Studio Code`, `Microsoft Excel`).
    - Process Executable Name (`Code.exe`, `EXCEL.EXE`).
    - Local Date (`2026-10-22`).
    - Start Time (`09:15:00`).
    - End Time (`10:30:00`).
    - Total Foreground Duration (`1h 15m 00s`).
    - Status (`COMPLETED`, `CLIPPED_BOUNDARY`).
  - *(Notice: Window titles, document names, or captions are strictly absent from this view).*

---

### 3.5 Profiles & Health Screen
Status, enrollment, and health monitoring for all Chrome profiles.

- **Enrolled Profile Cards:**
  - Profile ID, Friendly Display Name (editable inline).
  - Status Indicator: `CONNECTED` (green), `DISCONNECTED` (gray).
  - Last Event Timestamp and Last Successful Screenshot Timestamp.
- **Enrollment Wizard & Missing Coverage Detection:**
  - Displays detected Chrome profiles located in `%LOCALAPPDATA%\Google\Chrome\User Data\`.
  - Prominently flags any profiles that exist on the machine but do not have the extension installed (`MISSING COVERAGE`).
  - Clear step-by-step instructions to load the unpacked extension in each profile.

---

### 3.6 Settings & Maintenance Screen
- **Tracking Configuration:**
  - Screenshot Interval Slider: 1 to 60 minutes (Default: 10 minutes).
  - Start with Windows Toggle (Manages Windows Startup shortcut in interactive user session).
  - Query Parameter Redaction Toggle (Default: Enabled).
- **Storage & Retention:**
  - Data Retention Dropdown: 30 days, 60 days, 90 days, 1 year, Indefinite.
  - Storage Paths: Displays local folder paths for Database, Screenshots, and Exports with "Open Folder" shortcuts.
- **Maintenance Actions:**
  - `Create Database Backup` (Triggers immediate SQLite backup).
  - `Purge Old Records` (Deletes data older than retention window).
  - `Purge All Local Data` (Destructive action with confirmation dialog).

---

### 3.7 Master CSV Export Modal (Custom Date Range)
The dedicated export interface for generating unified reports.

- **Modal Trigger:** Always accessible via the persistent header `[Export CSV]` button or the navigation menu.
- **Form Controls:**
  - **Start Date:** Date picker (Defaults to first day of current month).
  - **End Date:** Date picker (Defaults to today).
  - **Profile Selection:** Checkbox list: `[✓] All Enrolled Profiles` or individual profile checkboxes.
  - **Section Inclusions:**
    - `[✓] Report Metadata & System Header`
    - `[✓] Profile Summary Table`
    - `[✓] Chrome Profile URL Activity Sessions`
    - `[✓] Screenshot Metadata & Local Paths`
    - `[✓] Non-Chrome Application Usage Sessions (Duration & Executable Only)`
- **Action Button:** `[Generate Master CSV]`
- **Progress State:**
  - Progress bar showing query execution and record streaming.
- **Completion State:**
  - Success message: *"Export generated successfully!"*
  - Summary badges: `X Profiles`, `Y URL Sessions`, `Z Screenshots`.
  - Full Output Path displayed: `%LOCALAPPDATA%\LocalActivityTracker\exports\activity_report_2026-10-01_to_2026-10-22.csv`.
  - Action Buttons: `[Open in File Explorer]`, `[Close]`.

---

## 4. UI State Specifications

1. **Empty States:**
   - When no activity exists for the selected date: Render an illustrative empty slate graphic with message *"No activity recorded for this date. Ensure Chrome is running and the extension is connected."*
2. **Loading States:**
   - Skeleton shimmer cards for KPI metrics and table rows while querying SQLite.
3. **Error States:**
   - Toast notifications and inline banners for Coordinator disconnects: *"Unable to communicate with local Interactive Coordinator. Check if LocalActivityTracker.Coordinator.exe is running in your Windows session."*
4. **Keyboard Accessibility:**
   - Full keyboard navigation (`Tab`, `Shift+Tab`, `Enter`, `Escape`).
   - Modal dialogs trap focus and close on `Escape`.
   - Table rows support arrow-key navigation.
