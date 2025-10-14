import { NextRequest } from "next/server";
import { spawn } from "node:child_process";
import path from "node:path";
import fs from "node:fs";
import { getCliPath, getDownloadsDir } from "@/lib/cli";

function toSeconds(input: string): number | null {
  const s = input.trim();
  if (!s) return null;
  // numeric with suffix
  const suffix = s.match(/^\s*(\d+)(ms|s|m|h)\s*$/i);
  if (suffix) {
    const n = parseInt(suffix[1], 10);
    const u = suffix[2].toLowerCase();
    if (u === "ms") return Math.floor(n / 1000);
    if (u === "s") return n;
    if (u === "m") return n * 60;
    if (u === "h") return n * 3600;
  }
  // HH:MM:SS or MM:SS or SS
  const parts = s.split(":").map(p => p.trim()).filter(Boolean);
  if (parts.length === 1 && /^\d+$/.test(parts[0])) {
    return parseInt(parts[0], 10);
  }
  if (parts.length === 2 && parts.every(p => /^\d+$/.test(p))) {
    const mm = parseInt(parts[0], 10);
    const ss = parseInt(parts[1], 10);
    if (ss >= 60) return mm * 60 + ss; // allow sloppy 0:60 => 60s; will be normalized
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
  const pad = (n: number) => n.toString();
  // Allow single digit hours, but always two-digit mm:ss per CLI accepted pattern
  return `${hh}:${mm.toString().padStart(2, "0")}:${ss.toString().padStart(2, "0")}`;
}

export async function POST(req: NextRequest) {
  try {
    const body = await req.json();
    const id: string | undefined = body?.id?.toString()?.trim();
    const quality: string = (body?.quality?.toString() || "160p30").trim();
    const startRaw: string = (body?.start?.toString() || "0:00:00").trim();
    const endRaw: string = (body?.end?.toString() || "0:00:30").trim();
    const outputName: string = (body?.outputName?.toString() || "clip.mp4").trim();

    if (!id) {
      return new Response(JSON.stringify({ error: "Missing id" }), { status: 400 });
    }

    const cliPath = getCliPath();

    // Normalize and validate times
    let startSec = toSeconds(startRaw);
    let endSec = toSeconds(endRaw);
    if (startSec == null) startSec = 0;
    if (endSec == null) endSec = startSec + 30; // default 30s clip
    // If user entered something like 0:00:60, normalize by treating it as 60s
    if (endSec <= startSec) endSec = startSec + 30;
    const start = toHms(startSec);
    const end = toHms(endSec);
    const downloads = getDownloadsDir();
    const outputPath = path.resolve(downloads, outputName);

    // Ensure parent directory exists
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });

    const isVod = /^\d+$/.test(id);
    const args = isVod
      ? [
          "videodownload",
          "--id", id,
          "--quality", quality,
          "--beginning", start,
          "--ending", end,
          "-o", outputPath,
          "--log-level", "Status,Info,Warning,Error",
          "--banner", "false",
        ]
      : [
          // For clips trimming is not supported by the CLI downloader, download full clip
          "clipdownload",
          "--id", id,
          "--quality", quality,
          "-o", outputPath,
          "--log-level", "Status,Info,Warning,Error",
          "--banner", "false",
        ];

    const stdoutChunks: Buffer[] = [];
    const stderrChunks: Buffer[] = [];

    const child = spawn(cliPath, args, {
      cwd: process.cwd(),
    });

    child.stdout.on("data", (d) => stdoutChunks.push(Buffer.from(d)));
    child.stderr.on("data", (d) => stderrChunks.push(Buffer.from(d)));

    const exitCode: number = await new Promise((resolve) => {
      child.on("close", (code) => resolve(code ?? 0));
    });

    const stdout = Buffer.concat(stdoutChunks).toString("utf8");
    const stderr = Buffer.concat(stderrChunks).toString("utf8");

    if (exitCode !== 0) {
      return new Response(JSON.stringify({ error: "CLI error", exitCode, stdout, stderr, args }), { status: 500 });
    }

    const note = isVod ? undefined : "Times are ignored for clips; full clip downloaded.";
    return new Response(JSON.stringify({ outputPath, stdout, stderr, exitCode, note, args }), { status: 200 });
  } catch (e: any) {
    return new Response(JSON.stringify({ error: e?.message || String(e) }), { status: 500 });
  }
}
