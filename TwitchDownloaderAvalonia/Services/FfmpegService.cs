using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace TwitchDownloaderAvalonia.Services;

public sealed class FfmpegService
{
    private static readonly string ExecutableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    public async Task<string> GetPreferredFfmpegPathAsync()
    {
        // 1) Local next to app
        var local = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        if (File.Exists(local)) return local;

        // 2) On PATH
        var onPath = ResolveOnPath(ExecutableName);
        if (onPath is not null) return onPath;

        // 3) Download locally
        return await DownloadLatestAsync();
    }

    public async Task<string> DownloadLatestAsync()
    {
        var appDir = AppContext.BaseDirectory;
        var cwd = Environment.CurrentDirectory;
        var appTarget = Path.Combine(appDir, ExecutableName);
        var cwdTarget = Path.Combine(cwd, ExecutableName);
        if (File.Exists(appTarget)) return appTarget;
        if (File.Exists(cwdTarget)) return cwdTarget;

        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

        // Xabe usually places ffmpeg in the current working directory
        var found = File.Exists(cwdTarget) ? cwdTarget : (File.Exists(appTarget) ? appTarget : null);
        var target = found ?? cwdTarget;

        // ensure executable bit
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TryChmodX(target);
        }

        return target;
    }

    private static string? ResolveOnPath(string exe)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            var candidate = Path.Combine(p, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void TryChmodX(string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                ArgumentList = { "+x", file },
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            proc?.WaitForExit(5000);
        }
        catch { /* ignore */ }
    }
}
