/* ══════════════════════════════════════════════════════════════
   NICE Platform Collaboration — Chat UI JS helpers
   ══════════════════════════════════════════════════════════════ */

/* ── Session persistence (localStorage so refresh keeps auth) ─── */
window.chatStorage = {
    saveSession: (json) => {
        try { localStorage.setItem('nice_session', json); } catch {}
    },
    loadSession: () => {
        try { return localStorage.getItem('nice_session') ?? null; } catch { return null; }
    },
    clear: () => {
        try { localStorage.removeItem('nice_session'); } catch {}
        try { localStorage.removeItem('nice_token');   } catch {}   // legacy key — clean up too
    },
    // Kept for backward compat — no longer primary storage
    setToken: (token) => {
        try { localStorage.setItem('nice_token', token); } catch {}
    },
    getToken: () => {
        try { return localStorage.getItem('nice_token') ?? null; } catch { return null; }
    }
};

/* ── Scroll helpers ───────────────────────────────────────────── */
window.chatHelpers = {
    scrollToBottom: (elementId) => {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    },
    focusElement: (elementId) => {
        const el = document.getElementById(elementId);
        if (el) el.focus();
    },
    requestFullscreen: (elementId) => {
        const el = document.getElementById(elementId);
        if (!el) {
            console.warn('[chatHelpers] requestFullscreen: element not found:', elementId);
            return;
        }
        // Prefer the video element's own fullscreen to avoid container clip issues.
        // This MUST be called from a direct user gesture handler (onclick, not an
        // async Blazor @onclick chain) otherwise the browser will reject it.
        const fn = el.requestFullscreen   || el.webkitRequestFullscreen ||
                   el.mozRequestFullScreen || el.msRequestFullscreen;
        if (fn) {
            fn.call(el).catch(err => console.warn('[chatHelpers] fullscreen failed:', err.message));
        } else {
            console.warn('[chatHelpers] requestFullscreen: API not supported in this browser');
        }
    }
};

/* ══════════════════════════════════════════════════════════════
   screenShare — simplified WebRTC with ICE gathering completion.
   No trickle ICE: we collect all candidates into the SDP before
   returning it to .NET, so no JS→C# ICE callbacks are needed.
   ══════════════════════════════════════════════════════════════ */
