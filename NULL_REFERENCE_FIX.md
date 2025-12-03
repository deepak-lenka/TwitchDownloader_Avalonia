# Null Reference Exception Fix

## Problem
The v0.1.3 release was experiencing "Object reference not set to an instance of an object" errors during VOD and Chat download operations. The app would crash or fail to complete downloads. Clip downloads worked correctly.

## Root Causes

### Issue 1: Missing CacheCleanerCallback (VOD Downloads)
The `VideoDownloadOptions` requires a `CacheCleanerCallback` to handle cleanup of abandoned cache directories from previous interrupted downloads. This callback was null, causing a null reference exception when `TwitchHelper.CleanupAbandonedVideoCaches()` tried to invoke it.

### Issue 2: Unsafe Progress Callbacks (All Downloads)
The progress callbacks in `UiTaskProgress` were capturing a `logBox` variable (TextBox control) in lambda closures. When these callbacks were invoked from background threads during the download process, the captured `logBox` reference could become stale, null, or inaccessible, causing null reference exceptions.

The issue occurred in:
- VOD downloads (`VodDownloadBtnOnClickAsync`)
- Clip downloads (`ClipDownloadBtnOnClickAsync`)
- Chat downloads (`ChatDownloadBtnOnClickAsync`)
- Chat rendering (`ChatRenderBtnOnClickAsync`)

## Solutions

### Fix 1: Add CacheCleanerCallback to VideoDownloadOptions
Added a simple callback that returns all directories for automatic cleanup:

```csharp
var opts = new VideoDownloadOptions
{
    // ... other options ...
    CacheCleanerCallback = dirs => dirs // Auto-delete all abandoned caches
};
```

This ensures the callback is never null and automatically cleans up abandoned cache directories without requiring user interaction.

### Fix 2: Thread-Safe Progress Callbacks
Modified all progress callback instantiations to:

1. **Find the control dynamically** on each callback invocation instead of capturing it
2. **Explicitly use Dispatcher.UIThread.Post()** to ensure thread-safe UI updates
3. **Add null checks** before accessing the control

### Before (Unsafe)
```csharp
var progress = new Services.UiTaskProgress(
    msg => AppendLog(logBox, msg + "\n"),
    status => AppendLog(logBox, status + "\n"),
    p => AppendLog(logBox, $"Progress: {p}%\n")
);
```

The `logBox` variable was captured in the closure, but could become invalid when invoked from background threads.

### After (Safe)
```csharp
var progress = new Services.UiTaskProgress(
    msg => Dispatcher.UIThread.Post(() => {
        var log = this.FindControl<TextBox>("VodLog");
        if (log != null) { 
            log.Text += msg + "\n"; 
            log.CaretIndex = log.Text?.Length ?? 0; 
        }
    }),
    status => Dispatcher.UIThread.Post(() => {
        var log = this.FindControl<TextBox>("VodLog");
        if (log != null) { 
            log.Text += status + "\n"; 
            log.CaretIndex = log.Text?.Length ?? 0; 
        }
    }),
    p => Dispatcher.UIThread.Post(() => {
        var log = this.FindControl<TextBox>("VodLog");
        if (log != null) { 
            log.Text += $"Progress: {p}%\n"; 
            log.CaretIndex = log.Text?.Length ?? 0; 
        }
    })
);
```

Now each callback:
- Finds the control by name when invoked
- Checks if the control is null before using it
- Explicitly marshals to the UI thread using `Dispatcher.UIThread.Post()`

## Benefits
- **No more null reference exceptions** during downloads
- **Thread-safe UI updates** from background operations
- **More robust** - handles cases where controls might be disposed or unavailable
- **Consistent behavior** across all download types

## Files Modified
- `TwitchDownloaderAvalonia/MainWindow.axaml.cs`
  - Added CacheCleanerCallback to VideoDownloadOptions (line ~561)
  - Fixed VOD download progress callbacks (line ~564)
  - Fixed Chat download progress callbacks (line ~886)
  - Fixed Clip download progress callbacks (line ~1262)
  - Fixed Chat Render progress callbacks (line ~1100)

## Testing
After this fix, you should be able to:
- Download VODs without crashes
- Download Clips without crashes
- Download Chat without crashes
- Render Chat without crashes
- See progress updates in the log without null reference errors
