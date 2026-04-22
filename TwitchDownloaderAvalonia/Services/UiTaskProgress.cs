using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TwitchDownloaderCore.Interfaces;

namespace TwitchDownloaderAvalonia.Services;

public sealed class UiTaskProgress : ITaskProgress
{
    private readonly Action<string>? _onLog;
    private readonly Action<string>? _onStatus;
    private readonly Action<int>? _onPercent;

    private int _lastPercent = -1;
    private TimeSpan _lastTime1 = new(-1);
    private TimeSpan _lastTime2 = new(-1);
    private readonly Stopwatch _progressThrottle = Stopwatch.StartNew();
    private const int PROGRESS_UPDATE_INTERVAL_MS = 100; // 10 updates per second max

    public UiTaskProgress(Action<string>? onLog, Action<string>? onStatus, Action<int>? onPercent)
    {
        _onLog = onLog;
        _onStatus = onStatus;
        _onPercent = onPercent;
    }

    public void SetStatus(string status) => _onStatus?.Invoke(status);

    public void SetTemplateStatus(string status, int initialPercent)
    {
        _onStatus?.Invoke(string.Format(status, initialPercent));
        _lastPercent = -1; // Force update on template change
        ReportProgress(initialPercent);
    }

    public void SetTemplateStatus(string status, int initialPercent, TimeSpan initialTime1, TimeSpan initialTime2)
    {
        _onStatus?.Invoke(string.Format(status, initialPercent, initialTime1, initialTime2));
        _lastPercent = -1; // Force update on template change
        ReportProgress(initialPercent, initialTime1, initialTime2);
    }

    public void ReportProgress(int percent)
    {
        if (_lastPercent == percent)
        {
            return;
        }

        if (percent == 0 || percent == 100 || _progressThrottle.ElapsedMilliseconds >= PROGRESS_UPDATE_INTERVAL_MS)
        {
            _onPercent?.Invoke(percent);
            _lastPercent = percent;
            _progressThrottle.Restart();
        }
    }

    public void ReportProgress(int percent, TimeSpan time1, TimeSpan time2)
    {
        if (_lastPercent == percent && _lastTime1 == time1 && _lastTime2 == time2)
        {
            return;
        }

        if (percent == 0 || percent == 100 || _progressThrottle.ElapsedMilliseconds >= PROGRESS_UPDATE_INTERVAL_MS)
        {
            _onPercent?.Invoke(percent);
            _lastPercent = percent;
            _lastTime1 = time1;
            _lastTime2 = time2;
            _progressThrottle.Restart();
        }
    }

    public void LogVerbose(string logMessage) { }

    public void LogVerbose(DefaultInterpolatedStringHandler logMessage) { _ = logMessage.ToStringAndClear(); }

    public void LogInfo(string logMessage) => _onLog?.Invoke(logMessage);

    public void LogInfo(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke(logMessage.ToStringAndClear());

    public void LogWarning(string logMessage) => _onLog?.Invoke("[WARN] " + logMessage);

    public void LogWarning(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke("[WARN] " + logMessage.ToStringAndClear());

    public void LogError(string logMessage) => _onLog?.Invoke("[ERROR] " + logMessage);

    public void LogError(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke("[ERROR] " + logMessage.ToStringAndClear());

    public void LogFfmpeg(string logMessage)
    {
        if (string.IsNullOrWhiteSpace(logMessage)) return;

        var trimmed = logMessage.TrimStart();

        // Filter out verbose FFMPEG logs that clutter the UI
        if (trimmed.Contains("libx264 @")) return;
        if (trimmed.StartsWith("ffmpeg version")) return;
        if (trimmed.StartsWith("built with")) return;
        if (trimmed.StartsWith("configuration:")) return;
        if (trimmed.StartsWith("lib")) return;
        if (trimmed.StartsWith("Stream mapping:")) return;
        if (trimmed.StartsWith("Stream #")) return;
        if (trimmed.StartsWith("Metadata:")) return;
        if (trimmed.StartsWith("encoder")) return;
        if (trimmed.StartsWith("Side data:")) return;
        if (trimmed.StartsWith("cpb:")) return;
        if (trimmed.StartsWith("Duration:")) return;
        if (trimmed.StartsWith("Input #")) return;
        if (trimmed.StartsWith("Output #")) return;
        if (trimmed.StartsWith("frame=")) return;
        if (trimmed.StartsWith("[out#")) return;
        if (trimmed.StartsWith("video:") && trimmed.Contains("audio:")) return; // muxing summary line

        _onLog?.Invoke("[FFMPEG] " + logMessage);
    }
}