window.screenShare = (() => {

    const ICE_SERVERS = [{ urls: 'stun:stun.l.google.com:19302' }];
    const ICE_TIMEOUT_MS = 4000;   // max wait for ICE gathering

    // Each re-offer (for a new viewer joining late) gets its OWN PeerConnection so
    // existing viewers are not disturbed.  Answers are matched to the LAST created PC.
    let _sharePCs    = [];          // offerer side — one entry per viewer (external user)
    let _viewPCs     = new Map();   // answerer side — RTCPeerConnection keyed by collaborationId
    let _viewStreams  = new Map();  // answerer side — MediaStream keyed by collaborationId
                                    // kept alive so the video can be re-attached after a
                                    // Blazor re-render when the agent switches sessions.
    let _shareStream = null;

    /* ── Wait for ICE gathering to complete ─────────────────── */
    function waitForIce(pc) {
        if (pc.iceGatheringState === 'complete') return Promise.resolve();
        return new Promise((resolve) => {
            const timer = setTimeout(resolve, ICE_TIMEOUT_MS);
            pc.addEventListener('icegatheringstatechange', function handler() {
                if (pc.iceGatheringState === 'complete') {
                    clearTimeout(timer);
                    pc.removeEventListener('icegatheringstatechange', handler);
                    resolve();
                }
            });
        });
    }

    let _dotnetShareRef = null;   // DotNetObjectReference for stop-share callback

    /* ── Build a new sharing PeerConnection and return it ───────── */
    function _makeSharePC() {
        const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
        _shareStream.getTracks().forEach(t => pc.addTrack(t, _shareStream));
        return pc;
    }

    /* ── External user: start screen share and return offer SDP ─ */
    async function getOffer(dotnetRef) {
        try {
            _dotnetShareRef = dotnetRef || null;

            // Close any previous sharing connections
            _sharePCs.forEach(pc => { try { pc.close(); } catch {} });
            _sharePCs = [];

            // Ask user to pick a screen
            _shareStream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });

            // Stop share when user clicks browser's "Stop sharing" button
            _shareStream.getVideoTracks()[0].addEventListener('ended', () => {
                stop();
                // Notify .NET so it can inform the hub → agent/supervisor clear video
                if (_dotnetShareRef) {
                    _dotnetShareRef.invokeMethodAsync('OnScreenShareStopped').catch(() => {});
                    _dotnetShareRef = null;
                }
            });

            const pc = _makeSharePC();
            const offer = await pc.createOffer();
            await pc.setLocalDescription(offer);
            await waitForIce(pc);
            _sharePCs.push(pc);

            return pc.localDescription.sdp;
        } catch (err) {
            console.warn('[screenShare] getOffer failed:', err.message);
            return '';
        }
    }

    /* ── Re-offer for a late-joining viewer (supervisor, etc.)
          Creates a NEW PeerConnection — existing ones stay alive so
          other viewers (e.g. the agent) are NOT disconnected.        ─ */
    async function reOffer() {
        if (!_shareStream || !_shareStream.active) {
            console.warn('[screenShare] reOffer: no active stream — _shareStream is', _shareStream);
            return '';
        }
        try {
            console.info('[screenShare] reOffer: creating new PC, tracks:', _shareStream.getTracks().length);
            const pc = _makeSharePC();
            const offer = await pc.createOffer();
            await pc.setLocalDescription(offer);
            await waitForIce(pc);
            _sharePCs.push(pc);   // keep all previous PCs — agent stream unaffected
            console.info('[screenShare] reOffer: done, _sharePCs.length=', _sharePCs.length, 'iceState=', pc.iceConnectionState);
            return pc.localDescription.sdp;
        } catch (err) {
            console.warn('[screenShare] reOffer failed:', err.message);
            return '';
        }
    }

    /* ── Agent / Supervisor: receive offer, attach video, return answer SDP ─
          collabId:        the collaboration ID this offer belongs to — used to
                           key the PeerConnection so multiple simultaneous streams
                           (one per customer) never interfere with each other.
          enableRecording: pass true to auto-record the incoming stream.
          When omitted the value from window.screenShareConfig.enableRecording
          (set by the Blazor host page) is used; falls back to false.         */
    async function receiveOffer(collabId, sdp, videoElementId, enableRecording) {
        const doRecord = enableRecording !== undefined
            ? !!enableRecording
            : !!(window.screenShareConfig && window.screenShareConfig.enableRecording);

        try {
            // Close any PREVIOUS connection for this specific collaboration only
            const existing = _viewPCs.get(collabId);
            if (existing) { try { existing.close(); } catch {} _viewPCs.delete(collabId); }

            const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
            _viewPCs.set(collabId, pc);

            pc.oniceconnectionstatechange = () => {
                console.info('[screenShare] ICE state changed for', collabId, '→', pc.iceConnectionState);
            };

            pc.ontrack = (e) => {
                if (!e.streams[0]) return;
                const stream = e.streams[0];

                // Persist the stream so it can be reattached if Blazor unmounts and
                // remounts the <video> element (e.g. agent switches sessions and back).
                _viewStreams.set(collabId, stream);

                console.info('[screenShare] ontrack fired for', collabId, '→ looking for', videoElementId);
                const video = document.getElementById(videoElementId);
                if (video) {
                    video.srcObject = stream;
                    // Some browsers block autoplay until the user interacts with the page.
                    // We attempt play() and, if blocked, the video onclick="this.play()"
                    // lets the user start it with a single click on the video itself.
                    video.play().catch(err => {
                        console.warn('[screenShare] autoplay blocked — click video to start:', err.message);
                    });
                    console.info('[screenShare] stream attached to', videoElementId);
                } else {
                    // Element not in DOM yet (Blazor may still be rendering).
                    // Retry once after a short delay.
                    console.warn('[screenShare] video element not found:', videoElementId, '— retrying in 200ms');
                    setTimeout(() => {
                        const v2 = document.getElementById(videoElementId);
                        if (v2) { v2.srcObject = stream; v2.play().catch(() => {}); }
                        else { console.error('[screenShare] video element still missing after retry:', videoElementId); }
                    }, 200);
                }

                // ── Auto-recording (gated by flag) ─────────────────────
                if (doRecord) {
                    _startDemoRecording(stream, videoElementId);
                }
            };

            await pc.setRemoteDescription({ type: 'offer', sdp });
            const answer = await pc.createAnswer();
            await pc.setLocalDescription(answer);
            await waitForIce(pc);

            return pc.localDescription.sdp;
        } catch (err) {
            console.warn('[screenShare] receiveOffer failed:', err.message);
            return '';
        }
    }

    /* ── Reattach a stored stream to a freshly rendered <video> element ──
          Call after a Blazor re-render re-creates the video element (e.g.
          the agent switched sessions and switched back).  The WebRTC stream
          is still alive in _viewStreams — we just need to set srcObject again. */
    function reattachStream(collabId, videoElementId) {
        const stream = _viewStreams.get(collabId);
        if (!stream) {
            console.warn('[screenShare] reattachStream: no stored stream for collab', collabId);
            return;
        }
        const video = document.getElementById(videoElementId);
        if (!video) {
            // Element might not be in DOM yet — retry after a frame
            console.warn('[screenShare] reattachStream: video element not found:', videoElementId, '— retrying in 200ms');
            setTimeout(() => {
                const v2 = document.getElementById(videoElementId);
                if (v2) { v2.srcObject = stream; v2.play().catch(() => {}); console.info('[screenShare] reattachStream: attached on retry'); }
                else { console.error('[screenShare] reattachStream: element still missing after retry:', videoElementId); }
            }, 200);
            return;
        }
        video.srcObject = stream;
        video.play().catch(() => {});
        console.info('[screenShare] reattachStream: reattached stream to', videoElementId);
    }

    /* ── Return the ICE connection state for a viewer PC ────────────────
          Called by StandaloneMonitor's PollIceStateAsync to check when
          the WebRTC connection has been established.
          Returns the iceConnectionState string, or 'no-pc' if not found. */
    function getViewPcState(collabId) {
        const pc = _viewPCs.get(collabId);
        if (!pc) return 'no-pc';
        return pc.iceConnectionState;
    }

    /* ── Close the viewer PeerConnection for one specific collaboration ──
          Call when a customer stops sharing or a session ends, so that the
          other simultaneous screen-shares are left untouched.             */
    function stopView(collabId) {
        const pc = _viewPCs.get(collabId);
        if (pc) { try { pc.close(); } catch {} _viewPCs.delete(collabId); }
        _viewStreams.delete(collabId);
        // Stop any ongoing demo recording for that video element
        _stopDemoRecording();
    }

    /* ── Recording config (set by Blazor via screenShare.configure) ─ */
    // window.screenShareConfig is the shared config bag; write defaults here
    // so the object always exists before Blazor has a chance to call configure().
    if (!window.screenShareConfig) window.screenShareConfig = {};

    /** Configure upload target, feature flags, and auth credentials from Blazor.
     *  @param {object} opts  { uploadUrl, enableRecording, collaborationId, token, apiKey } */
    function configure(opts) {
        window.screenShareConfig = Object.assign(window.screenShareConfig || {}, opts);
    }

    /* ── Recording toast ─────────────────────────────────────────── */
    function _showRecordingToast(message, isError) {
        const toast = document.createElement('div');
        toast.style.cssText =
            'position:fixed;bottom:20px;left:50%;transform:translateX(-50%);' +
            'background:' + (isError ? '#b91c1c' : '#003727') + ';color:#fff;' +
            'border-radius:8px;padding:8px 20px;font-size:.8rem;font-weight:600;' +
            'box-shadow:0 4px 20px rgba(0,0,0,.35);z-index:9999;' +
            'animation:cw-in .2s ease;white-space:nowrap;max-width:90vw;' +
            'overflow:hidden;text-overflow:ellipsis';
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(() => { toast.style.opacity='0'; toast.style.transition='opacity .4s'; }, 3500);
        setTimeout(() => toast.remove(), 4000);
    }

    /* ── Fallback download button (used when upload is not configured) ─ */
    function _triggerDownload(blob, mimeType, videoElementId) {
        const ts  = new Date().toISOString().replace(/[:.]/g, '-');
        const url = URL.createObjectURL(blob);
        const outer   = document.getElementById(videoElementId)?.closest('.nc-rp-section');
        const target  = outer || document.getElementById(videoElementId)?.parentElement?.parentElement;
        const btn = document.createElement('button');
        btn.textContent  = '⬇ Download recording';
        btn.style.cssText =
            'display:block;width:calc(100% - 24px);margin:6px 12px;padding:6px 14px;' +
            'background:#003727;color:#fff;border:none;border-radius:6px;cursor:pointer;' +
            'font-size:.76rem;font-weight:600;white-space:nowrap;text-align:center';
        btn.onclick = () => {
            const a    = document.createElement('a');
            a.href     = url;
            a.download = `screen-recording-${ts}.webm`;
            a.click();
            URL.revokeObjectURL(url);
            btn.remove();
        };
        if (target) target.appendChild(btn);
    }

    /* ── Demo recording: capture incoming stream → auto-upload on stop ─ */
    let _recorder = null;
    // NOTE: chunks are stored in a closure-local array per recording session
    // (not a shared module-level array) so that a second _startDemoRecording call
    // cannot clear the previous session's data before its onstop fires.

    function _getBestMimeType() {
        const types = [
            'video/webm;codecs=vp9,opus',
            'video/webm;codecs=vp8,opus',
            'video/webm;codecs=vp9',
            'video/webm;codecs=vp8',
            'video/webm',
            'video/mp4'
        ];
        return types.find(t => MediaRecorder.isTypeSupported(t)) || '';
    }

    function _startDemoRecording(stream, videoElementId) {
        // Stop any previous recorder — its onstop will still fire with its own
        // closure-captured chunk array, so no data is lost.
        if (_recorder && _recorder.state !== 'inactive') {
            _recorder.stop();
        }
        _recorder = null;

        const mimeType = _getBestMimeType();
        console.info('[screenShare] Recording MIME:', mimeType || '(browser default)');

        try {
            // Use a closure-local array so each recording session owns its own data.
            // A concurrent or subsequent call to _startDemoRecording cannot clear these chunks.
            const chunks = [];
            // Snapshot the upload config NOW so it is captured in the closure even if
            // window.screenShareConfig is mutated (or cleared) before onstop fires.
            const uploadUrl = window.screenShareConfig?.uploadUrl        || '';
            const collabId  = window.screenShareConfig?.collaborationId  || '';
            const token     = window.screenShareConfig?.token            || '';
            const apiKey    = window.screenShareConfig?.apiKey           || '';

            _recorder = new MediaRecorder(stream, mimeType ? { mimeType } : {});

            // ── Inject a visible REC badge next to the LIVE badge ──────────
            const container = document.getElementById(videoElementId)?.parentElement;
            let recBadge = null;
            if (container) {
                recBadge = document.createElement('div');
                recBadge.id = `rec-badge-${videoElementId}`;
                recBadge.style.cssText =
                    'position:absolute;top:6px;right:8px;display:flex;align-items:center;' +
                    'gap:4px;background:rgba(180,0,0,.75);border-radius:20px;' +
                    'padding:2px 8px;font-size:.65rem;color:#fff;font-weight:700;' +
                    'letter-spacing:.03em;z-index:10';
                recBadge.innerHTML =
                    '<span style="width:7px;height:7px;border-radius:50%;background:#fff;' +
                    'animation:pulse 1s infinite;display:inline-block"></span>REC';
                container.style.position = 'relative';   // ensure overlay works
                container.appendChild(recBadge);
            }

            _recorder.ondataavailable = (e) => {
                if (e.data && e.data.size > 0) chunks.push(e.data);
            };

            _recorder.onstop = async () => {
                // Remove REC badge
                recBadge?.remove();

                console.info('[screenShare] onstop fired — chunks:', chunks.length,
                             'uploadUrl:', uploadUrl ? uploadUrl.slice(-40) : '(none)',
                             'token:', token ? '(present)' : '(MISSING)',
                             'apiKey:', apiKey ? '(present)' : '(MISSING)');

                if (chunks.length === 0) {
                    console.warn('[screenShare] Recording stopped with no data — nothing to upload.');
                    return;
                }

                const blob = new Blob(chunks, { type: mimeType || 'video/webm' });
                console.info('[screenShare] Blob ready:', blob.size, 'bytes');

                if (uploadUrl) {
                    // ── Auto-save to API server ────────────────────────────
                    _showRecordingToast('⏫ Saving recording…', false);
                    try {
                        const ts   = new Date().toISOString().replace(/[:.]/g, '-');
                        const form = new FormData();
                        form.append('file', blob, `recording-${ts}.webm`);
                        if (collabId) form.append('collaborationId', collabId);

                        // Include the Bearer token and app key so the upload endpoint accepts the request.
                        const headers = {};
                        if (token)  headers['Authorization'] = `Bearer ${token}`;
                        if (apiKey) headers['X-Api-Key']     = apiKey;

                        const res = await fetch(uploadUrl, { method: 'POST', body: form, headers });
                        if (res.ok) {
                            const data = await res.json();
                            _showRecordingToast(`✓ Saved: ${data.fileName}`, false);
                            console.info('[screenShare] Recording saved to server:', data.path, data);
                        } else {
                            const body = await res.text().catch(() => '');
                            console.warn('[screenShare] Upload failed, status:', res.status, body);
                            _showRecordingToast(`Upload failed (${res.status}) — downloading locally`, true);
                            _triggerDownload(blob, mimeType, videoElementId);
                        }
                    } catch (err) {
                        console.warn('[screenShare] Upload error:', err.message);
                        _showRecordingToast('Upload error — downloading locally', true);
                        _triggerDownload(blob, mimeType, videoElementId);
                    }
                } else {
                    // ── No upload URL configured — fall back to download ──
                    console.warn('[screenShare] No uploadUrl configured — falling back to download.');
                    _triggerDownload(blob, mimeType, videoElementId);
                }
            };

            _recorder.start(1000);   // collect chunk every 1 s
            console.info('[screenShare] Auto-recording started for collab:', collabId || '(no id)');
        } catch (err) {
            console.warn('[screenShare] Auto-recording unavailable:', err.message);
        }
    }

    function _stopDemoRecording() {
        const r = _recorder;
        _recorder = null;   // Clear reference immediately so a concurrent call cannot double-stop.
                            // The onstop handler is already closure-bound and does NOT reference
                            // _recorder, so nulling here is safe regardless of when onstop fires.
        if (r && r.state !== 'inactive') {
            r.stop();       // Triggers final ondataavailable flush, then onstop → upload.
        }
        // If r.state was already 'inactive' (auto-stopped when stream tracks ended),
        // onstop has already been queued by the browser — upload will fire on its own.
    }

    /* ── External user: apply the answer SDP from a viewer ─────
          Answers are matched to the LAST created PeerConnection so
          that re-offer answers go to the supervisor's PC while
          the agent's existing PC remains untouched.               ─ */
    async function setAnswer(sdp) {
        try {
            // Apply to the most-recently created PC (the one awaiting this answer)
            const pc = _sharePCs.length > 0 ? _sharePCs[_sharePCs.length - 1] : null;
            console.info('[screenShare] setAnswer: _sharePCs.length=', _sharePCs.length,
                         'signalingState=', pc?.signalingState, 'sdpLen=', sdp?.length);
            if (!pc) { console.warn('[screenShare] setAnswer: no PC available'); return; }
            if (pc.signalingState === 'stable') { console.warn('[screenShare] setAnswer: PC already stable — answer ignored'); return; }
            await pc.setRemoteDescription({ type: 'answer', sdp });
            console.info('[screenShare] setAnswer: applied, iceState=', pc.iceConnectionState);
        } catch (err) {
            console.warn('[screenShare] setAnswer failed:', err.message);
        }
    }

    /* ── Sender-side recording (Standalone user) ──────────────────────────
       Records the ALREADY-CAPTURED _shareStream without calling getDisplayMedia
       again.  Uploads 1-second webm chunks to the API exactly as agentRecorder
       does, so DualRecordingStreamStore writes to local disk + Azure Blob.
       Call AFTER getOffer() has set _shareStream.                           */
    let _senderRecorder  = null;
    let _senderSequence  = 0;

    async function startSenderRecording(recordingId, chunkUrl, token) {
        if (!_shareStream || !_shareStream.active) {
            console.warn('[screenShare] startSenderRecording: no active _shareStream — call getOffer() first');
            return;
        }
        if (_senderRecorder && _senderRecorder.state !== 'inactive') {
            console.warn('[screenShare] startSenderRecording: already recording');
            return;
        }

        const mimeType = _getBestMimeType();
        _senderSequence = 0;

        try {
            _senderRecorder = new MediaRecorder(_shareStream, mimeType ? { mimeType } : {});

            _senderRecorder.ondataavailable = async (e) => {
                if (!e.data || e.data.size === 0) return;
                const seq = ++_senderSequence;
                try {
                    const resp = await fetch(chunkUrl, {
                        method:  'POST',
                        headers: {
                            'Authorization': `Bearer ${token}`,
                            'X-Api-Key':     'chat-ui-key',
                            'Content-Type':  'application/octet-stream',
                            'X-Sequence':    String(seq)
                        },
                        body: e.data
                    });
                    if (!resp.ok) {
                        console.warn('[screenShare] chunk upload failed, seq:', seq, 'status:', resp.status);
                    }
                } catch (err) {
                    console.warn('[screenShare] chunk upload error, seq:', seq, err.message);
                }
            };

            _senderRecorder.onerror = (e) => {
                console.warn('[screenShare] MediaRecorder error:', e.error?.message);
            };

            _senderRecorder.start(1000);   // 1-second chunks, matching server expectation
            console.info('[screenShare] startSenderRecording started, recording id:', recordingId);
        } catch (err) {
            console.warn('[screenShare] startSenderRecording failed:', err.message);
            _senderRecorder = null;
        }
    }

    function stopSenderRecording() {
        if (_senderRecorder && _senderRecorder.state !== 'inactive') {
            _senderRecorder.stop();
            console.info('[screenShare] stopSenderRecording: stopped MediaRecorder');
        }
        _senderRecorder = null;
        _senderSequence = 0;
    }

    /* ── Stop everything ────────────────────────────────────── */
    function stop() {
        stopSenderRecording();
        _stopDemoRecording();
        if (_shareStream) {
            _shareStream.getTracks().forEach(t => t.stop());
            _shareStream = null;
        }
        // Close all sharing peer connections (offerer side)
        _sharePCs.forEach(pc => { try { pc.close(); } catch {} });
        _sharePCs = [];
        // Close ALL viewer peer connections and clear stored streams
        _viewPCs.forEach(pc => { try { pc.close(); } catch {} });
        _viewPCs.clear();
        _viewStreams.clear();
    }

    return { configure, getOffer, reOffer, receiveOffer, setAnswer, reattachStream,
             stopView, getViewPcState, stop, startSenderRecording, stopSenderRecording };

})();

