# Central LAN Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Transform the local NFe Agendamento app into a secure central service reachable by the internal network while keeping the A1 certificate and XML cache on the central PC.

**Architecture:** Keep the Windows tray app and ASP.NET Core web UI. Add explicit local/LAN hosting configuration, local session authentication, and a shared fiscal coordinator that serializes and deduplicates requests before they reach SEFAZ. Default behavior remains loopback-only.

**Tech Stack:** .NET 8 Windows Forms, ASP.NET Core minimal APIs, xUnit, vanilla JavaScript, DPAPI.

**Spec:** `docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md`

## Global Constraints

- Default host remains `http://127.0.0.1:17345`.
- LAN exposure is explicitly enabled by the central PC operator.
- Certificate and private key remain in the Windows Certificate Store.
- XML cache remains encrypted with DPAPI on the central PC.
- Fiscal requests are serialized and duplicate in-flight keys do not create duplicate SEFAZ calls.
- CI never contacts SEFAZ and never uses real certificates or XMLs.

### Task 1: Add explicit host mode and network binding

**Files:**
- Modify: `src/NfeAgendamento.App/LocalHost.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Test: `tests/NfeAgendamento.App.Tests/LocalHostTests.cs`

- [ ] Write tests proving default mode binds only to loopback and LAN mode binds to all interfaces with the configured port.
- [ ] Run the focused tests and confirm they fail for the missing LAN mode.
- [ ] Implement a typed host configuration read from an app-local configuration file or command-line flag, with loopback as the safe default.
- [ ] Keep the port fixed at `17345` and reject invalid bind modes or ports.
- [ ] Run focused tests and confirm they pass.
- [ ] Commit as `feat: add explicit local and lan host modes`.

### Task 2: Add central-PC session authentication

**Files:**
- Create: `src/NfeAgendamento.App/Security/LocalSessionService.cs`
- Modify: `src/NfeAgendamento.App/Security/LocalRequestSecurityMiddleware.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/wwwroot/index.html`
- Modify: `src/NfeAgendamento.App/wwwroot/app.js`
- Test: `tests/NfeAgendamento.App.Tests/LocalRequestSecurityMiddlewareTests.cs`

- [ ] Write tests proving unauthenticated LAN requests cannot call certificate, lookup, batch, XML, or DANFE data endpoints.
- [ ] Write tests proving successful login creates a session cookie and authenticated requests pass CSRF validation.
- [ ] Run the focused tests and confirm the new authentication behavior fails before implementation.
- [ ] Implement a local numeric password setup with a safe first-run flow and a DPAPI-protected password verifier.
- [ ] Use an HttpOnly, SameSite session cookie with expiration and constant-time password verification.
- [ ] Keep loopback requests compatible with the existing local workflow while requiring sessions for LAN clients.
- [ ] Add login/logout UI and make the client send credentials using same-origin requests.
- [ ] Run focused security tests and confirm they pass.
- [ ] Commit as `feat: protect lan access with local sessions`.

### Task 3: Centralize and deduplicate fiscal operations

**Files:**
- Create: `src/NfeAgendamento.App/Fiscal/FiscalRequestCoordinator.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/NfeLookupService.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/BatchLookupService.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Test: `tests/NfeAgendamento.App.Tests/NfeLookupServiceTests.cs`
- Test: `tests/NfeAgendamento.App.Tests/BatchLookupServiceTests.cs`

- [ ] Write a failing test showing concurrent requests for the same key invoke the transport once and return the same result.
- [ ] Write a failing test showing different keys remain serialized through the shared fiscal gate.
- [ ] Run the focused tests and confirm the expected failures.
- [ ] Implement a singleton coordinator keyed by access key, with shared task completion and bounded in-memory entry retention.
- [ ] Reuse the existing persistent cooldown and encrypted cache; do not add a second fiscal state store.
- [ ] Ensure cancellation from one browser request does not cancel a shared operation needed by other callers.
- [ ] Route both single and batch lookup through the coordinator.
- [ ] Run fiscal tests and confirm they pass.
- [ ] Commit as `feat: deduplicate central fiscal requests`.

### Task 4: Make LAN operation discoverable and errors actionable

**Files:**
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/wwwroot/app.js`
- Modify: `src/NfeAgendamento.App/wwwroot/index.html`
- Modify: `README.md`
- Test: `tests/NfeAgendamento.App.Tests/TrayApplicationContextTests.cs`

- [ ] Write tests proving tray actions expose the current access URL and preserve the local-only default.
- [ ] Add a bootstrap response containing mode and access URL without exposing certificate details.
- [ ] Show the central access address in the tray menu and provide a copy/open action.
- [ ] Return `Retry-After` for fiscal cooldown responses and display the server-provided unblock time in the UI.
- [ ] Update README with central-PC setup, LAN client access, firewall scope, and shutdown procedure.
- [ ] Run static JavaScript checks and focused tests.
- [ ] Commit as `docs: document central lan operation`.

### Task 5: Full verification and release readiness

**Files:**
- Modify: `docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md` only if verification reveals a required correction.
- Modify: `README.md` with final tested behavior and version notes.

- [ ] Run `dotnet test Nfe-Agendamento.sln -c Release` on Windows/.NET 8.
- [ ] Run `dotnet build Nfe-Agendamento.sln -c Release` on Windows/.NET 8.
- [ ] Verify loopback mode manually with the central app.
- [ ] Verify LAN mode from a second computer without exposing the certificate or XML cache.
- [ ] Verify two simultaneous requests for the same key produce one fiscal transport call.
- [ ] Verify `656` cooldown survives app restart and returns an actionable `429` response.
- [ ] Confirm CI remains free of real fiscal credentials and external SEFAZ calls.
- [ ] Commit only after all verification commands pass.
