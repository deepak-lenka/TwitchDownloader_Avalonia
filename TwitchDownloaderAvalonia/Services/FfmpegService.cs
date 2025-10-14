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

        // 3) Download to user-writable folder
        return await DownloadLatestAsync();
    }

    public async Task<string> DownloadLatestAsync()
    {
        // Use a per-user writable directory (no writes inside .app or root)
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(homeDir))
            homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var toolsDir = Path.Combine(homeDir, "TwitchDownloader", "ffmpeg");
        Directory.CreateDirectory(toolsDir);
        var target = Path.Combine(toolsDir, ExecutableName);
        if (File.Exists(target))
        {
            TryChmodX(target);
            return target;
        }

        // Tell Xabe where to place ffmpeg and download
        FFmpeg.SetExecutablesPath(toolsDir);
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

        // Xabe places ffmpeg into toolsDir
        if (!File.Exists(target))
        {
            // some versions add platform subfolders; try to locate the binary
            var guess = ResolveOnPathFromDir(toolsDir, ExecutableName);
            if (guess is not null) target = guess;
        }

        // ensure executable bit
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TryChmodX(target);
        }

        return target;
    }

    private static string? ResolveOnPath(string exe)
    {
        var pathsVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        // Add common locations that Finder-launched apps often miss
        var extra = new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin" };
        var paths = new System.Collections.Generic.HashSet<string>(pathsVar.Split(':', StringSplitOptions.RemoveEmptyEntries));
        foreach (var p in extra) paths.Add(p);
        foreach (var p in paths)
        {
            var candidate = Path.Combine(p, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? ResolveOnPathFromDir(string dir, string exe)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(dir, exe, SearchOption.AllDirectories))
            {
                if (File.Exists(path)) return path;
            }
        }
        catch { }
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
