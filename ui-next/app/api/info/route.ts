import { NextRequest } from "next/server";
import { spawn } from "node:child_process";
import { getCliPath } from "@/lib/cli";

export async function POST(req: NextRequest) {
  try {
    const body = await req.json();
    const id: string | undefined = body?.id?.toString()?.trim();
    if (!id) {
      return new Response(JSON.stringify({ error: "Missing id" }), { status: 400 });
    }

    const cliPath = getCliPath();

    const stdoutChunks: Buffer[] = [];
    const stderrChunks: Buffer[] = [];

    const child = spawn(cliPath, ["info", "--id", id], {
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
      return new Response(JSON.stringify({ error: "CLI error", exitCode, stdout, stderr }), { status: 500 });
    }

    return new Response(JSON.stringify({ stdout, stderr, exitCode }), { status: 200 });
  } catch (e: any) {
    return new Response(JSON.stringify({ error: e?.message || String(e) }), { status: 500 });
  }
}
