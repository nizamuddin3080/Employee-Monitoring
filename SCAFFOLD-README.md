# Local Activity Tracker — Milestone 1 Scaffold

This package contains **scaffolding only**. It does not collect data, capture screenshots, expose an API, install a Chrome extension, or configure persistence.

## Architecture baseline
- `src/Coordinator`: future interactive per-user Windows session process (not a Session 0 service).
- `src/NativeHost`: future per-connection Chrome Native Messaging bridge.
- `src/Core`: shared C# contracts and domain types (future).
- `src/ChromeExtension`: inert MV3 extension workspace.
- `src/LocalDashboard`: placeholder React 18 / TypeScript / Tailwind app.
- `tests`: test project placeholder, not yet a runnable xUnit suite.

## Windows prerequisites and build
Install .NET 8 SDK, Node.js LTS, npm, PowerShell, and optionally Git. Verify `dotnet --version`, `node --version`, `npm --version`.

From PowerShell in the project root:
```powershell
.\build.ps1
```

To remove generated build outputs:
```powershell
.\clean.ps1
```

Chrome extension output should include `dist/manifest.json` and `dist/background.js`. Build scripts need network access to download npm dependencies on first use. **No Windows build was performed in the authoring environment.**

## Review before further milestones
The supplied planning documents conflict with the reconciliation report: some still describe a Session 0-style Worker Service and window-title capture. This scaffold follows the **reconciliation report** only for process boundaries and privacy. Update and approve canonical documentation before implementing Milestone 2 or later.
