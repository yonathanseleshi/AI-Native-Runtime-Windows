# AI Native Runtime — Windows (WinUI)

The Windows desktop client for AI Native Runtime, built with WinUI 3 / the
Windows App SDK. This is the counterpart to `AI-Native-Runtime-MacOS`; both
sign in a human via Firebase, then register this device against the local
AI Native Runtime Core (CORE) daemon over its local IPC transport (a Windows
named pipe here, a Unix domain socket on macOS).

## Prerequisites

- Windows 10 19041 (20H1) or later.
- Visual Studio 2022 with the "Windows App SDK C# templates" / ".NET
  Multi-platform App UI development" workload, or the .NET 8 SDK plus the
  Windows App SDK.
- The AI Native Runtime Core daemon (`ainativeruntime_node`, from
  `ai_native_runtime_rust`) installed and running locally — this app cannot
  do anything useful without it. If the Core is not running you will see a
  "Runtime unavailable" error on sign-in; that is CORE's absence, not a bug
  in this app.
- A Firebase project whose Web API key is authorized against this
  deployment's RTAPI/CORE trust chain, with email/password sign-in enabled.

## Configuration

Before signing in, set the Firebase Web API key:

1. Open `AI-Native-Runtime-Windows/appsettings.json`.
2. Set `Firebase.ApiKey` to your Firebase project's Web API key (Firebase
   console → Project settings → General → "Web API Key"). This is a public,
   client-side identifier (not a secret), but it is still
   environment-specific and must not be hardcoded in source — that is why
   it lives in `appsettings.json` instead of `AuthService.cs`.

`Runtime.ApplicationId` (`app_desktop_windows`) and `Runtime.PipeName`
(`ainativeruntime-runtime`) are also read from `appsettings.json`, but are
fixed to match CORE's own configuration
(`api/applications.rs::FIRST_PARTY_APPLICATION_IDS`, `main.rs`'s named-pipe
listener) — they are not meant to be changed per-deployment.

There is no separate "RTAPI base URL" setting on this side: this app never
calls RTAPI directly. It only talks to Firebase (directly) and to CORE
(over the local named pipe); CORE is the one that holds
`RUNTIME_CLOUD_API_BASE_URL` and relays `application.bind`/`device.register`
to RTAPI on this app's behalf. If RTAPI is unreachable from CORE's side,
this app surfaces that as the "Substrate offline" error, the same as if
Firebase itself were unreachable — from the human's point of view both mean
"the cloud isn't answering right now," even though the failure originates
on different legs.

## Running

Open `AI-Native-Runtime-Windows.slnx` in Visual Studio and run the
`AI-Native-Runtime-Windows` project (F5), or from a Developer PowerShell:

```powershell
dotnet run --project "AI-Native-Runtime-Windows\AI-Native-Runtime-Windows.csproj"
```

## Signing in

The sign-in screen (`Views/SignInPage.xaml`) takes an email and password —
this deployment's product (SUBAPI) only supports email/password sign-in, so
there is no Google/social sign-in button here. On submit:

1. `Services/AuthService.cs` signs in against the Firebase Auth REST API
   directly (`identitytoolkit.googleapis.com`), storing the resulting
   ID/refresh token pair.
2. `Services/RuntimeClient.cs` connects to CORE over its named pipe
   (`\\.\pipe\ainativeruntime-runtime`), ensures this installation has its
   own Ed25519 keypair, binds it to the `app_desktop_windows` application
   id, establishes a session, and calls CORE's `device.register` RPC with a
   freshly refreshed Firebase ID token.

Three failure states are shown as distinct banners, because each implies a
different fix:

- **Runtime unavailable** — CORE's named pipe could not be reached at all.
  Start the Core daemon and retry.
- **Substrate offline** — Firebase (or, transitively, RTAPI behind CORE)
  could not be reached. Check network connectivity and retry.
- **Registration rejected** — the substrate was reached and affirmatively
  refused the request (no matching organization, a key conflict, an
  expired/invalid challenge, etc.). Retrying without changing anything will
  not help; the account/organization state needs to change first.

An ordinary wrong password is a fourth, separate "Sign-in failed" banner —
distinguishable from all three of the above, since fixing it means
re-entering credentials, not restarting a service or checking a network
connection.

## Token storage rule

**All secrets — the Firebase ID/refresh token pair and the application's
own Ed25519 installation private key — are stored exclusively in Windows
Credential Manager**, via P/Invoke against `advapi32.dll`
(`Services/CredentialStore.cs`: `CredWriteW`/`CredReadW`/`CredDeleteW`).

Never store a secret in:

- `ApplicationData.Current.LocalSettings` (or any other app-data
  registry/settings surface),
- a plain file on disk,
- a log line, `Debug.WriteLine`, or any diagnostic output.

Every Credential Manager entry is target-named
`com.ainativeruntime.runtime.windows/<key>`, deliberately distinct from
CORE's own `com.ainativeruntime.runtime/<key>` convention
(`ainativeruntime_shared/src/secret_store/windows_credential.rs`) — this
process and the CORE daemon hold different keypairs for different
principals (this app's installation key vs. CORE's node identity key) and
must never collide in the same namespace, even though both run as the same
signed-in Windows user.

## Architecture notes

- `Services/AuthService.cs` — Firebase email/password sign-in, explicit
  refresh-token exchange, and proactive refresh-before-expiry. Has zero
  knowledge of CORE, the named pipe, or any node/control-channel state;
  `SignOut()` only discards the locally stored Firebase credential.
- `Services/CredentialStore.cs` — the only class in this app allowed to
  persist a secret.
- `Services/RuntimeClient.cs` — the local CORE client. Generates and holds
  this **application's own** installation Ed25519 keypair; never touches
  the **node's** keypair, which CORE generates and holds entirely on the
  Rust side. Uses [NSec.Cryptography](https://github.com/ektrah/nsec) (a
  libsodium binding) for Ed25519, since net8.0's
  `System.Security.Cryptography` has no cross-platform Ed25519 API on this
  target framework.
- `Views/SignInPage.xaml` / `.xaml.cs` — the sign-in UI and the three
  failure-state banners described above.

This slice was written by close analogy to CORE's local API contract
(`ainativeruntime_node::api::installation`,
`ainativeruntime_node::api::device_registration`,
`ainativeruntime_node::api::framing`) and RTAPI's registration/binding DTOs
(`src/runtime-devices`, `src/runtime-installations`). It has not been
compiled or run — there is no Windows/.NET host in the environment it was
written in — so treat it as "implemented, not behaviorally verified" until
it has been built and exercised against a real running CORE daemon.
