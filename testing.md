# Testing & Verification Specification — Local Activity Tracker

> **Test Strategy, Automated Test Suites, Windows Lifecycle Testing, and Acceptance Test Plans**  
> *Target Test Frameworks: xUnit / FluentAssertions (.NET 8) \| Vitest (TypeScript) \| Interactive Session Verification*

---

## 1. Testing Philosophy & Hierarchy

In strict alignment with the **Gravitas Reliability Runtime**:
- **Measured, Not Claimed:** A test passes only when executed and observed with exit code 0.
- **Evidence-Backed:** Verifications produce machine-readable outputs, pass counts, and coverage logs.
- **Deterministic Assertions:** Zero reliance on flaky timeouts or race-prone delays.

```
                    +-----------------------------+
                    |  Manual E2E Acceptance      |  (Chrome Multi-Profile, Excel Import, No Titles)
                    +-----------------------------+
                    |  Integration & Security     |  (Named Pipe IPC, Host/Origin Check, CSRF, Token)
                    +-----------------------------+
                    |  Unit & Algorithmic Tests   |  (Duration Math, Boundary Clipping, Formula Defense)
                    +-----------------------------+
```

---

## 2. Automated Test Suites & Coverage Scope

### 2.1 C# / .NET 8 Test Project (`tests/LocalActivityTracker.Tests`)

| Test Suite | File | Focus Areas |
| :--- | :--- | :--- |
| **Duration Calculation Tests** | `DurationCalculatorTests.cs` | Active URL timing, focus interruption, pause/resume intervals, non-overlapping guarantees. |
| **Date Boundary Clipping Tests** | `DateBoundaryClippingTests.cs` | Slicing sessions starting before range, ending after range, or spanning range. |
| **CSV Formatter & Security Tests**| `CsvExportServiceTests.cs` | RFC 4180 escaping, UTF-8 BOM, formula injection neutralization (`=`, `+`, `-`, `@`), zero window titles. |
| **Named Pipe IPC Security Tests**| `NamedPipeIpcTests.cs` | Length framing, user SID access control, client PID verification, reconnect buffering. |
| **Localhost API Security Tests** | `LocalApiSecurityTests.cs` | Host header validation, Origin validation, CSRF `X-LAT-Requested-With` header, Capability Bearer Token. |
| **Non-Chrome App Tracking Tests** | `ApplicationTrackerTests.cs` | Executable and display name recording, **assertion of zero window titles stored or queried**. |
| **SQLite Persistence Tests** | `SqliteRepositoryTests.cs` | In-memory / file SQLite CRUD, WAL mode, foreign keys, migrations, concurrent access. |
| **Native Messaging Framer Tests**| `NativeMessageStreamTests.cs`| uint32 LE length framing, 1 MB bounds, partial stream recovery, invalid payload handling. |
| **Crash Recovery Tests** | `CrashRecoveryServiceTests.cs`| Dangling session repair, heartbeat reconciliation, `RECOVERED_AFTER_CRASH` flag. |

### 2.2 TypeScript / Chrome Extension Test Suite (`tests/ChromeExtension.Tests`)

| Test Suite | File | Focus Areas |
| :--- | :--- | :--- |
| **URL Sanitizer Tests** | `sanitizer.test.ts` | Stripping token, auth, key, password parameters, removing fragments, preserving clean paths. |
| **Profile Manager Tests** | `profile.test.ts` | UUID generation, storage caching, display name updates. |
| **Message Dispatcher Tests** | `dispatcher.test.ts` | Native messaging frame serialization and tab event formatting. |

---

## 3. Critical Algorithmic & Security Test Cases

### 3.1 Localhost API Security Tests (`LocalApiSecurityTests.cs`)

