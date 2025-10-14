# TwitchDownloaderAvalonia – Delivery Plan

This document tracks the plan and progress for building a macOS-friendly GUI (Avalonia) mirroring the Windows WPF app while reusing `TwitchDownloaderCore/`.

## Milestones

- [x] Scaffold Avalonia project (`net6.0`, Fluent theme, ReactiveUI optional)
- [x] Reference `TwitchDownloaderCore`
- [x] Basic window + tabs (VOD, Clip, Settings)
- [x] Add README with run/publish instructions
- [ ] Wire VOD download to `VideoDownloader`
- [ ] Wire Clip download to `ClipDownloader`
- [ ] Settings: FFmpeg download (portable) + chmod on macOS
- [ ] Add Task Queue shell and basic runner
- [ ] Add Chat Download page (`ChatDownloader`)
- [ ] Add Chat Update page (`ChatUpdater`)
- [ ] Add Chat Render page (`ChatRenderer`)
- [ ] Theming toggle (Dark/Light/System)
- [ ] Localization hooks (resources)
- [ ] Packaging (macOS app bundle, optional signing)

## macOS Setup (required)

- Install .NET SDK (6 or 8)
  - Download: https://dotnet.microsoft.com/download
  - Or Homebrew:
    ```bash
    brew install --cask dotnet-sdk
    ```
  - Verify:
    ```bash
    dotnet --version
    ```

## Build & Run (dev)

From repo root `TwitchDownloader/`:

```bash
# Restore
dotnet restore TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj

# Run
dotnet run --project TwitchDownloaderAvalonia
```

## Publish (macOS)

Apple Silicon (arm64):
```bash
dotnet publish TwitchDownloaderAvalonia -c Release -r osx-arm64 --self-contained true -p:UseAppHost=true
open TwitchDownloaderAvalonia/bin/Release/net6.0/osx-arm64/publish/TwitchDownloaderAvalonia.app
```

Intel mac (x64):
```bash
dotnet publish TwitchDownloaderAvalonia -c Release -r osx-x64 --self-contained true -p:UseAppHost=true
```

## Notes

- For sub-only/private VODs, OAuth is needed (same as WPF). Never share tokens.
- FFmpeg is required for finalization/rendering; provide a Settings action to fetch a portable copy and mark it executable.
- All job flows should map 1:1 with WPF pages to match user expectations.
