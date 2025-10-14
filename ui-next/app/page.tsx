"use client";
import { useState } from "react";

export default function Page() {
  const [id, setId] = useState("");
  const [info, setInfo] = useState<string>("");
  const [quality, setQuality] = useState("160p30");
  const [start, setStart] = useState("0:00:00");
  const [end, setEnd] = useState("0:00:30");
  const [outputName, setOutputName] = useState("test_clip.mp4");
  const [downloading, setDownloading] = useState(false);
  const [log, setLog] = useState("");

  function toSeconds(input: string): number | null {
    const s = (input || "").trim();
    if (!s) return null;
    const suffix = s.match(/^\s*(\d+)(ms|s|m|h)\s*$/i);
    if (suffix) {
      const n = parseInt(suffix[1], 10);
      const u = suffix[2].toLowerCase();
      if (u === "ms") return Math.floor(n / 1000);
      if (u === "s") return n;
      if (u === "m") return n * 60;
      if (u === "h") return n * 3600;
    }
    const parts = s.split(":").map(p => p.trim()).filter(Boolean);
    if (parts.length === 1 && /^\d+$/.test(parts[0])) return parseInt(parts[0], 10);
    if (parts.length === 2 && parts.every(p => /^\d+$/.test(p))) {
      const mm = parseInt(parts[0], 10);
      const ss = parseInt(parts[1], 10);
      return mm * 60 + ss;
    }
    if (parts.length === 3 && parts.every(p => /^\d+$/.test(p))) {
      const hh = parseInt(parts[0], 10);
      const mm = parseInt(parts[1], 10);
      const ss = parseInt(parts[2], 10);
      return hh * 3600 + mm * 60 + ss;
    }
    return null;
  }

  function toHms(totalSeconds: number): string {
    if (totalSeconds < 0) totalSeconds = 0;
    const hh = Math.floor(totalSeconds / 3600);
    const mm = Math.floor((totalSeconds % 3600) / 60);
    const ss = Math.floor(totalSeconds % 60);
    return `${hh}:${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
  }

  const startSec = toSeconds(start) ?? 0;
  const endSecRaw = toSeconds(end);
  const endSec = endSecRaw == null ? startSec + 30 : endSecRaw;
  const normStart = toHms(startSec);
  const normEnd = toHms(Math.max(endSec, startSec));
  const durationSec = Math.max(0, (endSec - startSec));

  async function fetchInfo() {
    setInfo("");
    setLog("");
    const res = await fetch("/api/info", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ id }),
    });
    const data = await res.json();
    if (!res.ok) {
      setInfo("");
      setLog(data.error || "Failed to fetch info");
      return;
    }
    setInfo(data.stdout || "");
  }

  async function downloadClip() {
    setDownloading(true);
    setLog("");
    try {
      const res = await fetch("/api/download", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ id, quality, start: normStart, end: normEnd, outputName }),
      });
      const data = await res.json();
      if (!res.ok) {
        setLog(data.error || "Download failed");
      } else {
        setLog(`Saved to: ${data.outputPath}\n\n` + (data.stdout || ""));
      }
    } catch (e: any) {
      setLog(e?.message || String(e));
    } finally {
      setDownloading(false);
    }
  }

  return (
    <main>
      <section style={{ display: 'grid', gap: 12 }}>
        <label>
          <div>VOD/Clip URL or ID</div>
          <input
            value={id}
            onChange={(e) => setId(e.target.value)}
            placeholder="e.g. 892647155 or https://www.twitch.tv/videos/892647155"
            style={{ width: '100%', padding: 8, borderRadius: 6, border: '1px solid #333', background: '#18181b', color: '#fff' }}
          />
        </label>
        <div style={{ display: 'flex', gap: 8 }}>
          <button onClick={fetchInfo} style={{ padding: '8px 12px', borderRadius: 6, border: '1px solid #444', background: '#9146FF', color: '#fff' }}>Load Info</button>
        </div>
      </section>

      {info && (
        <section style={{ marginTop: 16 }}>
          <h3>Info (raw)</h3>
          <pre style={{ whiteSpace: 'pre-wrap', background: '#111', padding: 12, borderRadius: 6, border: '1px solid #222' }}>{info}</pre>
        </section>
      )}

      <section style={{ marginTop: 24, display: 'grid', gap: 12 }}>
        <h3>Clip Download</h3>
        <div style={{ display: 'flex', gap: 12 }}>
          <label>
            <div>Quality</div>
            <input value={quality} onChange={(e) => setQuality(e.target.value)} placeholder="e.g. 160p30, 360p30, 480p30, 720p30" style={{ padding: 8, borderRadius: 6, border: '1px solid #333', background: '#18181b', color: '#fff' }} />
          </label>
          <label>
            <div>Start (HH:MM:SS)</div>
            <input value={start} onChange={(e) => setStart(e.target.value)} placeholder="0:00:00" style={{ padding: 8, borderRadius: 6, border: '1px solid #333', background: '#18181b', color: '#fff' }} />
          </label>
          <label>
            <div>End (HH:MM:SS)</div>
            <input value={end} onChange={(e) => setEnd(e.target.value)} placeholder="0:00:30" style={{ padding: 8, borderRadius: 6, border: '1px solid #333', background: '#18181b', color: '#fff' }} />
          </label>
          <label>
            <div>Output file</div>
            <input value={outputName} onChange={(e) => setOutputName(e.target.value)} placeholder="clip.mp4" style={{ padding: 8, borderRadius: 6, border: '1px solid #333', background: '#18181b', color: '#fff' }} />
          </label>
        </div>
        <div style={{ background: '#111', border: '1px solid #222', borderRadius: 6, padding: 8 }}>
          <div>Normalized:</div>
          <div style={{ color: '#bdbdbd' }}>Start: {normStart} → End: {normEnd} — Duration: {durationSec}s</div>
        </div>
        <button disabled={downloading || !id || durationSec <= 0} onClick={downloadClip} style={{ padding: '8px 12px', borderRadius: 6, border: '1px solid #444', background: (downloading || !id || durationSec <= 0) ? '#333' : '#22c55e', color: '#000' }}>{downloading ? 'Downloading…' : 'Download Clip'}</button>
      </section>

      {log && (
        <section style={{ marginTop: 16 }}>
          <h3>Log</h3>
          <pre style={{ whiteSpace: 'pre-wrap', background: '#111', padding: 12, borderRadius: 6, border: '1px solid #222' }}>{log}</pre>
        </section>
      )}
    </main>
  );
}
