# AI Native Runtime — Windows (WinUI)

The Windows desktop client for AI Native Runtime, built with WinUI 3 / the
Windows App SDK. This is the counterpart to `AI-Native-Runtime-MacOS`; both
sign in a human via Firebase, then register this device against the local
AI Native Runtime Core (CORE) daemon over its local IPC transport (a Windows
named pipe here, a Unix domain socket on macOS), and both conform to
`AI-Runtime-Dev-Docs/Application/desktop-shell-conventions.md` — the
authoritative cross-platform contract this app follows.

**Status (FD-Wave 08, Phase 2 / Checkpoint F8E):** this is the first time
this app has ever been compiled and run. Its own README previously said so
plainly, and that history is preserved here rather than erased: as
committed at the start of Phase 2 (`0d1e154`, "Foundation Wave 06
Completed"), the app crashed during window construction on first launch
(`AuthService`'s constructor threw on a blank `Firebase:ApiKey`, invoked
from `SignInPage`'s own constructor) — fixed in this checkpoint by moving
every service into a `Microsoft.Extensions.Hosting` DI container built once
at app startup (`App.xaml.cs`), so a missing key now renders a
configuration-error banner instead.

## Prerequisites

- Windows 10 19041 (20H1) or later.
- Visual Studio with the **"Desktop development with C++"** workload (for
  the Windows SDK toolchain the Windows App SDK's own build steps need) and
  the **Windows App SDK** / WinUI project templates, or the .NET 8 SDK plus
  the Windows App SDK NuGet packages this `.csproj` already references.
  **Found the hard way this checkpoint:** a Visual Studio install can have
  the Windows 11 SDK *and* an MSVC toolset present and still fail to link
  anything, if the MSVC toolset installed is the OneCore-only slice rather
  than the full desktop one (no `VC\Tools\MSVC\<ver>\lib\x64`, no
  `vcvarsall.bat`) — the fix is the *whole* "Desktop development with C++"
  workload checkbox in the VS Installer, not individual components.
- A Rust toolchain (CORE builds and is verified on Windows too, D-36).
- The AI Native Runtime Core daemon (`ainativeruntime_node`, from
  `AI-Native-Runtime-Rust`) — either running by hand for development, or
  installed as the per-user background service (see below).
- A Firebase project whose Web API key is authorized against this
  deployment's RTAPI/CORE trust chain, with email/password sign-in enabled.

## Configuration

Before signing in, set the Firebase Web API key:

1. Open `AI-Native-Runtime-Windows/appsettings.json`.
2. Set `Firebase.ApiKey` to your Firebase project's Web API key (Firebase
   console → Project settings → General → "Web API Key"). This is a public,
   client-side identifier (not a secret), but it is still
   environment-specific and must not be hardcoded in source.

If it is left blank, the app **launches without throwing** and shows a
"Not configured" banner on the sign-in screen instead — the plan §4.5 fix.

`Runtime.ApplicationId` (`app_desktop_windows`) and `Runtime.PipeName`
(`ainativeruntime-runtime`) are fixed to match CORE's own configuration
(`api/applications.rs::FIRST_PARTY_APPLICATION_IDS`, `main.rs`'s named-pipe
listener) — not meant to be changed per-deployment.

## Building and running

Open `AI-Native-Runtime-Windows.slnx` in Visual Studio and run the
`AI-Native-Runtime-Windows` project (F5), or from a Developer PowerShell:

```powershell
dotnet build "AI-Native-Runtime-Windows\AI-Native-Runtime-Windows.csproj" -p:Platform=x64
dotnet run   --project "AI-Native-Runtime-Windows\AI-Native-Runtime-Windows.csproj"
```

Both **packaged** (via the sibling `AI-Native-Runtime-Windows (Package)`
`.wapproj`, MSIX) and **unpackaged** (plain `dotnet run`/`dotnet publish`)
modes are supported — the app never calls a packaged-only API
(`Windows.Storage.ApplicationData.Current`, for instance; `CursorStore` and
the background-service scripts deliberately use plain files under
`%LOCALAPPDATA%` instead, specifically so unpackaged mode keeps working).
No UAC elevation is required to run the app itself (`app.manifest` requests
no elevated execution level); the per-user background service install
script below also runs unelevated, registering a task scoped to the current
user only (System Guide §105.5's session boundary: this app and the daemon
it talks to both run as the same signed-in user, never SYSTEM).

## Running the tests

```powershell
dotnet test "AI-Native-Runtime-Windows.Tests\AI-Native-Runtime-Windows.Tests.csproj" -p:Platform=x64
```

Covers `CursorStore` (real file round-trips), `RuntimeAppError`'s code
mapping (exhaustively, every documented code), the protocol model parsers
(`ExecutionItem`/`ApprovalItem`/`CapabilityItem.FromJson`, against literal
JSON shaped like CORE's real wire output), `CredentialStore` (real Windows
Credential Manager round-trips, mirroring MAC's `KeychainStoreTests`
precedent), `BackgroundServiceController`'s `schtasks` output parsing, and
a launch smoke test (starts the real built `.exe`, asserts it is still
running after two seconds — the direct regression test for the
constructor-throw bug above). The launch test does **not** drive the UI
itself; that needs WinAppDriver/Appium, not installed in this environment —
the same limitation MAC's own UI launch test recorded for its own
environment, stated here rather than silently assumed passing.

## Sign-in

The sign-in screen (`Views/SignInPage.xaml`) takes an email and password —
this deployment's product (SUBAPI) only supports email/password sign-in.
On submit (`Views/SignInPage.xaml.cs`):

1. `Services/AuthService.cs` signs in against the Firebase Auth REST API
   directly, storing the resulting ID/refresh token pair.
2. `Services/RuntimeService.cs` opens the RPC connection to CORE over its
   named pipe, ensures this installation has its own Ed25519 keypair,
   binds it to the `app_desktop_windows` application id, establishes a
   session, and calls CORE's `device.register` RPC with a freshly
   refreshed Firebase ID token.
3. The event-stream connection is started only now, once a session exists
   (`events.subscribe` requires one — `desktop-shell-conventions.md` §4.6).

If a Firebase refresh token can be restored from a previous launch
(`AuthService.TryRestoreSession`), the app skips straight to step 2-3
rather than asking for a password again — but a CORE session token is
**never** persisted, so steps 2-3 still run fresh on every launch
regardless (`RuntimeService`'s own doc comment).

Four distinguishable failure states are shown, per §43:

- **Runtime unavailable** — CORE's named pipe could not be reached at all.
- **Substrate offline** — Firebase, or (relayed through CORE) RTAPI, could
  not be reached.
- **Registration rejected** — the substrate was reached and affirmatively
  refused the request.
- **Sign-in failed** — an ordinary wrong password (not one of the three
  above; needs re-entered credentials, not a service restart).
- **Not configured** — `Firebase:ApiKey` is blank (plan §4.5).

## The nine surfaces

Home (status + emergency controls), Activity, Approvals, Applications,
Permissions, Capabilities, Security, Logs, and Settings (the
`NavigationView`'s own built-in item) — `Navigation/ShellPage.xaml`.
Applications, Permissions, and Security render an honest "not yet
available" state (`Shared/NotYetAvailablePage`): no CORE surface backs them
on **either** platform yet (plan §4.18), so this is not a Windows-specific
shortfall.

Every RPC-backed surface follows the loading/empty/error/access-denied
state pattern (`Shared/SurfaceViewModelBase.cs`,
`Shared/BoolToVisibilityConverter.cs`) built once, here, rather than
reinvented per screen — `desktop-shell-conventions.md` §6.

## Emergency controls and the close/quit/stop distinction

Home's Pause/Resume/Disconnect Cloud/Connect Cloud/Stop Runtime buttons
(and the identical set in the system-tray context menu,
`Navigation/ShellPage.xaml`) are thin callers of F8A's real CORE
operations (`runtime.pause`/`resume`/`stop`, `cloud.disconnect`/`connect`)
— never a client-side simulation. §35's close/quit/stop distinction:

- **Closing the window** leaves the app (via the tray icon) and the daemon
  running.
- **Quitting the app** (tray menu → Quit) leaves the daemon running — it is
  a wholly separate per-user background process, never a child process
  this app spawns.
- **Only "Stop Runtime"** actually stops the daemon, via the real
  `runtime.stop` RPC.

## The per-user background service (ADR-0014, Windows's own mechanism)

There is no Windows Service here, by ADR-0014's own deliberate choice
(deferred; a per-user process, mirroring — not copying — MAC's LaunchAgent
decision). `Scripts/install-background-service.ps1` registers a per-user
Scheduled Task (`AtLogOn` trigger) that runs
`Scripts/run-daemon-supervised.cmd` — a small wrapper loop that restarts
the daemon only on a **non-zero (crash) exit**, never on a clean `exit(0)`,
mirroring MAC's own `KeepAlive = { SuccessfulExit = false }` LaunchAgent
decision and the reasoning behind it (Phase 1 report §4.3: a deliberate
`runtime.stop` must not be immediately relaunched by whatever supervises
the process). Windows Task Scheduler has no native "restart on crash only"
primitive the way launchd's `KeepAlive` does, so the wrapper script
supplies it directly.

```powershell
# One-time, per machine/user:
.\Scripts\install-background-service.ps1
# To remove it:
.\Scripts\uninstall-background-service.ps1
```

**Stated plainly, the same limitation ADR-0014 records for macOS:** this
requires a signed-in user session — it does not run before anyone logs in,
and does not survive a full logout with nobody signed in. It *does* start
again automatically at the user's next logon, which is the property that
actually matters for "does the daemon come back."

## Token storage rule

**All secrets — the Firebase ID/refresh token pair and the application's
own Ed25519 installation private key — are stored exclusively in Windows
Credential Manager**, via P/Invoke against `advapi32.dll`
(`Services/CredentialStore.cs`). The event-stream cursor (a sequence number
and stream id — not a secret) deliberately does **not** go through
Credential Manager; it is a plain JSON file under `%LOCALAPPDATA%`
(`Services/Transport/CursorStore.cs`), mirroring MAC's choice of
`UserDefaults` over Keychain for the same non-secret data.

Never store a secret in `ApplicationData.Current.LocalSettings`, a plain
file, or any log/diagnostic output — master §6.4's "no credential, token,
private key, signature, or nonce in any log at any level" applies to this
app's own logs too, not only CORE's (the Logs surface renders
`runtime.logs`'s already-redacted output verbatim and adds no client-side
logging of request payloads).

Every Credential Manager entry is target-named
`com.ainativeruntime.runtime.windows/<key>`, deliberately distinct from
CORE's own `com.ainativeruntime.runtime/<key>` convention — this process
and the CORE daemon hold different keypairs for different principals and
must never collide in the same namespace, even though both run as the same
signed-in Windows user.

## Architecture notes

- `Services/Transport/RpcConnection.cs` /
  `Services/Transport/EventStreamConnection.cs` — the two-connection model
  (`desktop-shell-conventions.md` §2): one connection for ordinary RPC
  calls, demultiplexed by `requestId`, supporting concurrent in-flight
  requests; a wholly separate connection for `events.subscribe`, since
  CORE's own connection loop switches a subscribed connection into
  streaming-only mode for the rest of its life. Both reconnect
  independently with exponential backoff (0.5s doubling, capped at 8s, §3).
- `Services/Transport/RuntimeAppError.cs` — the one error-code-to-
  presentation mapping every surface reads (§5).
- `Services/RuntimeService.cs` — the installation ceremony
  (`application.bind` → `session.challenge` → `session.establish` →
  `device.register`) and every RPC method call this app makes. Generates
  and holds this **application's own** installation Ed25519 keypair via
  [NSec.Cryptography](https://github.com/ektrah/nsec) (net8.0 has no
  cross-platform Ed25519 API); never touches the **node's** keypair, which
  CORE generates and holds entirely on the Rust side.
- `Navigation/ShellPage.xaml(.cs)` — the `NavigationView` shell and the
  system-tray presence ([H.NotifyIcon.WinUI](https://github.com/HavenDV/H.NotifyIcon),
  since WinUI 3 has no first-party tray-icon API).
- `Views/*` / `ViewModels/*` — the nine surfaces and their view models
  (`CommunityToolkit.Mvvm`'s `ObservableObject`/`[RelayCommand]`).
- `Shared/SurfaceViewModelBase.cs` — the loading/empty/error/access-denied
  state pattern, built once.

## What Phase 1 (macOS) established that this app follows, not reinvents

`AI-Runtime-Dev-Docs/Application/desktop-shell-conventions.md`, authored in
Phase 1, is the authoritative cross-platform contract — read it before
changing the transport, error mapping, or state patterns here. Where this
platform genuinely differs from macOS (Credential Manager vs. Keychain, a
Scheduled Task vs. a LaunchAgent, a named pipe vs. a Unix domain socket),
that is a recorded, deliberate difference, not an accident.
