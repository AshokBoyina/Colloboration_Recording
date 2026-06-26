// ──────────────────────────────────────────────────────────────────────────
// recorder.js — screen capture + chunked upload for the embeddable
// <StandaloneRecorder/> Blazor component (NICE.Platform.Collaboration.Recording).
//
// ES module. Loaded by the component via:
//   import('./_content/NICE.Platform.Collaboration.Recording/recorder.js')
//
// Capture pipeline:
//   getDisplayMedia → MediaRecorder (fragmented MP4 preferred) → POST each
//   chunk to the Standalone Recording API. The server de-fragments the fMP4
//   into a progressive, Windows Media Player-compatible MP4 on finalize.
//
// IMPORTANT (matches the hardened recorder.html): every chunk upload promise is
// tracked, and stop() drains them all (Promise.allSettled) BEFORE returning, so
// the caller can finalize the recording only after the final fragment is on the
// server. Otherwise short clips lose their trailing fragment and become an
// unplayable init-only file.
// ──────────────────────────────────────────────────────────────────────────

const MIME_PREFERENCES = [
  'video/mp4;codecs=avc1,mp4a.40.2',   // Chrome / Edge / WebView2 — fragmented MP4 (preferred)
  'video/mp4',
  'video/webm;codecs=vp9,opus',         // Firefox fallback (WebM)
  'video/webm;codecs=vp8,opus',
  'video/webm'
];

// Per-instance recording state, keyed by a handle string so multiple components
// (or sequential recordings) never collide.
const sessions = new Map();

export async function validateToken(opts) {
  const apiBase = (opts.apiBase || '').replace(/\/$/, '');
  const res = await fetch(`${apiBase}/api/v1/collaboration/auth/validate`, {
    method: 'POST',
    headers: {
      'X-Api-Key': opts.apiKey,
      'X-Access-Key': opts.appName,
      'AuthToken': opts.authToken,
      'UserType': opts.userType || 'StandAlone'
    }
  });

  if (!res.ok) {
    const txt = await res.text();
    throw new Error(`auth/validate failed (${res.status}): ${txt}`);
  }

  const body = await res.json();
  return body.sessionToken || body.SessionToken || '';
}

function pickMimeType() {
  return MIME_PREFERENCES.find(m => MediaRecorder.isTypeSupported(m)) || '';
}

/**
 * Acquires the screen-capture stream. MUST be called first, directly within the
 * user-gesture call stack, so getDisplayMedia retains transient activation before
 * the (async) SignalR handshakes run.
 * @returns {Promise<string>} the negotiated MIME type.
 */
export async function acquire(handle) {
  handle = handle || 'default';

  const stream = await navigator.mediaDevices.getDisplayMedia({
    video: { frameRate: 15 },
    audio: true
  });

  const s = {
    dotNetRef: null,
    opts: null,
    stream,
    recorder: null,
    peer: null,
    mimeType: pickMimeType(),
    pendingUploads: [],
    chunkSeq: 0,
    chunksSent: 0,
    totalBytes: 0,
    stopping: false
  };
  sessions.set(handle, s);

  // If the user ends the OS share ("Stop sharing"), surface it so .NET can finalize.
  const videoTrack = stream.getVideoTracks()[0];
  if (videoTrack) videoTrack.addEventListener('ended', () => notify(s, 'ended', ''));

  return s.mimeType;
}

/**
 * Starts MediaRecorder + chunk upload on the already-acquired stream.
 * @param {object} dotNetRef  DotNetObjectReference for callbacks ([JSInvokable] OnRecorderEvent).
 * @param {object} opts       { handle, chunkUrl, token, videoBitsPerSecond, timesliceMs }
 */
export async function beginUpload(dotNetRef, opts) {
  const handle = opts.handle || 'default';
  const s = sessions.get(handle);
  if (!s || !s.stream) throw new Error('beginUpload: no acquired stream — call acquire() first.');

  s.dotNetRef = dotNetRef;
  s.opts = opts;

  const recorder = new MediaRecorder(s.stream, {
    mimeType: s.mimeType,
    videoBitsPerSecond: opts.videoBitsPerSecond || 2_000_000
  });
  s.recorder = recorder;

  // Track each upload promise so stop() can await them before finalizing.
  recorder.ondataavailable = e => {
    if (e.data && e.data.size > 0) s.pendingUploads.push(uploadChunk(s, e.data));
  };

  recorder.start(opts.timesliceMs || 2000);
  await notify(s, 'started', s.mimeType);
}

// ── WebRTC live publish (lets the monitor watch in real time) ────────────────

/**
 * Creates a WebRTC offer from the acquired capture stream and returns the SDP
 * (after ICE gathering). The component sends it to the hub via ShareScreenOffer.
 */
export async function createOffer(handle, iceServersJson) {
  const s = sessions.get(handle || 'default');
  if (!s || !s.stream) throw new Error('createOffer: no active capture stream.');

  let iceServers = [];
  try { iceServers = (JSON.parse(iceServersJson || '[]') || []).map(normalizeIce).filter(Boolean); }
  catch { /* ignore — empty list still works for same-host */ }

  if (s.peer) { try { s.peer.close(); } catch {} }
  const pc = new RTCPeerConnection({ iceServers });
  s.peer = pc;
  s.stream.getTracks().forEach(t => pc.addTrack(t, s.stream));

  const offer = await pc.createOffer();
  await pc.setLocalDescription(offer);
  await waitForIce(pc);
  return pc.localDescription.sdp;
}

/** Applies the monitor's WebRTC answer. */
export async function setAnswer(handle, sdp) {
  const s = sessions.get(handle || 'default');
  if (!s || !s.peer || !sdp) return;
  try { await s.peer.setRemoteDescription({ type: 'answer', sdp }); }
  catch (e) { console.warn('niceScreenRecording.setAnswer:', e); }
}