/* ══════════════════════════════════════════════════════════════
   webRtcHelper — legacy full-ICE-trickle helper (kept for
   any existing callers; new code should use screenShare above).
   ══════════════════════════════════════════════════════════════ */
window.webRtcHelper = (() => {

    let _localStream  = null;
    let _screenStream = null;
    let _peerConns    = {};

    const ICE_SERVERS = [{ urls: 'stun:stun.l.google.com:19302' }];

    async function startCamera(videoElementId, constraints) {
        try {
            _localStream = await navigator.mediaDevices.getUserMedia(constraints || { video: true, audio: true });
            const video  = document.getElementById(videoElementId);
            if (video) { video.srcObject = _localStream; video.play(); }
            return true;
        } catch (err) { console.warn('[WebRTC] startCamera:', err.message); return false; }
    }

    async function startScreenShare(videoElementId) {
        try {
            _screenStream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
            const video   = document.getElementById(videoElementId);
            if (video) { video.srcObject = _screenStream; video.play(); }
            _screenStream.getVideoTracks()[0].addEventListener('ended', () => stopScreenShare(videoElementId));
            return true;
        } catch (err) { console.warn('[WebRTC] startScreenShare:', err.message); return false; }
    }

    function stopScreenShare(videoElementId) {
        if (_screenStream) { _screenStream.getTracks().forEach(t => t.stop()); _screenStream = null; }
        const video = document.getElementById(videoElementId);
        if (video) video.srcObject = null;
    }

    function stopCamera() {
        if (_localStream) { _localStream.getTracks().forEach(t => t.stop()); _localStream = null; }
    }

    async function handleOffer(peerId, offerJson, videoElementId, dotnetRef) {
        const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
        _peerConns[peerId] = pc;
        pc.ontrack = (e) => {
            const video = document.getElementById(videoElementId);
            if (video && e.streams[0]) { video.srcObject = e.streams[0]; video.play(); }
        };
        pc.onicecandidate = (e) => {
            if (e.candidate && dotnetRef)
                dotnetRef.invokeMethodAsync('OnIceCandidate', peerId, JSON.stringify(e.candidate));
        };
        await pc.setRemoteDescription(new RTCSessionDescription(JSON.parse(offerJson)));
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        if (dotnetRef) dotnetRef.invokeMethodAsync('OnAnswer', peerId, JSON.stringify(answer));
    }

    async function handleAnswer(peerId, answerJson) {
        const pc = _peerConns[peerId];
        if (pc) await pc.setRemoteDescription(new RTCSessionDescription(JSON.parse(answerJson)));
    }

    async function handleIceCandidate(peerId, candidateJson) {
        const pc = _peerConns[peerId];
        if (pc) await pc.addIceCandidate(new RTCIceCandidate(JSON.parse(candidateJson)));
    }

    function closePeer(peerId) {
        const pc = _peerConns[peerId];
        if (pc) { pc.close(); delete _peerConns[peerId]; }
    }

    function setMicEnabled(enabled) {
        if (_localStream) _localStream.getAudioTracks().forEach(t => t.enabled = enabled);
    }

    function setCameraEnabled(enabled) {
        if (_localStream) _localStream.getVideoTracks().forEach(t => t.enabled = enabled);
    }

    async function requestPip(videoElementId) {
        const video = document.getElementById(videoElementId);
        if (video && document.pictureInPictureEnabled)
            try { await video.requestPictureInPicture(); } catch {}
    }

    return {
        startCamera, startScreenShare, stopScreenShare, stopCamera,
        handleOffer, handleAnswer, handleIceCandidate, closePeer,
        setMicEnabled, setCameraEnabled, requestPip
    };
})();

