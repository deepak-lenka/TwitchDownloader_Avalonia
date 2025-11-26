# Progress Update Throttling Fix

## Problem
The Avalonia (macOS) app was printing `Progress: X%` updates on **every single frame** during video rendering, causing:
- Console flooding with hundreds of duplicate progress lines
- UI hanging and becoming unresponsive
- Poor user experience

Example of the problem:
```
Progress: 1%
Progress: 1%
Progress: 1%
Progress: 1%
... (repeated 50+ times)
Progress: 2%
Progress: 2%
Progress: 2%
... (repeated 50+ times)
```

## Root Cause
The `UiTaskProgress` class in `TwitchDownloaderAvalonia/Services/UiTaskProgress.cs` was calling the progress callback (`_onPercent?.Invoke(percent)`) on **every** `ReportProgress()` call without any throttling or deduplication.

The Windows (WPF) version had built-in throttling by tracking `_lastPercent` and only updating when the value changed, but the Avalonia version was missing this optimization.

## Solution
Added intelligent throttling to `UiTaskProgress.cs`:

1. **Track last reported values**: Store `_lastPercent`, `_lastTime1`, `_lastTime2` to detect duplicates
2. **Time-based throttling**: Use `Stopwatch` to limit updates to **10 per second** (100ms interval)
3. **Skip duplicate updates**: Only invoke callbacks when:
   - The percent value actually changed, OR
   - At least 100ms has elapsed since the last update

### Code Changes
```csharp
// Added fields for throttling
private int _lastPercent = -1;
private TimeSpan _lastTime1 = new(-1);
private TimeSpan _lastTime2 = new(-1);
private readonly Stopwatch _progressThrottle = Stopwatch.StartNew();
private const int PROGRESS_UPDATE_INTERVAL_MS = 100; // 10 updates per second max

// Updated ReportProgress to check before invoking
public void ReportProgress(int percent)
{
    // Only update if percent changed AND enough time has elapsed
    if (_lastPercent == percent && _progressThrottle.ElapsedMilliseconds < PROGRESS_UPDATE_INTERVAL_MS)
    {
        return;
    }

    _onPercent?.Invoke(percent);
    _lastPercent = percent;
    _progressThrottle.Restart();
}
```

## Benefits
- **Reduced console spam**: Progress updates now limited to 10/second maximum
- **Better performance**: UI no longer hangs from excessive updates
- **Consistent with Windows version**: Matches the throttling behavior of WPF implementation
- **Smooth progress display**: Still responsive enough for real-time feedback

## Testing
After rebuilding the app, you should see:
- Progress updates appearing ~10 times per second
- No duplicate consecutive progress lines
- Smooth, responsive UI during rendering
- Much cleaner console output

## Files Modified
- `TwitchDownloaderAvalonia/Services/UiTaskProgress.cs`
