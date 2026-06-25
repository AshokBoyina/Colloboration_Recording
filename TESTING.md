# Testing guide — Standalone Recording + Monitor + Recording package

Covers the pieces built recently: the recording pipeline (fMP4 → progressive MP4),
the new **Monitor console** (`NICE.Platform.Collaboration.UI`), the **screen-recording
NuGet package** (`NICE.Platform.Collaboration.Recording`), and READI auth.

## 0. Prerequisites

- .NET 10 SDK
- SQL Server reachable per `ConnectionStrings:DefaultConnection`
  (default `Server=.;Database=NICEStandaloneRecording;...`). The schema is created
  automatically at API startup (`EnsureCreated`) — no migration step.
- A Chromium browser (Chrome / Edge / WebView2) — required for `video/mp4`
  `MediaRecorder`. Firefox falls back to WebM.

URLs (from `launchSettings.json`):
- API: `https://localhost:65167` (or `http://localhost:65168`)
- UI:  `https://localhost:7200`  (or `http://localhost:5200`)

CORS already allows any `localhost` origin, so the UI → API cross-origin calls work.

## 1. Build

```powershell
cd C:\Users\ashok\CascadeProjects\Collaboration_standalone
dotnet build NICE.Platform.StandaloneRecording.sln
```

## 2. Run the API and sanity-check

```powershell
dotnet run --project NICE.Platform.Collaboration.API
```

In a browser/terminal (the `-k` skips the dev self-signed cert):
```powershell
curl -k https://localhost:65167/api/v1/demo/status      # { seeded, appCount, dbOk:true }
```
`dbOk:true` confirms the database is created and reachable.

## 3. Test A — recording pipeline + de-fragmentation (core fixes)

This uses the built-in `recorder.html` (same-origin, no package needed) and validates
the 415 fix, the upload-drain fix, and the fMP4 → progressive de-fragmentation.

1. Mint a token (auto-seeds the Readi app + user — no separate seed needed):
   ```powershell
   curl -k -X POST "https://localhost:65167/api/v1/demo/mint-token?name=John+Smith&role=StandAlone"
   ```
   Copy the `token` value from the response.
2. Open in Chrome/Edge:
   ```
   https://localhost:65167/recorder.html?token=PASTE_TOKEN
   ```
   (optional: append `&sessionId=<your-guid>` to supply the collaboration id).
3. Click **Start**, share a screen/window, record **5–10 seconds with visible motion**,
   click **Stop**. Watch the "Chunks: N | … KB" counter climb past the first chunk —
   there should be **no `Chunk N failed (415)`** lines.
4. Open the saved file from the configured `LocalStorage:RecordingsPath`
   (default `C:\Readi\Recordings\<yyyy-MM-dd>\<recordingId>.mp4`):
   - File size is well past 0 bytes (hundreds of KB+).
   - **It plays in Windows Media Player** with correct duration and seeking.