/* ══════════════════════════════════════════════════════════════
   agentRecorder — StandAlone screen recording for agents.
   Captures screen via MediaRecorder, POSTs each 1-second chunk
   to the server's /api/v1/collaboration/recordings/{id}/chunks
   endpoint so it can be saved to local disk + Azure Blob and
   broadcast to supervisors in real-time.
   ══════════════════════════════════════════════════════════════ */
window.agentRecorder = (() => {
    let _recorder  = null;
    let _recId     = null;
    let _chunkUrl  = null;
    let _token     = null;
    let _sequence  = 0;
    let _stream    = null;

    function _getBestMime() {
        const types = [
            'video/webm;codecs=vp9,opus',
            'video/webm;codecs=vp8,opus',
            'video/webm;codecs=vp9',
            'video/webm;codecs=vp8',
            'video/webm',
            'video/mp4'
        ];
        return types.find(t => MediaRecorder.isTypeSupported(t)) || '';
    }

    async function start(recordingId, chunkUploadUrl, authToken) {
        if (_recorder && _recorder.state !== 'inactive') {
            console.warn('[agentRecorder] Already recording — call stop() first');
            return false;
        }

        try {
            _stream = await navigator.mediaDevices.getDisplayMedia({
                video: { frameRate: 15, width: { ideal: 1920 }, height: { ideal: 1080 } },
                audio: true
            });
        } catch (err) {
            console.warn('[agentRecorder] Screen capture denied:', err.message);
            throw new Error('Screen capture was denied or cancelled: ' + err.message);
        }

        _recId    = recordingId;
        _chunkUrl = chunkUploadUrl;
        _token    = authToken;
        _sequence = 0;

        const mimeType = _getBestMime();
        _recorder = new MediaRecorder(_stream, mimeType ? { mimeType } : {});

        // When the user stops sharing via the browser's native "Stop sharing" button,
        // stop the recorder cleanly.
        _stream.getVideoTracks()[0].addEventListener('ended', () => stop());

        _recorder.ondataavailable = async (e) => {
            if (!e.data || e.data.size === 0) return;
            const seq = _sequence++;
            try {
                const arrayBuf = await e.data.arrayBuffer();
                await fetch(_chunkUrl, {
                    method:  'POST',
                    headers: {
                        'Content-Type':    'application/octet-stream',
                        'Authorization':   'Bearer ' + _token,
                        'X-Chunk-Sequence': String(seq)
                    },
                    body: arrayBuf
                });
            } catch (err) {
                console.warn('[agentRecorder] chunk upload failed (seq=' + seq + '):', err.message);
            }
        };

        _recorder.onstop = () => {
            console.info('[agentRecorder] Recording stopped. Total chunks:', _sequence);
        };

        _recorder.start(1000);   // 1-second timeslice
        console.info('[agentRecorder] Started recording', recordingId);
        return true;
    }

    async function stop() {
        if (_recorder && _recorder.state !== 'inactive') {
            _recorder.stop();
        }
        if (_stream) {
            _stream.getTracks().forEach(t => t.stop());
            _stream = null;
        }
        _recorder = null;
        console.info('[agentRecorder] Stopped');
    }

    return { start, stop };
})();

/* ══════════════════════════════════════════════════════════════
   supervisorRecording — attaches a live recording HTTP stream
   to a <video> element so the supervisor can watch.
   Uses the server's /api/v1/collaboration/recordings/{id}/stream
   endpoint which serves the growing .webm file with range support.
   ══════════════════════════════════════════════════════════════ */
window.supervisorRecording = (() => {

    function attachStream(videoElementId, streamUrl) {
        const video = document.getElementById(videoElementId);
        if (!video) {
            console.warn('[supervisorRecording] video element not found:', videoElementId);
            return;
        }
        // Point the video src at the server-side streaming endpoint.
        // The browser will range-fetch incrementally as the file grows.
        video.src = streamUrl;
        video.load();
        video.play().catch(() => {});
        console.info('[supervisorRecording] Attached stream', streamUrl, 'to', videoElementId);
    }

    return { attachStream };
})();
