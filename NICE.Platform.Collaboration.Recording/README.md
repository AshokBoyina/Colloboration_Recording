# NICE.Platform.Collaboration.Recording

An embeddable **Blazor screen-recording component** for NICE Collaboration. Host
applications install this package and drop `<StandaloneRecorder/>` onto a page to
capture the user's screen and stream it to the Standalone Recording API.

Capture uses the browser's `MediaRecorder` with **fragmented MP4** (falling back to
WebM where MP4 recording is unavailable). The server de-fragments the fMP4 into a
**progressive, Windows Media Player-compatible MP4** on finalize — no FFmpeg, no
re-encoding.

## Install

```
dotnet add package NICE.Platform.Collaboration.Recording
```

## Configure (host `Program.cs`)

```csharp
builder.Services.AddCollaborationRecording(builder.Configuration);
// or inline:
builder.Services.AddCollaborationRecording(o =>
{
    o.ApiBaseUrl    = "https://recording-api.example.com";
    o.ApplicationId = appGuid.ToString();
    // Do NOT set AccessToken here — it is user-specific and passed when recording
    // starts: window.niceScreenRecording.start(userAccessToken).
});
```

`appsettings.json`:

```json
"CollaborationRecording": {
  "ApiBaseUrl": "https://recording-api.example.com",
  "ApplicationId": "00000000-0000-0000-0000-000000000000",
  "VideoBitsPerSecond": 2000000,
  "TimesliceMs": 2000
}
```

## Use

```razor
<StandaloneRecorder ApiBaseUrl="@apiBase"
                    ApplicationId="@appGuid"
                    AccessToken="@userToken"
                    OnStarted="mime => Log($\"recording {mime}\")"
                    OnStopped="r => Log($\"saved {r.ChunksSent} chunks, {r.TotalBytes} bytes\")"
                    OnError="msg => Log(msg)" />
```

Provide `ChildContent` to fully replace the default Start/Stop UI while still
driving recording through the cascaded component instance.

## Start / stop from JavaScript

Each `<StandaloneRecorder/>` registers a plain-JS global on first render, so your
host page scripts can trigger recording without any Blazor interop or the
`@microsoft/signalr` client — the SignalR handshake and capture happen inside the
component (.NET).

The **access token is user-specific, so pass it at the moment recording starts**
(not in DI). It authenticates both the recording hub and the chunk uploads:

```js
const userAccessToken = /* the signed-in user's LOCAL_JWT / READI bearer token */;

await window.niceScreenRecording.start(userAccessToken);   // start for this user
await window.niceScreenRecording.stop();                    // stop needs no token

if (window.niceScreenRecording.isReady()) { /* a recorder is mounted */ }
```

```html
<button onclick="window.niceScreenRecording.start(getCurrentUserToken())">Record</button>
<button onclick="window.niceScreenRecording.stop()">Stop</button>
```

### Correlate to your own session (collaborationId)

To tie the recording to your host session/ticket id, pass `collaborationId` via the
object form. If omitted, the API generates one:

```js
await window.niceScreenRecording.start({
  token:           userAccessToken,
  collaborationId: myTicketGuid     // optional — server auto-generates when absent
});
```

The recording is attached to that collaboration (created server-side if it does not
exist yet), so multiple recordings for the same session correlate to one record.

Multiple recorders? Give each a `Name` and target it with the object form:

```razor
<StandaloneRecorder Name="primary" />
```
```js
await window.niceScreenRecording.start({ token: userAccessToken, collaborationId: ticket, name: "primary" });
await window.niceScreenRecording.stop("primary");
```

> The token passed to `start(...)` takes priority over any `AccessToken` parameter
> or `CollaborationRecording:AccessToken` configuration (those remain as optional
> fallbacks). `ApiBaseUrl` and `ApplicationId` are not user-specific, so keep them
> in DI/config.

The component must be mounted (rendered once) before the global is available —
`isReady()` returns `true` once it is. Place the recorder anywhere on the page
(it can be visually hidden if you only want JS control).

## How it works

1. Connects to the recording hub (`/hubs/v1/recording`) and calls `StartRecording`,
   receiving a `recordingId`.
2. `getDisplayMedia` → `MediaRecorder` emits a chunk every `TimesliceMs`; each chunk
   is `POST`ed to `/api/v1/collaboration/recordings/{recordingId}/chunks`.
3. On stop, **all in-flight chunk uploads are drained** before `StopRecording` is
   called — this guarantees the trailing fragment reaches the server, so even very
   short clips finalize into a playable file.

It also **publishes a live WebRTC stream** so a supervisor monitor console can watch
in real time: on start it opens a standalone session on the collaboration hub
(`/hubs/v1/collaboration` → `StartStandaloneSession`); when a monitor joins, it answers
the `ScreenOfferRequested` with a WebRTC offer (`ShareScreenOffer`) built from the same
capture stream, and applies the monitor's `Answer`. Recording (to file) and live view
run off one `getDisplayMedia` capture.

## Requirements

- A reachable Standalone Recording API (hub + chunk endpoint + auth).
- A bearer token (`AccessToken`) for the signed-in user.
- A Chromium-based browser/WebView2 for MP4 capture (Firefox falls back to WebM).
