# TwitchDownloader (Avalonia) – macOS App

Cross‑platform GUI powered by Avalonia, reusing `TwitchDownloaderCore/`. This README explains how a user can download the macOS app and run it, and how maintainers can build/publish new zips.

---

## For Users: Download and Run on macOS

- **Download** the `.zip` provided by the author, e.g. `TwitchDownloader-macOS-arm64.zip`.
- **Unzip** it. You will get `TwitchDownloader.app`.
- **Open the app** by double‑clicking `TwitchDownloader.app`.
  - If macOS shows a security prompt, right‑click the app → Open → Open.
  - If still blocked, run these in Terminal (replace the path if needed):
    ```bash
    xattr -dr com.apple.quarantine ~/Downloads/TwitchDownloader.app
    open ~/Downloads/TwitchDownloader.app
    ```

### Apple Silicon vs Intel

- Apple Silicon (M1/M2/M3): use the `-arm64.zip` build.
- Intel Macs: use the `-x64.zip` build if provided.

### First‑Run Notes

- FFmpeg is required for finalizing downloads. The app can auto‑download a portable binary or use a system one if installed via Homebrew.
- Sub‑only/private VODs require an OAuth token. Paste it in the VOD tab when prompted.

---

## Quick Usage

- **VOD Download**
  - Paste a Twitch VOD URL or numeric ID.
  - Click “Load Info / Qualities”, pick a quality from the dropdown.
  - Set Start/End times; see the Estimated size update.
  - Set Output filename, then click “Download Clip”.

- **Clip Download**
  - Paste a Clip URL or slug.
  - Set Quality and Output, then click “Download Clip”.

- **Settings**
  - Download FFmpeg or point the app to a custom ffmpeg path if needed.

---

## For Maintainers: Build & Publish (macOS)

We use the `Dotnet.Bundle` MSBuild target to create a `.app` bundle.

### Build app bundle (Apple Silicon)

From repo root `TwitchDownloader/`:

```bash
dotnet restore TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj -r osx-arm64
dotnet msbuild TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj \
  -t:BundleApp -p:RuntimeIdentifier=osx-arm64 -p:Configuration=Release -p:UseAppHost=true

# App bundle will be placed at:
# TwitchDownloaderAvalonia/bin/Release/net9.0/osx-arm64/publish/TwitchDownloader.app
```

### Zip the app for distribution

```bash
cd TwitchDownloaderAvalonia/bin/Release/net9.0/osx-arm64/publish
zip -r TwitchDownloader-macOS-arm64.zip TwitchDownloader.app
```

### Optional: Intel build

```bash
dotnet msbuild TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj \
  -t:BundleApp -p:RuntimeIdentifier=osx-x64 -p:Configuration=Release -p:UseAppHost=true

cd TwitchDownloaderAvalonia/bin/Release/net9.0/osx-x64/publish
zip -r TwitchDownloader-macOS-x64.zip TwitchDownloader.app
```

### Gatekeeper (unsigned builds)

On the user’s Mac, if Gatekeeper blocks the app:

```bash
xattr -dr com.apple.quarantine /path/to/TwitchDownloader.app
open /path/to/TwitchDownloader.app
```

### Optional: Signing/Notarization (smoother UX)

```bash
codesign --deep --force --options runtime \
  --sign "Developer ID Application: Your Name (TEAMID)" TwitchDownloader.app

xcrun notarytool submit TwitchDownloader.app --keychain-profile "AC_PROFILE" --wait
xcrun stapler staple TwitchDownloader.app
```

---

## Troubleshooting

- “App can’t be opened because it is from an unidentified developer”
  - Right‑click → Open → Open, or use the `xattr` command above.
- “ffmpeg not found”
  - Use the Settings tab to download ffmpeg or install via Homebrew: `brew install ffmpeg`.
- “Invalid VOD id or URL” while pasting a clip link
  - Use the Clip tab for clip URLs; the VOD tab is for `/videos/<id>`.

---

## Credits

This UI uses the core logic from `TwitchDownloaderCore/`. Thanks to Avalonia and the open‑source libraries listed in `THIRD-PARTY-LICENSES.txt`.