✅ Pass = a multi-second clip plays in WMP. (A 0-byte or header-only file means chunks
aren't arriving — re-check token/cert and the browser console.)

## 4. Test B — Monitor console (NICE.Platform.Collaboration.UI) + live from the client app

Run **3 startup projects**: API, the **client recorder** (`NICE.Platform.StandaloneRecording.Client`,
`http://localhost:5300`) and the **monitor** (`NICE.Platform.Collaboration.UI`, `http://localhost:5200`).
The client app and monitor must use the **same application** (`Readi`).

1. **Monitor** — browse `http://localhost:5200`, sign in:
   Application `Readi`, User Type `StandaloneMonitor`, a StandaloneMonitor token (from
   `/demo/mint-token?...&role=StandaloneMonitor&email=s@x.com`), API Key `chat-ui-key`
   → lands on `/standalone-monitor` (status **Connected**).
2. **Client recorder** — browse `http://localhost:5300`, click **Start recording**, share a screen.
   The client now (a) uploads recording chunks **and** (b) opens a standalone session on the
   collaboration hub and publishes a live WebRTC stream.
3. **In the monitor** the session appears in the left list. **Click it.** The on-screen signal
   log walks through `JoinStandaloneSession → Offer arrived → receiveOffer → Answer sent →
   ICE connected`, and the live screen renders.

> Live view requires the recorder to publish WebRTC. The client app now does this (added to
> the package). `recorder.html` also works as a live source. If the session lists but no video
> appears, the signal-log panel shows exactly where the WebRTC negotiation stalls.

✅ Pass = recording from `5300` shows up live in the monitor at `5200`.

## 5. Test C — the recording NuGet package (embeddable component + JS start)

The package ships an embeddable `<StandaloneRecorder/>` plus the
`window.niceScreenRecording` JS global. Two ways to test it:

### Option 1 — quick in-repo smoke test
Temporarily reference the package from the UI app and drop the component on a page:
```powershell
dotnet add NICE.Platform.Collaboration.UI reference NICE.Platform.Collaboration.Recording
```
Add to `Program.cs`:
```csharp
builder.Services.AddCollaborationRecording(o =>
{
    o.ApiBaseUrl    = "http://localhost:65168";
    o.ApplicationId = "<Readi app GUID>";   // from /api/v1/demo/apps
});
```
Put `<StandaloneRecorder />` on a test page, run the UI, and from the browser console:
```js
const t = "<a StandAlone token from /demo/mint-token>";
await window.niceScreenRecording.start({ token: t, collaborationId: crypto.randomUUID() });
// …record a few seconds…
await window.niceScreenRecording.stop();
```

### Option 2 — the sample customer app (NICE.Platform.StandaloneRecording.Client)
This project is a Blazor WASM app that consumes the recording component. For
development it references the component as a **ProjectReference** (in-solution), so it
just builds and runs — no packing, no local feed.

```powershell
dotnet run --project NICE.Platform.StandaloneRecording.Client    # http://localhost:5300
```
Configure it in `NICE.Platform.StandaloneRecording.Client/wwwroot/appsettings.json`
(`StandaloneRecording:ApiBaseUrl`, `ApplicationName`, `UserName`, `UserRole`). Open the
app, click **Start recording**, record a few seconds, **Stop** — it obtains a token via
the bundled helper, resolves the application id, and drives the component's
`window.niceScreenRecording.start({ token, collaborationId })`.

> To ship/consume it as an actual NuGet package later, run
> `dotnet pack NICE.Platform.Collaboration.Recording -c Release -o <feed>`, add `<feed>`
> as a source, and swap the ProjectReference for a `PackageReference`. (Avoid a
> repo-level `nuget.config` with `<clear/>` — it removes your machine's package sources
> and breaks restore for everything.)

What to verify:
- `window.niceScreenRecording.isReady()` returns `true` once the component has rendered.
- `start(token)` (or `start({ token, collaborationId })`) begins capture; the resulting
  MP4 in `RecordingsPath` plays in WMP.
- Passing `collaborationId` correlates the recording to that collaboration; omitting it
  has the server generate one (check the `CollaborationRecording.CollaborationId` row).
- `start()` with no token fails fast with the "access token is required" message.

✅ Pass = JS `start()`/`stop()` produce a playable recording, and the supplied
`collaborationId` is the one stored.

## 6. Test D — READI auth flag (optional)

Per-app override and the global flag (`appsettings.json`):
- Set `Applications:Readi:StaffAuthProvider = "READI"` (override), **or** remove it and
  set `FeatureFlags:UseReadiAuth = true` (global default).
- Set `AuthValidation:Endpoints:ReadiValidateUrl` to a reachable validate endpoint
  (or set `AuthValidation:UseMock = true` to use the `Mock:Readi` response).

Exercise it:
```powershell
curl -k -X POST https://localhost:65167/api/v1/collaboration/auth/validate `
  -H "X-Api-Key: <Readi api key>" -H "X-Access-Key: Readi" `
  -H "AuthToken: <readi token>"  -H "UserType: Agent"
```
✅ Pass = with mock on, a staff (non-External) validate returns success via READI;
External users still route to ANON.

## 7. Test E — Prod flags (Azure Blob, Azure SignalR, custom TURN)

Put test values in **appsettings.Development.json** so the prod baseline stays clean
(Development overrides win when you `dotnet run`).

### E1 — `UseAzureBlob` with the Azurite emulator (no Azure account needed)

1. Install + start Azurite (local Azure Storage emulator):
   ```powershell
   npm install -g azurite
   azurite --silent --location ./azurite-data       # blob endpoint http://127.0.0.1:10000
   ```
   (or Docker: `docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite`; it also
   ships with Visual Studio.)
2. In `NICE.Platform.Collaboration.API/appsettings.Development.json`:
   ```jsonc
   {
     "FeatureFlags": { "UseAzureBlob": true },
     "Azure": { "Blob": { "ConnectionString": "UseDevelopmentStorage=true", "Container": "collaboration" } }
   }
   ```
3. `dotnet run --project NICE.Platform.Collaboration.API` — startup must NOT throw
   (a bad/empty connection string fails fast).
4. Record a clip (recorder.html or the 5300 client) and **Stop**.
5. Verify in the API log: `AzureBlobStreamStore: uploaded recordings/<date>/<id>.mp4 (... bytes) to Azure Blob`.
6. Verify the blob exists and plays:
   ```powershell
   az storage blob list --container-name collaboration --connection-string "UseDevelopmentStorage=true" -o table
   ```
   (or use Azure Storage Explorer → "Local & Attached" → Storage Accounts → Emulator →
   Blob Containers → collaboration → `recordings/<date>/<id>.mp4` → Download → plays in WMP.)
7. Verify the **local scratch file is gone** (the file under `LocalStorage:RecordingsPath` was
   deleted after upload — Azure is now the durable store).
8. Verify SAS playback: `GET /api/v1/collaboration/recordings/{id}/sas-url` → returns an
   emulator blob URL with a SAS token → open it in the browser → the MP4 downloads/plays.

✅ Pass = the recording lands in the blob container, plays in WMP, the SAS URL works, and the
local temp copy is removed. For a **real** account, just set the connection string to your
storage account's key-based connection string (Portal → Access keys); the container is created
automatically.

### E2 — `UseAzureSignalR`
Needs a real **Azure SignalR Service** (no local emulator). Provision one (Default mode), set
`Azure:SignalR:ConnectionString`, `FeatureFlags:UseAzureSignalR=true`. Startup fails fast if the
connection string is still the placeholder. Pass = recorder + monitor still connect and stream
(traffic now flows through Azure SignalR; check the resource's "Live traces"/metrics).

### E3 — `UseCustomTurn`
Needs a TURN server (e.g. coturn). Set `IceServers:TurnServer` (`Urls`/`Username`/`Credential`),
`FeatureFlags:UseCustomTurn=true`. Pass = the recorder/monitor receive your TURN servers in the
`IceServersReady` event and ICE uses `relay` candidates — verify in `chrome://webrtc-internals`
(look for candidate type `relay`). Recording/chunk upload doesn't use TURN; only the live view does.

## Success checklist

- [ ] `dotnet build` clean.
- [ ] `/api/v1/demo/status` → `dbOk:true`.
- [ ] recorder.html clip plays in Windows Media Player (Test A).
- [ ] Monitor console connects and shows a live session (Test B).
- [ ] Package `start()/stop()` from JS produces a playable file; `collaborationId` honored (Test C).
- [ ] READI routing works with the flag/override (Test D).
- [ ] UseAzureBlob: recording uploads to the blob container and SAS playback works (Test E1).