```csharp
[Fact]
public async Task Request_WithUnapprovedHostHeader_ShouldReturn400BadRequest()
{
    // Arrange: Simulate DNS rebinding attack
    var client = _factory.CreateClient();
    var request = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
    request.Headers.Host = "attacker.domain.com";

    // Act
    var response = await client.SendAsync(request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task StateChangingRequest_WithoutCsrfHeader_ShouldReturn403Forbidden()
{
    // Arrange: Missing X-LAT-Requested-With header
    var client = _factory.CreateClient();
    var content = new StringContent("{\"start_date\":\"2026-10-01\"}", Encoding.UTF8, "application/json");

    // Act
    var response = await client.PostAsync("/api/export/csv", content);

    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task SensitiveEndpoint_WithoutBearerToken_ShouldReturn401Unauthorized()
{
    // Arrange: Valid Host and CSRF header, but missing Bearer Token
    var client = _factory.CreateClient();
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/tracking/pause");
    request.Headers.Add("X-LAT-Requested-With", "LocalActivityTracker");

    // Act
    var response = await client.SendAsync(request);

    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

### 3.2 Non-Chrome Privacy Test (`ApplicationTrackerTests.cs`)

```csharp
[Fact]
public async Task TrackNonChromeApp_ShouldRecordExecutableAndDuration_AndZeroWindowTitles()
{
    // Arrange: Focus switch from Chrome to Notepad
    var arbiter = new ActivityArbiter(_mockRepo.Object, _mockTime.Object);
    
    // Act: Simulate Win32 focus switch to Notepad
    arbiter.OnForegroundWindowChanged(hWndNotepad, "notepad.exe", "Notepad");

    // Assert: Verify stored session has executable name and NO window title
    _mockRepo.Verify(r => r.OpenApplicationSessionAsync(
        It.Is<ApplicationSession>(s => 
            s.ProcessName == "notepad.exe" && 
            s.ApplicationDisplayName == "Notepad"
        )), Times.Once);

    // Assert: Inspect database table schema to verify window_title column does NOT exist
    var columns = await _dbContext.GetTableColumnsAsync("application_sessions");
    Assert.DoesNotContain("window_title", columns);
}
```

### 3.3 Named Pipe Access Control Test (`NamedPipeIpcTests.cs`)

```csharp
[Fact]
public async Task NamedPipeServer_ShouldRejectClientsFromDifferentUserSID()
{
    // Arrange: Server configured with current-user DACL
    var server = new NamedPipeIpcServer("test-pipe", currentSessionId);
    await server.StartAsync();

    // Act & Assert: Connection from unauthorized process token is denied by OS
    // and GetNamedPipeClientProcessId validation rejects mismatched SIDs
}
```

### 3.4 Date Boundary Duration Clipping Test (`DateBoundaryClippingTests.cs`)

**Scenario:** User requests report for `2026-10-10 00:00:00` to `2026-10-10 23:59:59` (Total range: 86,400s).
- **Session A (Fully Inside):** Actual: `10:00:00` to `10:30:00` (1,800s) -> Clipped: `1,800s`, `COMPLETED`.
- **Session B (Spanning Start Boundary):** Actual: `2026-10-09 23:30:00` to `2026-10-10 01:00:00` (5,400s) -> Clipped: `3,600s`, `CLIPPED_BOUNDARY`.
- **Session C (Spanning End Boundary):** Actual: `2026-10-10 23:00:00` to `2026-10-11 01:00:00` (7,200s) -> Clipped: `3,599s`, `CLIPPED_BOUNDARY`.
- **Session D (Spanning Entire Range):** Actual: `2026-10-09 12:00:00` to `2026-10-11 12:00:00` (172,800s) -> Clipped: `86,400s`, `CLIPPED_BOUNDARY`.

### 3.5 CSV Formula Injection Defense Test (`CsvExportServiceTests.cs`)

```csharp
[Theory]
[InlineData("=1+1", "'=1+1")]
[InlineData("+cmd|'/c calc'!A0", "'+cmd|'/c calc'!A0")]
[InlineData("-2+3", "'-2+3")]
[InlineData("@SUM(A1:A10)", "'@SUM(A1:A10)")]
[InlineData("\tmalicious_tab", "'\tmalicious_tab")]
[InlineData("\rmalicious_cr", "'\rmalicious_cr")]
[InlineData("Normal Title", "Normal Title")]
public void FormatCsvField_ShouldNeutralizeFormulasAndEscape(string input, string expected)
{
    var result = CsvFormatter.SanitizeAndEscape(input);
    Assert.Equal(expected, result);
}
```

---

## 4. Windows Lifecycle & Error Recovery Test Matrix

| Lifecycle Event | Simulated Trigger | Verification Assertion |
| :--- | :--- | :--- |
| **Interactive Session Lock** | Dispatch `WTS_SESSION_LOCK` | Open sessions receive `end_time_utc = Now`; timers stopped. Zero time accrued during lock. |
| **Interactive Session Unlock** | Dispatch `WTS_SESSION_UNLOCK` | Focus re-evaluated; fresh session opened only if Chrome/App focused. |
| **System Sleep / Suspend** | Dispatch `PowerModes.Suspend` event | Open sessions receive `end_time_utc = Now`; zero time accrued during sleep. |
| **Abrupt Coordinator Kill (`taskkill /F`)** | Force kill process while browsing | Upon restart, `CrashRecoveryService` closes orphaned sessions with `last_heartbeat_utc` and sets `session_status = 'RECOVERED_AFTER_CRASH'`. |
| **Native Host Disconnect** | Force kill `LocalActivityTracker.NativeHost.exe` | Coordinator detects pipe EOF; profile connection updated to `DISCONNECTED`; active sessions cleanly closed. |

---

## 5. End-to-End Manual Acceptance Verification Checklist

- [ ] **Test Case E2E-01: Multi-Profile Concurrent Isolation over Named Pipes**
  - *Steps:* Enroll extension in Chrome `Default` and `Profile 1`. Open different websites in each profile simultaneously.
  - *Assertion:* SQLite `url_activity_sessions` records discrete profile IDs for each URL without mixing.
- [ ] **Test Case E2E-02: Non-Chrome Application Tracking (Zero Window Titles)**
  - *Steps:* Open Chrome, switch to Excel for 5 minutes, switch to VS Code for 5 minutes, return to Chrome.
  - *Assertion:* SQLite `application_sessions` contains rows for `EXCEL.EXE` and `Code.exe` with exact durations. Inspect database to confirm `window_title` is not stored.
- [ ] **Test Case E2E-03: Periodic Local Tab Screenshot & Routing**
  - *Steps:* Set screenshot interval to 1 minute in Settings. Browse GitHub for 3 minutes.
  - *Assertion:* 3 PNG files created in `%LOCALAPPDATA%\LocalActivityTracker\screenshots\`. Gallery renders thumbnails. No images in text logs.
- [ ] **Test Case E2E-04: Restricted Screenshot Handling**
  - *Steps:* Navigate active tab to `chrome://extensions/`. Await capture interval.
  - *Assertion:* No desktop capture occurs. SQLite records `status = 'RESTRICTED'` with reason `Cannot capture chrome:// URLs`.
- [ ] **Test Case E2E-05: Unified Multi-Profile CSV Export & Excel Verification**
  - *Steps:* Open Dashboard CSV Export modal. Select 14-day date range. Click `Generate Master CSV`.
  - *Assertion:* Single CSV file generated with all 5 sections. Section 5 has NO window titles. Open file in Microsoft Excel: columns align cleanly, non-ASCII characters render properly, formulas do not execute.
- [ ] **Test Case E2E-06: Localhost API Security Verification**
  - *Steps:* Attempt `curl` to `http://127.0.0.1:48120/api/overview` without Bearer token.
  - *Assertion:* Returns 401 Unauthorized. With valid token and custom header, returns 200 OK.
