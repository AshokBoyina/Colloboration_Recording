namespace NICE.Platform.Collaboration.Infrastructure.Storage;

using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Streams recording chunks to the local disk in real-time.
/// Each recording gets its own FileStream kept open during the session.
///
/// Files are written to:
///   LocalStorage:RecordingsPath / yyyy-MM-dd / {recordingId}.mp4
///
/// Fragmented MP4 is the default container because <c>video/mp4</c> produced
/// by the browser's MediaRecorder is inherently seekable (each fragment carries
/// its own <c>tfdt</c> decode-time box).  WebM files recorded via MediaRecorder
/// lack a Cues index and therefore cannot be seeked.
///
/// On finalization a pure-C# fix-up is applied so the file plays in
/// Windows Media Player and other progressive-MP4 players:
///
/// • If the file is a <b>fragmented MP4</b> (it contains <c>moof</c> boxes — the
///   shape MediaRecorder produces) it is <b>de-fragmented</b>: every fragment's
///   <c>moof/traf/trun</c> run is parsed to recover per-sample size, duration,
///   composition offset and sync flag, a single self-contained <c>moov</c> with
///   complete sample tables (<c>stts</c>/<c>ctts</c>/<c>stsc</c>/<c>stsz</c>/
///   <c>stco</c>|<c>co64</c>/<c>stss</c>) is rebuilt, and the sample bytes are
///   copied into one contiguous <c>mdat</c>.  WMP cannot interpret <c>moof</c>
///   boxes, so this de-fragmentation is what makes the recording playable.
///
/// • If the file is already a <b>progressive MP4</b> but has <c>moov</c> after
///   <c>mdat</c>, a fast-start fix-up moves <c>moov</c> to the front and
///   increments every <c>stco</c>/<c>co64</c> entry by the size of the moved
///   <c>moov</c>.
///
/// No re-encoding occurs.  No external tools (no FFmpeg) are required — the byte
/// stream of every sample is copied verbatim, so the operation is lossless.
///
/// A date subfolder is created automatically on each call to InitAsync.
/// The full path is stored in <c>_paths</c> so GetLocalPath remains correct
/// even when a recording spans midnight into a new date folder.
/// </summary>
public sealed class LocalDiskStreamStore(
    IConfiguration                config,
    ILogger<LocalDiskStreamStore> logger) : IRecordingStreamStore, IAsyncDisposable
{
    private string RootPath =>
        config["LocalStorage:RecordingsPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "LocalStorage", "Recordings");

    // Open file streams keyed by recordingId
    private readonly ConcurrentDictionary<Guid, FileStream> _streams = new();

    // Full OS paths keyed by recordingId (avoids recomputing the date folder later)
    private readonly ConcurrentDictionary<Guid, string> _paths = new();

    /// <summary>Today's date subfolder name, e.g. "2026-06-15".</summary>
    private static string DateFolder => DateTime.Now.ToString("yyyy-MM-dd");

    // ── Init ─────────────────────────────────────────────────────────────────

    public Task<string> InitAsync(Guid recordingId, CancellationToken ct = default)
    {
        var dateFolder = DateFolder;                          // capture once per recording
        var dir        = Path.Combine(RootPath, dateFolder);
        Directory.CreateDirectory(dir);                       // no-op if it already exists

        var path   = Path.Combine(dir, $"{recordingId}.mp4");
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read,
                                   bufferSize: 65536, useAsync: true);
        _streams[recordingId] = stream;
        _paths[recordingId]   = path;

        logger.LogInformation("RecordingStream: opened {Path}", path);

        // Blob path stored in DB: recordings/yyyy-MM-dd/{id}.mp4
        return Task.FromResult($"recordings/{dateFolder}/{recordingId}.mp4");
    }

    // ── Append ───────────────────────────────────────────────────────────────

    public async Task AppendChunkAsync(Guid recordingId, byte[] chunk, CancellationToken ct = default)
    {
        if (!_streams.TryGetValue(recordingId, out var stream))
        {
            stream = EnsureStreamForAppend(recordingId);
            logger.LogWarning(
                "RecordingStream: recovered missing stream for {Id}; appending to {Path}",
                recordingId,
                _paths.TryGetValue(recordingId, out var p) ? p : "(unknown)");
        }
        await stream.WriteAsync(chunk, ct);
        await stream.FlushAsync(ct);
    }

    private FileStream EnsureStreamForAppend(Guid recordingId)
    {
        if (_streams.TryGetValue(recordingId, out var existing)) return existing;

        var path = _paths.TryGetValue(recordingId, out var knownPath)
            ? knownPath
            : Path.Combine(RootPath, DateFolder, $"{recordingId}.mp4");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        var candidate = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 65536, useAsync: true);

        var stream = _streams.GetOrAdd(recordingId, candidate);
        if (!ReferenceEquals(stream, candidate))
            candidate.Dispose();

        _paths[recordingId] = path;
        return stream;
    }

    // ── Finalize ─────────────────────────────────────────────────────────────

    public async Task<long> FinalizeAsync(Guid recordingId, CancellationToken ct = default)
    {
        if (!_streams.TryRemove(recordingId, out var stream))
            return 0;

        _paths.TryRemove(recordingId, out var rawPath);

        // Flush and close the raw fMP4 file written during recording.
        await stream.FlushAsync(ct);
        await stream.DisposeAsync();
        logger.LogInformation("RecordingStream: raw file closed for {Id}", recordingId);

        // Apply pure-C# progressive fix-up so the file plays in Windows Media Player.
        // Fragmented MP4 (MediaRecorder output) is de-fragmented; already-progressive
        // files get a moov front-start. Copy-only — no re-encode.
        var finalSize = rawPath is not null
            ? await ApplyProgressiveFixupAsync(rawPath, ct)
            : 0L;

        logger.LogInformation("RecordingStream: finalized {Id} ({Bytes} bytes)", recordingId, finalSize);
        return finalSize;
    }

    // ── GetLocalPath ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full OS path of the recording file, or null if not currently open.
    /// Uses the path captured at InitAsync time so the date folder is always correct.
    /// </summary>
    public string? GetLocalPath(Guid recordingId)
        => _paths.TryGetValue(recordingId, out var p) ? p : null;

    // ── Dispose ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, stream) in _streams)
            await stream.DisposeAsync();
        _streams.Clear();
        _paths.Clear();
    }

    // ── Progressive fix-up dispatcher ────────────────────────────────────────

    /// <summary>
    /// Decides how to make <paramref name="path"/> progressive-MP4 playable:
    /// fragmented files (containing <c>moof</c>) are de-fragmented; already
    /// progressive files get a <c>moov</c> front-start. Any failure falls back to
    /// leaving the raw file untouched and returns its current size.
    /// </summary>
    private async Task<long> ApplyProgressiveFixupAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return 0;

        List<Mp4Box> boxes;
        try
        {
            boxes = await ScanTopLevelBoxesAsync(path, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Fixup: box scan failed for {Path} — leaving as-is", path);
            return new FileInfo(path).Length;
        }

        var isFragmented = boxes.Any(b => b.Type == "moof");
        if (isFragmented)
        {
            try
            {
                return await DefragmentAsync(path, boxes, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Mp4Defrag: de-fragmentation failed for {Path} — leaving raw fMP4", path);
                return new FileInfo(path).Length;
            }
        }

        // Not fragmented — apply the classic moov front-start (no-op if already at front).
        return await ApplyMoovFrontstartAsync(path, ct);
    }

    // ── Pure C# fMP4 de-fragmentation ────────────────────────────────────────

    /// <summary>One decoded sample's location, timing and key-frame flag.</summary>
    private struct Sample
    {
        public long Offset;    // absolute byte offset of the sample's data in the source file
        public uint Size;      // sample size in bytes
        public uint Duration;  // sample duration in the track's media timescale
        public int  Cts;       // composition-time offset (signed; 0 when absent)
        public bool Sync;      // true if this is a sync sample (key-frame)
    }

    /// <summary>Per-track accumulator built from the init <c>moov</c> + all fragments.</summary>
    private sealed class TrackInfo
    {
        public uint           TrackId;
        public uint           MediaTimescale;
        public List<Sample>   Samples = new();
    }

    /// <summary>Box types that are pure containers for the purpose of moov parsing.</summary>
    private static readonly HashSet<string> ParseContainers = new()
        { "moov", "trak", "mdia", "minf", "stbl" };

    /// <summary>
    /// Converts a fragmented MP4 into a self-contained progressive MP4.
    ///
    /// Pass 1 walks every <c>moof</c> (each read into memory — fragments are tiny)
    /// and records per-sample metadata only; sample <i>data</i> is never buffered.
    /// Then a new <c>moov</c> with full sample tables is built, and pass 2
    /// stream-copies the sample bytes from their source offsets into a single
    /// contiguous <c>mdat</c>. Output layout: <c>ftyp · moov · mdat</c>.
    /// </summary>
    private async Task<long> DefragmentAsync(string path, List<Mp4Box> boxes, CancellationToken ct)
    {
        var moovBox = boxes.FirstOrDefault(b => b.Type == "moov");
        var ftypBox = boxes.FirstOrDefault(b => b.Type == "ftyp");

        if (moovBox is null)
        {
            logger.LogWarning("Mp4Defrag: no moov (init segment) in {Path} — cannot de-fragment", path);
            return new FileInfo(path).Length;
        }

        const long MaxMoovBytes = 256L * 1024 * 1024;
        if (moovBox.TotalSize is < 8 or > MaxMoovBytes)
        {
            logger.LogWarning("Mp4Defrag: moov size {Size} out of range for {Path} — skipping",
                moovBox.TotalSize, path);
            return new FileInfo(path).Length;
        }

        await using var src = new FileStream(path, FileMode.Open, FileAccess.Read,
                                             FileShare.Read, 1 << 16, true);

        // ── Read the init moov into memory and parse its box tree ────────────
        var moovBytes = new byte[moovBox.TotalSize];
        src.Seek(moovBox.Offset, SeekOrigin.Begin);
        await src.ReadExactlyAsync(moovBytes, ct);

        var moovTree = ParseTree(moovBytes, 0, moovBytes.Length);

        var mvhd = moovTree.Children!.First(c => c.Type == "mvhd");
        uint movieTimescale = mvhd.Payload![0] == 0
            ? ReadU32Be(mvhd.Payload, 12)
            : ReadU32Be(mvhd.Payload, 20);

        // ── trex defaults (mvex is parsed as a leaf; walk its raw children) ──
        var trex = new Dictionary<uint, (uint dur, uint size, uint flags)>();
        var mvex = moovTree.Children!.FirstOrDefault(c => c.Type == "mvex");
        if (mvex?.Payload is { } mvexPl)
        {
            int p = 0;
            while (p + 8 <= mvexPl.Length)
            {
                uint sz   = ReadU32Be(mvexPl, p);
                var  type = System.Text.Encoding.ASCII.GetString(mvexPl, p + 4, 4);
                if (sz < 8) break;
                if (type == "trex" && p + 32 <= mvexPl.Length)
                    trex[ReadU32Be(mvexPl, p + 12)] =
                        (ReadU32Be(mvexPl, p + 20), ReadU32Be(mvexPl, p + 24), ReadU32Be(mvexPl, p + 28));
                p += (int)sz;
            }
        }

        // ── Track templates (id, media timescale) in moov order ──────────────
        var tracks = new Dictionary<uint, TrackInfo>();
        var order  = new List<uint>();
        foreach (var trak in moovTree.Children!.Where(c => c.Type == "trak"))
        {
            var tkhd = trak.Children!.First(c => c.Type == "tkhd");
            uint tid = tkhd.Payload![0] == 0 ? ReadU32Be(tkhd.Payload, 12) : ReadU32Be(tkhd.Payload, 20);

            var mdia = trak.Children!.First(c => c.Type == "mdia");
            var mdhd = mdia.Children!.First(c => c.Type == "mdhd");
            uint mts = mdhd.Payload![0] == 0 ? ReadU32Be(mdhd.Payload, 12) : ReadU32Be(mdhd.Payload, 20);

            if (!tracks.ContainsKey(tid))
            {
                tracks[tid] = new TrackInfo { TrackId = tid, MediaTimescale = mts };
                order.Add(tid);
            }
        }

        // ── Pass 1: collect samples from every fragment ──────────────────────
        foreach (var mb in boxes.Where(b => b.Type == "moof"))
        {
            var mbytes = new byte[mb.TotalSize];
            src.Seek(mb.Offset, SeekOrigin.Begin);
            await src.ReadExactlyAsync(mbytes, ct);
            ParseMoof(mbytes, mb.Offset, trex, tracks);
        }

        long totalSamples = order.Sum(t => (long)tracks[t].Samples.Count);
        if (totalSamples == 0)
        {
            logger.LogWarning("Mp4Defrag: no samples recovered from fragments in {Path} — leaving raw", path);
            return new FileInfo(path).Length;
        }

        // ── ftyp bytes (copied verbatim, or a sane default) ──────────────────
        byte[] ftypBytes;
        if (ftypBox is not null)
        {
            ftypBytes = new byte[ftypBox.TotalSize];
            src.Seek(ftypBox.Offset, SeekOrigin.Begin);
            await src.ReadExactlyAsync(ftypBytes, ct);
        }
        else
        {
            ftypBytes = BuildDefaultFtyp();
        }

        long totalData = order.Sum(t => SumSizes(tracks[t].Samples));

        // ── Decide stco vs co64, then compute final chunk offsets ────────────
        var zeroOffsets = order.ToDictionary(t => t, _ => 0L);
        var moovProbe   = BuildMoov(moovBytes, tracks, movieTimescale, zeroOffsets, useCo64: false);

        long mdatHeader = (8 + totalData) > 0xFFFF_FFFFL ? 16 : 8;
        long baseOffset = ftypBytes.Length + moovProbe.Length + mdatHeader;
        bool useCo64    = (baseOffset + totalData) > 0xFFFF_FFFFL;

        if (useCo64)
        {
            moovProbe  = BuildMoov(moovBytes, tracks, movieTimescale, zeroOffsets, useCo64: true);
            baseOffset = ftypBytes.Length + moovProbe.Length + mdatHeader;
        }

        var offsets = new Dictionary<uint, long>();
        long cursor = baseOffset;
        foreach (var tid in order)
        {
            offsets[tid] = cursor;
            cursor += SumSizes(tracks[tid].Samples);
        }

        var moovFinal = BuildMoov(moovBytes, tracks, movieTimescale, offsets, useCo64);

        // The two builds have identical box/entry counts, so identical lengths.
        // If that ever fails to hold, abort rather than emit corrupt offsets.
        if (moovFinal.Length != moovProbe.Length)
        {
            logger.LogWarning(
                "Mp4Defrag: moov size drifted ({A} vs {B}) for {Path} — leaving raw",
                moovProbe.Length, moovFinal.Length, path);
            return new FileInfo(path).Length;
        }

        // ── Pass 2: write ftyp · moov · mdat to a temp file ──────────────────
        var tempPath = path + ".dftmp";
        try
        {
            await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                                                  FileShare.None, 1 << 20, true))
            {
                await dst.WriteAsync(ftypBytes, ct);
                await dst.WriteAsync(moovFinal, ct);

                long mdatSize = mdatHeader + totalData;
                var  mh       = new byte[mdatHeader];
                if (mdatHeader == 8)
                {
                    WriteU32Be(mh, 0, (uint)mdatSize);
                    System.Text.Encoding.ASCII.GetBytes("mdat", 0, 4, mh, 4);
                }
                else
                {
                    WriteU32Be(mh, 0, 1); // 1 => 64-bit largesize follows the type
                    System.Text.Encoding.ASCII.GetBytes("mdat", 0, 4, mh, 4);
                    WriteU64Be(mh, 8, (ulong)mdatSize);
                }
                await dst.WriteAsync(mh, ct);

                foreach (var tid in order)
                    await CopySamplesAsync(src, dst, tracks[tid].Samples, ct);

                await dst.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Defrag: write failed for {Path} — leaving original", path);
            try { File.Delete(tempPath); } catch { /* best effort */ }
            return new FileInfo(path).Length;
        }

        // ── Atomic swap ──────────────────────────────────────────────────────
        try
        {
            File.Delete(path);
            File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Defrag: swap failed for {Path}", path);
            try { File.Delete(tempPath); } catch { /* best effort */ }
            return new FileInfo(path).Length;
        }

        var finalSize = new FileInfo(path).Length;
        logger.LogInformation(
            "Mp4Defrag: complete — {Tracks} track(s), {Samples} samples, {Bytes} bytes, " +
            "progressive moov, WMP-compatible. {Path}",
            order.Count, totalSamples, finalSize, path);
        return finalSize;
    }

    // ── Fragment (moof) sample extraction ────────────────────────────────────

    /// <summary>
    /// Parses one in-memory <c>moof</c> box and appends its samples to the
    /// matching <see cref="TrackInfo"/>. Sample data offsets are resolved to
    /// absolute file positions using <paramref name="moofFileOffset"/>.
    /// </summary>
    private static void ParseMoof(
        byte[] d, long moofFileOffset,
        Dictionary<uint, (uint dur, uint size, uint flags)> trex,
        Dictionary<uint, TrackInfo> tracks)
    {
        foreach (var (type, off, _, size) in ScanMem(d, 8, d.Length))
        {
            if (type != "traf") continue;

            int tfhdOff = -1;
            var truns   = new List<(int off, int size)>();
            foreach (var (t, o, _, s) in ScanMem(d, off + 8, off + size))
            {
                if      (t == "tfhd") tfhdOff = o;
                else if (t == "trun") truns.Add((o, s));
            }
            if (tfhdOff < 0) continue;

            // ── tfhd ──────────────────────────────────────────────────────
            uint tf  = ReadU32Be(d, tfhdOff + 8) & 0x00FF_FFFF; // strip version byte
            uint tid = ReadU32Be(d, tfhdOff + 12);
            int  p   = tfhdOff + 16;

            long  baseOff   = -1;
            uint? defDurOpt = null, defSizeOpt = null, defFlagsOpt = null;

            if ((tf & 0x000001) != 0) { baseOff = (long)ReadU64Be(d, p); p += 8; } // base-data-offset
            if ((tf & 0x000002) != 0) { p += 4; }                                   // sample-description-index
            if ((tf & 0x000008) != 0) { defDurOpt   = ReadU32Be(d, p); p += 4; }    // default-sample-duration
            if ((tf & 0x000010) != 0) { defSizeOpt  = ReadU32Be(d, p); p += 4; }    // default-sample-size
            if ((tf & 0x000020) != 0) { defFlagsOpt = ReadU32Be(d, p); p += 4; }    // default-sample-flags
            // 0x020000 default-base-is-moof handled implicitly: when no explicit
            // base-data-offset is present the base is the moof's own file offset.
            if (baseOff < 0) baseOff = moofFileOffset;

            var tx       = trex.TryGetValue(tid, out var tv) ? tv : (dur: 0u, size: 0u, flags: 0u);
            uint defDur  = defDurOpt   ?? tx.dur;
            uint defSize = defSizeOpt  ?? tx.size;
            uint defFlag = defFlagsOpt ?? tx.flags;

            if (!tracks.TryGetValue(tid, out var track)) continue;

            // ── trun(s) ───────────────────────────────────────────────────
            long runCursor = baseOff;
            foreach (var (to, _) in truns)
            {
                uint trf   = ReadU32Be(d, to + 8) & 0x00FF_FFFF;
                int  rp    = to + 12;
                uint count = ReadU32Be(d, rp); rp += 4;

                int?  dataOff    = null;
                uint? firstFlags = null;
                if ((trf & 0x000001) != 0) { dataOff    = (int)ReadU32Be(d, rp); rp += 4; } // signed
                if ((trf & 0x000004) != 0) { firstFlags = ReadU32Be(d, rp);      rp += 4; }

                long pos = dataOff.HasValue ? baseOff + dataOff.Value : runCursor;

                for (uint i = 0; i < count; i++)
                {
                    uint sDur = defDur, sSize = defSize, sFlags = defFlag;
                    int  sCts = 0;

                    if ((trf & 0x000100) != 0) { sDur   = ReadU32Be(d, rp);      rp += 4; }
                    if ((trf & 0x000200) != 0) { sSize  = ReadU32Be(d, rp);      rp += 4; }
                    if ((trf & 0x000400) != 0) { sFlags = ReadU32Be(d, rp);      rp += 4; }
                    if ((trf & 0x000800) != 0) { sCts   = (int)ReadU32Be(d, rp); rp += 4; }

                    if (i == 0 && firstFlags.HasValue) sFlags = firstFlags.Value;
                    bool sync = (sFlags & 0x0001_0000u) == 0; // sample_is_non_sync_sample bit

                    track.Samples.Add(new Sample
                    {
                        Offset   = pos,
                        Size     = sSize,
                        Duration = sDur,
                        Cts      = sCts,
                        Sync     = sync
                    });
                    pos += sSize;
                }
                runCursor = pos;
            }
        }
    }

    // ── moov (re)builder ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fresh, self-contained <c>moov</c> from the init-segment template:
    /// drops <c>mvex</c>/<c>udta</c>, rewrites each track's <c>stbl</c> with full
    /// sample tables and patches <c>mvhd</c>/<c>tkhd</c>/<c>mdhd</c> durations.
    /// Re-parses the template each call so repeated builds never see mutations.
    /// </summary>
    private static byte[] BuildMoov(
        byte[] moovBytes, Dictionary<uint, TrackInfo> tracks,
        uint movieTimescale, Dictionary<uint, long> chunkOffsets, bool useCo64)
    {
        var tree = ParseTree(moovBytes, 0, moovBytes.Length);
        var mvhd = tree.Children!.First(c => c.Type == "mvhd");

        var  newChildren = new List<BoxNode>();
        long movieDuration = 0;

        foreach (var c in tree.Children!)
        {
            if (c.Type is "mvex" or "udta") continue;   // fragment defaults / user data: drop
            if (c.Type != "trak") { newChildren.Add(c); continue; }

            var trak = c;
            var tkhd = trak.Children!.First(x => x.Type == "tkhd");
            uint tid = tkhd.Payload![0] == 0 ? ReadU32Be(tkhd.Payload, 12) : ReadU32Be(tkhd.Payload, 20);
            var T    = tracks[tid];

            long mediaDuration = 0;
            foreach (var s in T.Samples) mediaDuration += s.Duration;

            // Patch mdhd (media-timescale duration)
            var mdia = trak.Children!.First(x => x.Type == "mdia");
            var mdhd = mdia.Children!.First(x => x.Type == "mdhd");
            if (mdhd.Payload![0] == 0) WriteU32Be(mdhd.Payload, 16, (uint)mediaDuration);
            else                       WriteU64Be(mdhd.Payload, 24, (ulong)mediaDuration);

            // Patch tkhd (movie-timescale duration)
            long movDuration = T.MediaTimescale != 0
                ? (long)((double)mediaDuration * movieTimescale / T.MediaTimescale)
                : 0;
            if (movDuration > movieDuration) movieDuration = movDuration;
            if (tkhd.Payload[0] == 0) WriteU32Be(tkhd.Payload, 20, (uint)movDuration);
            else                      WriteU64Be(tkhd.Payload, 28, (ulong)movDuration);

            // Replace stbl tables (keep stsd, rebuild the rest)
            var minf = mdia.Children!.First(x => x.Type == "minf");
            var stbl = minf.Children!.First(x => x.Type == "stbl");
            var stsd = stbl.Children!.First(x => x.Type == "stsd");
            stbl.Children = BuildStblChildren(stsd, T.Samples, chunkOffsets[tid], useCo64);

            newChildren.Add(trak);
        }

        // Patch mvhd (movie-timescale duration)
        if (mvhd.Payload![0] == 0) WriteU32Be(mvhd.Payload, 16, (uint)movieDuration);
        else                       WriteU64Be(mvhd.Payload, 24, (ulong)movieDuration);

        tree.Children = newChildren;
        return Serialize(tree);
    }

    /// <summary>
    /// Builds the replacement <c>stbl</c> children: <c>stsd</c> (kept) followed by
    /// freshly synthesised <c>stts</c>, optional <c>ctts</c>, <c>stsc</c>,
    /// <c>stsz</c>, <c>stco</c>/<c>co64</c> and optional <c>stss</c>.
    /// All samples are placed in a single chunk per track.
    /// </summary>
    private static List<BoxNode> BuildStblChildren(
        BoxNode stsd, List<Sample> samples, long chunkOffset, bool useCo64)
    {
        var kids = new List<BoxNode> { stsd };

        // stts — run-length (sample_count, sample_delta)
        var stts = new List<byte>();
        PutU32(stts, 0); // version + flags
        var sttsRuns = new List<(uint count, uint delta)>();
        foreach (var s in samples)
        {
            if (sttsRuns.Count > 0 && sttsRuns[^1].delta == s.Duration)
                sttsRuns[^1] = (sttsRuns[^1].count + 1, s.Duration);
            else
                sttsRuns.Add((1, s.Duration));
        }
        PutU32(stts, (uint)sttsRuns.Count);
        foreach (var (count, delta) in sttsRuns) { PutU32(stts, count); PutU32(stts, delta); }
        kids.Add(new BoxNode { Type = "stts", Payload = stts.ToArray() });

        // ctts — only when at least one composition offset is non-zero (version 1, signed)
        bool anyCts = false;
        foreach (var s in samples) if (s.Cts != 0) { anyCts = true; break; }
        if (anyCts)
        {
            var ctts = new List<byte> { 1, 0, 0, 0 }; // version 1 + flags
            var cttsRuns = new List<(uint count, int off)>();
            foreach (var s in samples)
            {
                if (cttsRuns.Count > 0 && cttsRuns[^1].off == s.Cts)
                    cttsRuns[^1] = (cttsRuns[^1].count + 1, s.Cts);
                else
                    cttsRuns.Add((1, s.Cts));
            }
            PutU32(ctts, (uint)cttsRuns.Count);
            foreach (var (count, offv) in cttsRuns) { PutU32(ctts, count); PutU32(ctts, (uint)offv); }
            kids.Add(new BoxNode { Type = "ctts", Payload = ctts.ToArray() });
        }

        // stsz — explicit per-sample sizes (sample_size field = 0)
        var stsz = new List<byte>();
        PutU32(stsz, 0);                    // version + flags
        PutU32(stsz, 0);                    // uniform sample_size = 0 => sizes follow
        PutU32(stsz, (uint)samples.Count);  // sample_count
        foreach (var s in samples) PutU32(stsz, s.Size);
        kids.Add(new BoxNode { Type = "stsz", Payload = stsz.ToArray() });

        // stsc — one chunk holding every sample (or zero entries for an empty track)
        var stsc = new List<byte>();
        PutU32(stsc, 0);
        if (samples.Count > 0)
        {
            PutU32(stsc, 1);                   // entry_count
            PutU32(stsc, 1);                   // first_chunk
            PutU32(stsc, (uint)samples.Count); // samples_per_chunk
            PutU32(stsc, 1);                   // sample_description_index
        }
        else
        {
            PutU32(stsc, 0);
        }
        kids.Add(new BoxNode { Type = "stsc", Payload = stsc.ToArray() });

        // stco / co64 — one chunk offset per track
        var stco = new List<byte>();
        PutU32(stco, 0);
        if (samples.Count > 0)
        {
            PutU32(stco, 1);
            if (useCo64) PutU64(stco, (ulong)chunkOffset);
            else         PutU32(stco, (uint)chunkOffset);
        }
        else
        {
            PutU32(stco, 0);
        }
        kids.Add(new BoxNode { Type = useCo64 ? "co64" : "stco", Payload = stco.ToArray() });

        // stss — sync-sample table; omitted when every sample is a sync sample
        var syncs = new List<uint>();
        for (int i = 0; i < samples.Count; i++) if (samples[i].Sync) syncs.Add((uint)(i + 1));
        if (syncs.Count > 0 && syncs.Count != samples.Count)
        {
            var stss = new List<byte>();
            PutU32(stss, 0);
            PutU32(stss, (uint)syncs.Count);
            foreach (var x in syncs) PutU32(stss, x);
            kids.Add(new BoxNode { Type = "stss", Payload = stss.ToArray() });
        }

        return kids;
    }

    // ── In-memory box tree (parse / serialize) ───────────────────────────────

    /// <summary>A parsed MP4 box: either a container (Children) or a leaf (Payload).</summary>
    private sealed class BoxNode
    {
        public required string     Type;
        public byte[]?             Payload;   // leaf content (excludes the 8-byte header)
        public List<BoxNode>?      Children;  // container children
    }

    /// <summary>
    /// Parses a box (and, for known pure containers, its descendants) out of an
    /// in-memory buffer. Non-container boxes are kept verbatim as leaves.
    /// </summary>
    private static BoxNode ParseTree(byte[] d, int off, int size)
    {
        var type = System.Text.Encoding.ASCII.GetString(d, off + 4, 4);
        if (ParseContainers.Contains(type))
        {
            var kids = new List<BoxNode>();
            int p = off + 8, end = off + size;
            while (p + 8 <= end)
            {
                uint s32   = ReadU32Be(d, p);
                int  hs    = 8;
                long bsize = s32;
                if (s32 == 1) { if (p + 16 > end) break; bsize = (long)ReadU64Be(d, p + 8); hs = 16; }
                else if (s32 == 0) { bsize = end - p; }
                if (bsize < hs || p + bsize > end) break;
                kids.Add(ParseTree(d, p, (int)bsize));
                p += (int)bsize;
            }
            return new BoxNode { Type = type, Children = kids };
        }

        return new BoxNode { Type = type, Payload = d[(off + 8)..(off + size)] };
    }

    /// <summary>Serialises a box tree to a byte array, computing every box size.</summary>
    private static byte[] Serialize(BoxNode node)
    {
        using var ms = new MemoryStream();
        SerializeInto(node, ms);
        return ms.ToArray();
    }

    private static void SerializeInto(BoxNode n, MemoryStream ms)
    {
        long sizePos = ms.Position;
        ms.Write(stackalloc byte[4]);                                   // size placeholder
        ms.Write(System.Text.Encoding.ASCII.GetBytes(n.Type), 0, 4);    // box type

        if (n.Children is not null)
            foreach (var c in n.Children) SerializeInto(c, ms);
        else if (n.Payload is not null)
            ms.Write(n.Payload, 0, n.Payload.Length);

        long endPos = ms.Position;
        Span<byte> sz = stackalloc byte[4];
        WriteU32BeSpan(sz, (uint)(endPos - sizePos));
        ms.Position = sizePos;
        ms.Write(sz);
        ms.Position = endPos;
    }

    // ── In-memory box scanner ────────────────────────────────────────────────

    /// <summary>
    /// Enumerates sibling boxes within <paramref name="d"/> between
    /// <paramref name="start"/> and <paramref name="end"/> (header offsets only).
    /// </summary>
    private static IEnumerable<(string type, int off, int hdrSize, int size)>
        ScanMem(byte[] d, int start, int end)
    {
        int p = start;
        while (p + 8 <= end)
        {
            uint s32  = ReadU32Be(d, p);
            var  type = System.Text.Encoding.ASCII.GetString(d, p + 4, 4);
            int  hs   = 8;
            long size = s32;
            if (s32 == 1) { if (p + 16 > end) yield break; size = (long)ReadU64Be(d, p + 8); hs = 16; }
            else if (s32 == 0) { size = end - p; }
            if (size < hs || p + size > end) yield break;
            yield return (type, p, hs, (int)size);
            p += (int)size;
        }
    }

    // ── Sample-data copy (pass 2) ────────────────────────────────────────────

    /// <summary>
    /// Stream-copies a track's samples into the destination, coalescing samples
    /// that are physically contiguous in the source to minimise seeks/reads.
    /// </summary>
    private static async Task CopySamplesAsync(
        FileStream src, FileStream dst, List<Sample> samples, CancellationToken ct)
    {
        var buf = new byte[1 << 20];
        int i = 0;
        while (i < samples.Count)
        {
            long start = samples[i].Offset;
            long len   = samples[i].Size;
            int  j     = i + 1;
            while (j < samples.Count && samples[j].Offset == start + len)
            {
                len += samples[j].Size;
                j++;
            }

            src.Seek(start, SeekOrigin.Begin);
            long remaining = len;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buf.Length, remaining);
                int read   = await src.ReadAsync(buf.AsMemory(0, toRead), ct);
                if (read == 0) throw new EndOfStreamException("Mp4Defrag: sample data truncated");
                await dst.WriteAsync(buf.AsMemory(0, read), ct);
                remaining -= read;
            }
            i = j;
        }
    }

    // ── Small helpers ────────────────────────────────────────────────────────

    private static long SumSizes(List<Sample> samples)
    {
        long sum = 0;
        foreach (var s in samples) sum += s.Size;
        return sum;
    }

    private static byte[] BuildDefaultFtyp()
    {
        // ftyp: major_brand 'isom', minor_version 0x200, compatible 'isom','iso2','mp41'
        var p = new List<byte>();
        PutAscii(p, "isom");
        PutU32(p, 0x200);
        PutAscii(p, "isom");
        PutAscii(p, "iso2");
        PutAscii(p, "mp41");
        return Serialize(new BoxNode { Type = "ftyp", Payload = p.ToArray() });
    }

    private static void PutAscii(List<byte> l, string s)
    {
        foreach (var ch in s) l.Add((byte)ch);
    }

    private static void PutU32(List<byte> l, uint v)
    {
        l.Add((byte)(v >> 24)); l.Add((byte)(v >> 16)); l.Add((byte)(v >> 8)); l.Add((byte)v);
    }

    private static void PutU64(List<byte> l, ulong v)
    {
        for (int s = 56; s >= 0; s -= 8) l.Add((byte)(v >> s));
    }

    private static void WriteU32BeSpan(Span<byte> b, uint v)
    {
        b[0] = (byte)(v >> 24); b[1] = (byte)(v >> 16); b[2] = (byte)(v >> 8); b[3] = (byte)v;
    }

    // ── Pure C# MP4 moov front-start ─────────────────────────────────────────

    /// <summary>
    /// Moves the MP4 <c>moov</c> atom to the front of the file using pure C#.
    ///
    /// Algorithm:
    ///   1. Scan top-level box headers (small sequential reads only).
    ///   2. If <c>moov</c> already precedes the first <c>mdat</c>, return immediately.
    ///   3. Read only the <c>moov</c> bytes into memory.
    ///   4. Patch every <c>stco</c>/<c>co64</c> entry by adding <c>sizeof(moov)</c>
    ///      (the amount by which <c>mdat</c> will shift forward).
    ///   5. Write a temp file: [prefix boxes] → [patched moov] → [mdat + trailing boxes].
    ///   6. Atomically replace the original with the temp file.
    ///
    /// Returns the final byte size of the (possibly rewritten) file.
    /// Falls back gracefully to the original size on any error.
    /// </summary>
    private async Task<long> ApplyMoovFrontstartAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return 0;

        // ── Step 1: locate top-level boxes ───────────────────────────────────
        List<Mp4Box> boxes;
        try
        {
            boxes = await ScanTopLevelBoxesAsync(path, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Faststart: box scan failed for {Path} — leaving as-is", path);
            return new FileInfo(path).Length;
        }

        var moovBox   = boxes.FirstOrDefault(b => b.Type == "moov");
        var firstMdat = boxes.FirstOrDefault(b => b.Type == "mdat");

        if (moovBox is null)
        {
            logger.LogWarning("Mp4Faststart: no moov box found in {Path}", path);
            return new FileInfo(path).Length;
        }

        // Already fast-start: moov before first mdat (or file has no mdat at all).
        if (firstMdat is null || moovBox.Offset < firstMdat.Offset)
        {
            logger.LogInformation("Mp4Faststart: moov already at front for {Path}", path);
            return new FileInfo(path).Length;
        }

        logger.LogInformation(
            "Mp4Faststart: moving moov from {MoovOff} to before mdat at {MdatOff} — {Path}",
            moovBox.Offset, firstMdat.Offset, path);

        // ── Step 2: sanity check moov size ────────────────────────────────────
        const long MaxMoovBytes = 256L * 1024 * 1024; // 256 MB — generous upper bound
        if (moovBox.TotalSize > MaxMoovBytes)
        {
            logger.LogWarning(
                "Mp4Faststart: moov is {Size} bytes (> 256 MB limit) — skipping fix-up for {Path}",
                moovBox.TotalSize, path);
            return new FileInfo(path).Length;
        }

        // ── Step 3: read moov into memory ─────────────────────────────────────
        byte[] moovBytes;
        try
        {
            moovBytes = new byte[moovBox.TotalSize];
            await using var rdr = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                 FileShare.Read, 65536, true);
            rdr.Seek(moovBox.Offset, SeekOrigin.Begin);
            await rdr.ReadExactlyAsync(moovBytes, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Faststart: could not read moov from {Path}", path);
            return new FileInfo(path).Length;
        }

        // ── Step 4: patch stco/co64 offsets ──────────────────────────────────
        // mdat (and everything after the prefix) shifts right by sizeof(moov).
        long delta = moovBox.TotalSize;
        PatchChunkOffsets(moovBytes, delta);

        // ── Step 5: write reordered temp file ────────────────────────────────
        // New layout:
        //   [boxes with offset < firstMdat.Offset, excl. moov]  — prefix (e.g. ftyp)
        //   [patched moov]
        //   [boxes with offset >= firstMdat.Offset, excl. moov] — mdat + trailing
        var tempPath = path + ".fstmp";
        try
        {
            await using var src = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                 FileShare.Read, 65536, true);
            await using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                                                 FileShare.None, 65536, true);

            // Prefix
            foreach (var box in boxes.Where(b => b.Offset < firstMdat.Offset && b.Type != "moov")
                                     .OrderBy(b => b.Offset))
            {
                src.Seek(box.Offset, SeekOrigin.Begin);
                await CopyExactAsync(src, dst, box.TotalSize, ct);
            }

            // Patched moov
            await dst.WriteAsync(moovBytes, ct);

            // mdat and trailing boxes (in original order, moov excluded)
            foreach (var box in boxes.Where(b => b.Offset >= firstMdat.Offset && b.Type != "moov")
                                     .OrderBy(b => b.Offset))
            {
                src.Seek(box.Offset, SeekOrigin.Begin);
                await CopyExactAsync(src, dst, box.TotalSize, ct);
            }

            await dst.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Faststart: write failed for {Path} — leaving original", path);
            try { File.Delete(tempPath); } catch { /* best effort */ }
            return new FileInfo(path).Length;
        }

        // ── Step 6: atomic swap ───────────────────────────────────────────────
        try
        {
            File.Delete(path);
            File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mp4Faststart: swap failed for {Path}", path);
            return new FileInfo(path).Length;
        }

        var finalSize = new FileInfo(path).Length;
        logger.LogInformation(
            "Mp4Faststart: complete — {Bytes} bytes, moov now at front, file is WMP-compatible.", finalSize);
        return finalSize;
    }

    // ── MP4 top-level box scanner ─────────────────────────────────────────────

    private sealed record Mp4Box(string Type, long Offset, long HeaderSize, long DataSize)
    {
        public long TotalSize => HeaderSize + DataSize;
    }

    /// <summary>
    /// Reads only the 8- or 16-byte header of each top-level box to discover
    /// their types, positions, and sizes.  Does not read box data.
    /// </summary>
    private static async Task<List<Mp4Box>> ScanTopLevelBoxesAsync(string path, CancellationToken ct)
    {
        var boxes    = new List<Mp4Box>();
        var hdr      = new byte[16];
        var fileSize = new FileInfo(path).Length;

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                            FileShare.Read, 4096, true);
        while (fs.Position < fileSize)
        {
            var boxStart = fs.Position;
            var read     = await fs.ReadAsync(hdr.AsMemory(0, 8), ct);
            if (read < 8) break;

            var size32 = ReadU32Be(hdr, 0);
            var type   = System.Text.Encoding.ASCII.GetString(hdr, 4, 4);

            long hdrSize, dataSize;
            if (size32 == 1)
            {
                // 64-bit extended size in next 8 bytes
                read = await fs.ReadAsync(hdr.AsMemory(0, 8), ct);
                if (read < 8) break;
                var size64 = ReadU64Be(hdr, 0);
                hdrSize  = 16;
                dataSize = (long)size64 - 16;
            }
            else if (size32 == 0)
            {
                // Box extends to end of file
                hdrSize  = 8;
                dataSize = fileSize - boxStart - 8;
            }
            else
            {
                hdrSize  = 8;
                dataSize = (long)size32 - 8;
            }

            if (dataSize < 0) break; // Malformed file

            boxes.Add(new Mp4Box(type, boxStart, hdrSize, dataSize));

            // Seek past this box's data to the next box header
            fs.Seek(boxStart + hdrSize + dataSize, SeekOrigin.Begin);
        }

        return boxes;
    }

    // ── stco / co64 offset patching ───────────────────────────────────────────

    /// <summary>
    /// Recursively walks the in-memory <c>moov</c> bytes and adds
    /// <paramref name="delta"/> to every entry in each <c>stco</c> and
    /// <c>co64</c> box, adjusting absolute file offsets to account for
    /// <c>moov</c> being inserted before <c>mdat</c>.
    /// </summary>
    private static void PatchChunkOffsets(byte[] moovData, long delta)
        => WalkAndPatch(moovData, 0, moovData.Length, delta);

    private static void WalkAndPatch(byte[] data, int start, int end, long delta)
    {
        int pos = start;
        while (pos + 8 <= end)
        {
            var size32 = ReadU32Be(data, pos);
            var type   = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);

            int  hdrSize;
            long boxDataLen;

            if (size32 == 1 && pos + 16 <= end)
            {
                var size64 = ReadU64Be(data, pos + 8);
                hdrSize    = 16;
                boxDataLen = (long)size64 - 16;
            }
            else if (size32 == 0)
            {
                hdrSize    = 8;
                boxDataLen = end - (pos + 8);
            }
            else
            {
                hdrSize    = 8;
                boxDataLen = (long)size32 - 8;
            }

            if (boxDataLen < 0) break;

            int dataStart = pos + hdrSize;
            int dataEnd   = (int)Math.Min((long)dataStart + boxDataLen, end);

            switch (type)
            {
                case "stco":
                    // FullBox: version(1) + flags(3) + entry_count(4) + uint32[entry_count]
                    if (dataStart + 8 <= dataEnd)
                    {
                        int count = (int)ReadU32Be(data, dataStart + 4);
                        for (int i = 0; i < count; i++)
                        {
                            int ep = dataStart + 8 + i * 4;
                            if (ep + 4 > dataEnd) break;
                            var newVal = (uint)((long)ReadU32Be(data, ep) + delta);
                            WriteU32Be(data, ep, newVal);
                        }
                    }
                    break;

                case "co64":
                    // FullBox: version(1) + flags(3) + entry_count(4) + uint64[entry_count]
                    if (dataStart + 8 <= dataEnd)
                    {
                        int count = (int)ReadU32Be(data, dataStart + 4);
                        for (int i = 0; i < count; i++)
                        {
                            int ep = dataStart + 8 + i * 8;
                            if (ep + 8 > dataEnd) break;
                            var newVal = (ulong)((long)ReadU64Be(data, ep) + delta);
                            WriteU64Be(data, ep, newVal);
                        }
                    }
                    break;

                // Container boxes — descend
                case "moov": case "trak": case "mdia": case "minf":
                case "stbl": case "edts": case "udta": case "meta":
                case "dinf": case "mvex": case "moof": case "traf":
                    WalkAndPatch(data, dataStart, dataEnd, delta);
                    break;
            }

            pos = dataStart + (int)Math.Min(boxDataLen, dataEnd - dataStart);
        }
    }

    // ── Streaming copy helper ─────────────────────────────────────────────────

    private static async Task CopyExactAsync(
        FileStream src, FileStream dst, long byteCount, CancellationToken ct)
    {
        var buf       = new byte[65536];
        long remaining = byteCount;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buf.Length, remaining);
            int read   = await src.ReadAsync(buf.AsMemory(0, toRead), ct);
            if (read == 0) break;
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    // ── Big-endian integer helpers ─────────────────────────────────────────────

    private static uint ReadU32Be(byte[] b, int o)
        => ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    private static ulong ReadU64Be(byte[] b, int o)
        => ((ulong)b[o] << 56) | ((ulong)b[o + 1] << 48) | ((ulong)b[o + 2] << 40) |
           ((ulong)b[o + 3] << 32) | ((ulong)b[o + 4] << 24) | ((ulong)b[o + 5] << 16) |
           ((ulong)b[o + 6] << 8) | b[o + 7];

    private static void WriteU32Be(byte[] b, int o, uint v)
    {
        b[o]     = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void WriteU64Be(byte[] b, int o, ulong v)
    {
        b[o]     = (byte)(v >> 56);
        b[o + 1] = (byte)(v >> 48);
        b[o + 2] = (byte)(v >> 40);
        b[o + 3] = (byte)(v >> 32);
        b[o + 4] = (byte)(v >> 24);
        b[o + 5] = (byte)(v >> 16);
        b[o + 6] = (byte)(v >> 8);
        b[o + 7] = (byte)v;
    }
}
