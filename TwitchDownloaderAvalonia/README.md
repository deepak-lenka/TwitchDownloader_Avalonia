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

## End‑user install from GitHub Releases (Terminal)

If a friend wants to install directly from your repo using Terminal, share these commands. Replace the version tag if needed (v0.1.0 shown as example).

Check Mac type:
```bash
uname -m
# arm64 = Apple Silicon, x86_64 = Intel
```

Apple Silicon (arm64):
```bash
cd ~/Downloads
curl -L -o TwitchDownloader-macOS-arm64.zip "https://github.com/deepak-lenka/TwitchDownloader_Avalonia/releases/download/v0.1.0/TwitchDownloader-macOS-arm64.zip"
unzip -q -o TwitchDownloader-macOS-arm64.zip
xattr -dr com.apple.quarantine "TwitchDownloader.app"
open "TwitchDownloader.app"
```

Intel (x64):
```bash
cd ~/Downloads
curl -L -o TwitchDownloader-macOS-x64.zip "https://github.com/deepak-lenka/TwitchDownloader_Avalonia/releases/download/v0.1.0/TwitchDownloader-macOS-x64.zip"
unzip -q -o TwitchDownloader-macOS-x64.zip
xattr -dr com.apple.quarantine "TwitchDownloader.app"
open "TwitchDownloader.app"
```

Optional move to Applications:
```bash
mv -f ~/Downloads/"TwitchDownloader.app" /Applications/
open /Applications/TwitchDownloader.app
```

Notes:
- Run one command per line (don’t glue commands together).
- If the release/tag changes, update the URL to the new tag.
- If Finder is used instead of Terminal: double‑click the zip, then right‑click the app → Open → Open on first run.

---

## Terminal Quickstart (macOS, no signing)

Follow these exact steps to build, zip, unzip, and run. Run one command per line.

### 1) Prerequisites

- Install .NET SDK (8 or 9):
  ```bash
  brew install --cask dotnet-sdk
  dotnet --version
  ```

### 2) Build the app bundle (arm64)

From the repo root `TwitchDownloader/`:

```bash
cd /Users/<you>/twitch-CLI/TwitchDownloader
dotnet restore TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj -r osx-arm64
dotnet msbuild TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj \
  -t:BundleApp -p:RuntimeIdentifier=osx-arm64 -p:Configuration=Release -p:UseAppHost=true
```

The app will be at:
`TwitchDownloaderAvalonia/bin/Release/net9.0/osx-arm64/publish/TwitchDownloader.app`

### 3) Create a zip (recommended: ditto)

```bash
cd TwitchDownloaderAvalonia/bin/Release/net9.0/osx-arm64/publish
ditto -c -k --keepParent TwitchDownloader.app TwitchDownloader-macOS-arm64.zip
ls -lh TwitchDownloader-macOS-arm64.zip
```

### 4) Test unzip and launch (like your testers will)

```bash
cd ~/Downloads
cp /Users/<you>/twitch-CLI/TwitchDownloader/TwitchDownloaderAvalonia/bin/Release/net9.0/osx-arm64/publish/TwitchDownloader-macOS-arm64.zip .
unzip -q -o TwitchDownloader-macOS-arm64.zip
xattr -dr com.apple.quarantine TwitchDownloader.app
open TwitchDownloader.app
```

If you prefer Finder: Double‑click the zip to unzip, then double‑click the app. If blocked, right‑click → Open → Open.

### 5) Share the zip

Upload `TwitchDownloader-macOS-arm64.zip` to Google Drive/GitHub Releases and share the link. Testers can follow step 4 to unzip and run.

### Intel Macs (x64)

```bash
cd /Users/<you>/twitch-CLI/TwitchDownloader
dotnet msbuild TwitchDownloaderAvalonia/TwitchDownloaderAvalonia.csproj \
  -t:BundleApp -p:RuntimeIdentifier=osx-x64 -p:Configuration=Release -p:UseAppHost=true
cd TwitchDownloaderAvalonia/bin/Release/net9.0/osx-x64/publish
ditto -c -k --keepParent TwitchDownloader.app TwitchDownloader-macOS-x64.zip
```

### Avoid common mistakes

- Do not paste multiple commands on one line unless you use `&&` between them.
  - Good: `cmd1 && cmd2`
  - Bad: `cmd1cmd2`
- If Finder says “is damaged and can’t be opened”, it’s Gatekeeper quarantine. Use `xattr -dr com.apple.quarantine TwitchDownloader.app` once after unzip.
- The app saves output by default to `~/Downloads/` (no permissions needed).

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