function normalizeIce(e) {
  if (!e) return null;
  const urls = e.urls ?? e.Urls ?? e.url ?? e.Url;
  if (!urls) return null;
  const o = { urls };
  const u = e.username ?? e.Username; if (u) o.username = u;
  const c = e.credential ?? e.Credential; if (c) o.credential = c;
  return o;
}

function waitForIce(pc) {
  return new Promise(resolve => {
    if (pc.iceGatheringState === 'complete') return resolve();
    const handler = () => {
      if (pc.iceGatheringState === 'complete') {
        pc.removeEventListener('icegatheringstatechange', handler);
        resolve();
      }
    };
    pc.addEventListener('icegatheringstatechange', handler);
    setTimeout(resolve, 3000); // don't hang if gathering stalls
  });
}

async function uploadChunk(s, blob) {
  const seq = s.chunkSeq++;
  const mime = s.mimeType || 'video/mp4';
  try {
    const headers = {
      'Content-Type':         mime,
      'Authorization':        `Bearer ${s.opts.token}`,
      'X-Chunk-Sequence':     String(seq),
      'X-Recording-MimeType': mime
    };
    if (s.opts.accessKey) headers['X-Access-Key'] = s.opts.accessKey;

    const r = await fetch(s.opts.chunkUrl, {
      method: 'POST',
      headers,
      body: blob
    });
    if (r.ok) {
      s.chunksSent++;
      s.totalBytes += blob.size;
      await notify(s, 'chunk', JSON.stringify({ seq, chunksSent: s.chunksSent, totalBytes: s.totalBytes }));
    } else {
      await notify(s, 'error', `Chunk ${seq} upload failed (HTTP ${r.status}).`);
    }
  } catch (e) {
    await notify(s, 'error', `Chunk ${seq} upload error: ${e}`);
  }
}

/**
 * Stops capture, DRAINS all in-flight chunk uploads, and releases the stream.
 * Returns only after every chunk has reached the server.
 * @returns {Promise<{chunksSent:number,totalBytes:number}>}
 */
export async function stop(handle) {
  const s = sessions.get(handle || 'default');
  if (!s) return { chunksSent: 0, totalBytes: 0 };
  if (s.stopping) return { chunksSent: s.chunksSent, totalBytes: s.totalBytes };
  s.stopping = true;

  if (s.recorder && s.recorder.state !== 'inactive') {
    await new Promise(res => { s.recorder.onstop = res; s.recorder.stop(); });
  }

  // Drain the final fragment (and any other in-flight uploads) before finalize.
  if (s.pendingUploads.length) {
    await Promise.allSettled(s.pendingUploads);
    s.pendingUploads = [];
  }

  if (s.peer) {
    try { s.peer.close(); } catch {}
    s.peer = null;
  }

  if (s.stream) {
    s.stream.getTracks().forEach(t => t.stop());
    s.stream = null;
  }

  const result = { chunksSent: s.chunksSent, totalBytes: s.totalBytes };
  await notify(s, 'stopped', JSON.stringify(result));
  sessions.delete(handle || 'default');
  return result;
}

async function notify(s, evt, payload) {
  try {
    if (s.dotNetRef) await s.dotNetRef.invokeMethodAsync('OnRecorderEvent', evt, payload || '');
  } catch {
    /* component disposed mid-callback — ignore */
  }
}

// ──────────────────────────────────────────────────────────────────────────
// JS → Blazor bridge
//
// Each <StandaloneRecorder/> registers itself here on first render, exposing a
// plain-JS global so host page scripts can drive recording without touching
// Blazor interop directly:
//
//   await window.niceScreenRecording.start();           // first/only recorder
//   await window.niceScreenRecording.start("myName");   // a named recorder
//   await window.niceScreenRecording.stop();
//   window.niceScreenRecording.isReady();                // bool
//
// start()/stop() invoke the component's [JSInvokable] StartAsync/StopAsync, which
// perform the SignalR handshake + capture in .NET — no @microsoft/signalr needed
// on the page.
// ──────────────────────────────────────────────────────────────────────────

const components = new Map(); // key -> DotNetObjectReference

export function registerComponent(key, dotNetRef) {
  components.set(key, dotNetRef);
  ensureGlobal();
}

export function unregisterComponent(key) {
  components.delete(key);
}

function resolve(key) {
  if (key != null && components.has(key)) return components.get(key);
  const first = components.values().next();
  return first.done ? null : first.value;          // default: first registered
}

function ensureGlobal() {
  if (window.niceScreenRecording) return;
  window.niceScreenRecording = {
    // start(accessToken)                                  — token string, first/only recorder
    // start({ token, collaborationId, name })             — token + optional session id + target
    start(arg) {
      let token = null, name = null, collaborationId = null;
      if (typeof arg === 'string') {
        token = arg;
      } else if (arg && typeof arg === 'object') {
        token           = arg.token ?? null;
        name            = arg.name ?? null;
        collaborationId = arg.collaborationId ?? null;
      }
      const c = resolve(name);
      if (!c) return Promise.reject(new Error(
        'niceScreenRecording: no <StandaloneRecorder/> is registered on this page yet.'));
      return c.invokeMethodAsync('StartAsync', token, collaborationId);
    },
    // stop(name?) — the access token is not needed to stop (hub already connected).
    stop(arg) {
      const name = (arg && typeof arg === 'object') ? (arg.name ?? null) : (arg ?? null);
      const c = resolve(name);
      if (!c) return Promise.reject(new Error(
        'niceScreenRecording: no <StandaloneRecorder/> is registered on this page yet.'));
      return c.invokeMethodAsync('StopAsync');
    },
    isReady(name) {
      return resolve(name) != null;
    }
  };
}
