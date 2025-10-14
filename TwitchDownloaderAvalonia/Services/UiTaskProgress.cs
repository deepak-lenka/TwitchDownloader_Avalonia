using System;
using System.Runtime.CompilerServices;
using TwitchDownloaderCore.Interfaces;

namespace TwitchDownloaderAvalonia.Services;

public sealed class UiTaskProgress : ITaskProgress
{
    private readonly Action<string>? _onLog;
    private readonly Action<string>? _onStatus;
    private readonly Action<int>? _onPercent;

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
        _onPercent?.Invoke(initialPercent);
    }

    public void SetTemplateStatus(string status, int initialPercent, TimeSpan initialTime1, TimeSpan initialTime2)
    {
        _onStatus?.Invoke(string.Format(status, initialPercent));
        _onPercent?.Invoke(initialPercent);
    }

    public void ReportProgress(int percent) => _onPercent?.Invoke(percent);

    public void ReportProgress(int percent, TimeSpan time1, TimeSpan time2) => _onPercent?.Invoke(percent);

    public void LogVerbose(string logMessage) => _onLog?.Invoke(logMessage);

    public void LogVerbose(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke(logMessage.ToStringAndClear());

    public void LogInfo(string logMessage) => _onLog?.Invoke(logMessage);

    public void LogInfo(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke(logMessage.ToStringAndClear());

    public void LogWarning(string logMessage) => _onLog?.Invoke("[WARN] " + logMessage);

    public void LogWarning(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke("[WARN] " + logMessage.ToStringAndClear());

    public void LogError(string logMessage) => _onLog?.Invoke("[ERROR] " + logMessage);

    public void LogError(DefaultInterpolatedStringHandler logMessage) => _onLog?.Invoke("[ERROR] " + logMessage.ToStringAndClear());

    public void LogFfmpeg(string logMessage) => _onLog?.Invoke("[FFMPEG] " + logMessage);
}
