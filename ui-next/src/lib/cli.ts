import path from "node:path";
import fs from "node:fs";

export function getCliPath(): string {
  const fromEnv = process.env.TWITCHDL_CLI?.trim();
  if (fromEnv && fs.existsSync(fromEnv)) return fromEnv;
  // Default to sibling folder TwitchDownloaderCLI-MacOSArm64
  const guess = path.resolve(process.cwd(), "..", "TwitchDownloaderCLI-MacOSArm64", process.platform === "win32" ? "TwitchDownloaderCLI.exe" : "TwitchDownloaderCLI");
  return guess;
}

export function getDownloadsDir(): string {
  const d = path.resolve(process.cwd(), "downloads");
  if (!fs.existsSync(d)) fs.mkdirSync(d, { recursive: true });
  return d;
}
