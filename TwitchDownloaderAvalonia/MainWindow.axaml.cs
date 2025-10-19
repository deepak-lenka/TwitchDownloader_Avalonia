using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkiaSharp;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderAvalonia;

public partial class MainWindow : Window
{
    private readonly Services.FfmpegService _ffmpeg = new();
    private readonly System.Collections.Generic.Dictionary<string, M3U8.Stream> _vodQualityMap = new();

    public MainWindow()
    {
        InitializeComponent();
        this.Opened += (_, __) => WireEvents();
    }

    private void WireEvents()
    {
        if (this.FindControl<Button>("VodDownloadBtn") is { } vodBtn)
            vodBtn.Click += VodDownloadBtnOnClickAsync;
        if (this.FindControl<Button>("VodLoadInfoBtn") is { } vodLoad)
            vodLoad.Click += VodLoadInfoBtnOnClickAsync;
        if (this.FindControl<Button>("ClipDownloadBtn") is { } clipBtn)
            clipBtn.Click += ClipDownloadBtnOnClickAsync;
        if (this.FindControl<Button>("BtnDownloadFFmpeg") is { } ffmpegBtn)
            ffmpegBtn.Click += BtnDownloadFfmpegOnClickAsync;

        if (this.FindControl<Button>("ChatDownloadBtn") is { } chatDlBtn)
            chatDlBtn.Click += ChatDownloadBtnOnClickAsync;
        if (this.FindControl<Button>("ChatRenderBtn") is { } chatRenderBtn)
            chatRenderBtn.Click += ChatRenderBtnOnClickAsync;

        if (this.FindControl<ComboBox>("VodQualityCombo") is { } qCombo)
            qCombo.SelectionChanged += (_, __) => UpdateVodEstimate();
        if (this.FindControl<TextBox>("VodStart") is { } t1)
            t1.GetObservable(TextBox.TextProperty).Subscribe(_ => UpdateVodEstimate());
        if (this.FindControl<TextBox>("VodEnd") is { } t2)
            t2.GetObservable(TextBox.TextProperty).Subscribe(_ => UpdateVodEstimate());
    }

    private async void VodDownloadBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("VodLog");
        try
        {
            var idText = this.FindControl<TextBox>("VodId")?.Text ?? string.Empty;
            if (!TryParseVodId(idText, out var vodId))
            {
                AppendLog(logBox, "Invalid VOD id or URL\n");
                return;
            }

            // Detect clip URLs pasted into VOD field
            if (LooksLikeClipUrl(idText))
            {
                AppendLog(logBox, "This looks like a Clip URL. Use the 'Clip Download' tab.\n");
                return;
            }

            var quality = (this.FindControl<ComboBox>("VodQualityCombo")?.SelectedItem as string) ?? "160p30";
            var startStr = this.FindControl<TextBox>("VodStart")?.Text ?? "0:00:00";
            var endStr = this.FindControl<TextBox>("VodEnd")?.Text ?? "0:00:30";
            var output = (this.FindControl<TextBox>("VodOutput")?.Text ?? "vod_clip.mp4").Trim();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += ".mp4";
            // If user provided a relative name, put it into ~/Downloads
            if (!Path.IsPathRooted(output))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var downloads = Path.Combine(home, "Downloads");
                var baseDir = Directory.Exists(downloads) ? downloads : home;
                output = Path.Combine(baseDir, output);
            }
            var outDir = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);

            var (start, end) = NormalizeTimes(startStr, endStr);
            if (end <= start)
            {
                end = start + TimeSpan.FromSeconds(30);
            }

            var ffmpegPath = await _ffmpeg.GetPreferredFfmpegPathAsync();

            var threadsText = this.FindControl<TextBox>("VodThreads")?.Text ?? "4";
            _ = int.TryParse(threadsText, out var threads);
            threads = Math.Clamp(threads == 0 ? 4 : threads, 1, 32);

            var trimModeIndex = this.FindControl<ComboBox>("VodTrimMode")?.SelectedIndex ?? 0;
            var trimMode = trimModeIndex == 1 ? VideoTrimMode.Safe : VideoTrimMode.Exact;

            var oauth = this.FindControl<TextBox>("VodOauth")?.Text;

            var opts = new VideoDownloadOptions
            {
                Id = vodId,
                Quality = quality,
                Filename = Path.GetFullPath(output),
                TrimBeginning = true,
                TrimBeginningTime = start,
                TrimEnding = true,
                TrimEndingTime = end,
                DownloadThreads = threads,
                ThrottleKib = -1,
                Oauth = string.IsNullOrWhiteSpace(oauth) ? null : oauth,
                FfmpegPath = ffmpegPath,
                TempFolder = null,
                TrimMode = trimMode,
                DelayDownload = false
            };

            var progress = new Services.UiTaskProgress(msg => AppendLog(logBox, msg + "\n"),
                (status) => AppendLog(logBox, status + "\n"),
                (p) => AppendLog(logBox, $"Progress: {p}%\n"));

            AppendLog(logBox, $"Starting VOD clip: {vodId} {quality} {start}->{end} -> {opts.Filename}\n");

            using var cts = new CancellationTokenSource();
            var downloader = new VideoDownloader(opts, progress);
            await downloader.DownloadAsync(cts.Token);

            AppendLog(logBox, "Done.\n");
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Error: " + ex.Message + "\n");
        }
    }

    private async void VodLoadInfoBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("VodLog");
        try
        {
            var idText = this.FindControl<TextBox>("VodId")?.Text ?? string.Empty;
            if (!TryParseVodId(idText, out var vodId))
            {
                AppendLog(logBox, "Invalid VOD id or URL\n");
                return;
            }
            var oauth = this.FindControl<TextBox>("VodOauth")?.Text;

            // 1) Get access token
            TwitchDownloaderCore.TwitchObjects.Gql.GqlVideoTokenResponse token;
            try
            {
                token = await TwitchHelper.GetVideoToken(vodId, string.IsNullOrWhiteSpace(oauth) ? null : oauth);
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Token request failed: " + ex.Message + "\n");
                return;
            }

            var vpat = token?.data?.videoPlaybackAccessToken;
            if (vpat is null || string.IsNullOrWhiteSpace(vpat.value) || string.IsNullOrWhiteSpace(vpat.signature))
            {
                AppendLog(logBox, "Twitch did not return a playback token. This VOD may be sub‑only/private; provide OAuth and try again.\n");
                return;
            }

            // 2) Get playlist text
            string playlist;
            try
            {
                playlist = await TwitchHelper.GetVideoPlaylist(vodId, vpat.value, vpat.signature);
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Fetching playlist failed: " + ex.Message + "\n");
                return;
            }

            // Some responses return error strings (403 restricted). Guard parsing.
            if (playlist.Contains("vod_manifest_restricted") || playlist.Contains("unauthorized_entitlements"))
            {
                AppendLog(logBox, "Access restricted: OAuth is required for this VOD.\n");
                return;
            }

            M3U8 m3u8;
            try
            {
                m3u8 = M3U8.Parse(playlist);
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Failed to parse playlist: " + ex.Message + "\n");
                return;
            }
            _vodQualityMap.Clear();
            var qualities = VideoQualities.FromM3U8(m3u8);
            var names = new System.Collections.Generic.List<string>();
            foreach (var q in qualities.Qualities)
            {
                _vodQualityMap[q.Name] = q.Item;
                names.Add(q.Name);
            }

            // Populate ComboBox
            if (this.FindControl<ComboBox>("VodQualityCombo") is { } combo)
            {
                combo.ItemsSource = names;
                if (names.Count > 0)
                {
                    var best = qualities.BestQuality()?.Name ?? names[0];
                    combo.SelectedItem = best;
                }
            }

            AppendLog(logBox, $"Loaded qualities: {string.Join(", ", names)}\n");
            UpdateVodEstimate();
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Load Info failed: " + ex.Message + "\n");
            AppendLog(logBox, GetFriendlyHint(ex) + "\n");
        }
    }

    private static string GetFriendlyHint(Exception ex)
    {
        // Unwrap aggregate/web exceptions
        var e = ex;
        if (ex is AggregateException ae && ae.InnerException != null) e = ae.InnerException;

        var msg = e.Message ?? string.Empty;

        // Access restrictions
        if (msg.Contains("403") || msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return "Hint: Twitch returned 403 (Forbidden). Try: 1) Paste your Twitch OAuth token in the OAuth box and retry, 2) Ensure your Mac clock is set automatically (Date & Time), 3) Disable VPN/Proxy and try a normal network, 4) Click 'Load Info' then start download within a minute, 5) Try a different public VOD.";
        }

        // Explicit VOD entitlement messages
        if (msg.Contains("vod_manifest_restricted") || msg.Contains("unauthorized_entitlements"))
        {
            return "Hint: This VOD is restricted. Provide a valid Twitch OAuth token for your account and retry. Some highlights/sub-only VODs require entitlement.";
        }

        // Clip -> missing VOD
        if (msg.Contains("Invalid VOD for clip", StringComparison.OrdinalIgnoreCase))
        {
            return "Hint: This clip's source VOD is missing/expired; chat replay is unavailable. Try another clip with a valid VOD or use the VOD URL/ID.";
        }

        // Network family hints
        if (msg.Contains("NameResolutionFailure", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("SSL", StringComparison.OrdinalIgnoreCase))
        {
            return "Hint: Network issue. Check internet connectivity, disable VPN/Proxy, and retry on a standard/home network.";
        }

        return "Hint: If this persists, try providing OAuth, syncing system time, disabling VPN/proxy, and retrying with a public VOD.";
    }

    private async void ChatDownloadBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("ChatLog");
        try
        {
            var idText = this.FindControl<TextBox>("ChatId")?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(idText))
            {
                AppendLog(logBox, "Provide a VOD/Clip URL or ID.\n");
                return;
            }

            // Normalize: if URL pasted, extract video ID or clip slug
            string normalizedId = idText;
            if (TryParseVodId(idText, out var vodId))
            {
                normalizedId = vodId.ToString();
            }
            else if (LooksLikeClipUrl(idText))
            {
                var clipSlug = ParseClipId(idText);
                if (string.IsNullOrWhiteSpace(clipSlug))
                {
                    AppendLog(logBox, "Invalid clip URL or slug.\n");
                    return;
                }
                normalizedId = clipSlug;
            }

            // Early check: if input is a clip slug, ensure its source VOD exists (chat requires VOD)
            if (!normalizedId.All(char.IsDigit))
            {
                try
                {
                    var clipStatus = await TwitchHelper.GetShareClipRenderStatus(normalizedId);
                    if (clipStatus?.data?.clip?.video == null || clipStatus.data.clip.videoOffsetSeconds == null)
                    {
                        AppendLog(logBox, "This clip's source VOD is deleted/expired. Chat replay is unavailable for this clip.\n");
                        return;
                    }
                }
                catch (Exception clipEx)
                {
                    AppendLog(logBox, "Fetching clip info failed: " + clipEx.Message + "\n");
                    return;
                }
            }

            // Determine output
            var output = (this.FindControl<TextBox>("ChatOutput")?.Text ?? "chat.json").Trim();
            var (fmt, comp, timeFmt) = GetChatSelectionsFromUi();
            output = EnsureChatOutputExtension(output, fmt, comp);
            if (!Path.IsPathRooted(output))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var downloads = Path.Combine(home, "Downloads");
                var baseDir = Directory.Exists(downloads) ? downloads : home;
                output = Path.Combine(baseDir, output);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // Times
            var start = ParseFlexibleTime(this.FindControl<TextBox>("ChatStart")?.Text ?? "0:00:00") ?? TimeSpan.Zero;
            var end = ParseFlexibleTime(this.FindControl<TextBox>("ChatEnd")?.Text ?? "0:00:30") ?? (start + TimeSpan.FromSeconds(30));
            if (end <= start) end = start + TimeSpan.FromSeconds(30);

            // Flags
            var embed = this.FindControl<CheckBox>("ChatEmbed")?.IsChecked == true;
            var bttv = this.FindControl<CheckBox>("ChatBttv")?.IsChecked ?? true;
            var ffz = this.FindControl<CheckBox>("ChatFfz")?.IsChecked ?? true;
            var stv = this.FindControl<CheckBox>("ChatStv")?.IsChecked ?? true;
            var threadsText = this.FindControl<TextBox>("ChatThreads")?.Text ?? "4";
            _ = int.TryParse(threadsText, out var threads);
            threads = Math.Clamp(threads == 0 ? 4 : threads, 1, 32);

            var opts = new ChatDownloadOptions
            {
                Id = normalizedId,
                Filename = Path.GetFullPath(output),
                DownloadFormat = fmt,
                Compression = comp,
                TrimBeginning = true,
                TrimBeginningTime = start.TotalSeconds,
                TrimEnding = true,
                TrimEndingTime = end.TotalSeconds,
                EmbedData = embed,
                BttvEmotes = bttv,
                FfzEmotes = ffz,
                StvEmotes = stv,
                DownloadThreads = threads,
                TimeFormat = timeFmt,
                TempFolder = null,
                DelayDownload = false
            };

            var progress = new Services.UiTaskProgress(msg => AppendLog(logBox, msg + "\n"),
                status => AppendLog(logBox, status + "\n"),
                p => AppendLog(logBox, $"Progress: {p}%\n"));

            AppendLog(logBox, $"Starting Chat download: {normalizedId} -> {opts.Filename}\n");
            using var cts = new CancellationTokenSource();
            var downloader = new ChatDownloader(opts, progress);
            await downloader.DownloadAsync(cts.Token);
            AppendLog(logBox, "Done.\n");

            // Prefill Chat Render input with the just-downloaded file for convenience
            var renderInput = this.FindControl<TextBox>("ChatRenderInput");
            if (renderInput is not null)
            {
                renderInput.Text = opts.Filename;
            }
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Error: " + ex.Message + "\n");
            AppendLog(logBox, GetFriendlyHint(ex) + "\n");
        }
    }

    private async void ChatRenderBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("ChatRenderLog");
        try
        {
            var input = (this.FindControl<TextBox>("ChatRenderInput")?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                AppendLog(logBox, "Provide an input chat JSON path (.json or .json.gz).\n");
                return;
            }
            if (!Path.IsPathRooted(input))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var downloads = Path.Combine(home, "Downloads");
                var baseDir = Directory.Exists(downloads) ? downloads : home;
                input = Path.Combine(baseDir, input);
            }
            if (!File.Exists(input))
            {
                AppendLog(logBox, $"Input not found: {input}\n");
                return;
            }

            var output = (this.FindControl<TextBox>("ChatRenderOutput")?.Text ?? "chat.mp4").Trim();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += ".mp4";
            if (!Path.IsPathRooted(output))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var downloads = Path.Combine(home, "Downloads");
                var baseDir = Directory.Exists(downloads) ? downloads : home;
                output = Path.Combine(baseDir, output);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // Numeric options
            _ = int.TryParse(this.FindControl<TextBox>("ChatWidth")?.Text ?? "350", out var chatWidth);
            _ = int.TryParse(this.FindControl<TextBox>("ChatHeight")?.Text ?? "600", out var chatHeight);
            _ = double.TryParse(this.FindControl<TextBox>("ChatFontSize")?.Text ?? "12", out var fontSize);
            _ = int.TryParse(this.FindControl<TextBox>("ChatFramerate")?.Text ?? "30", out var fps);
            var fontName = (this.FindControl<TextBox>("ChatFont")?.Text ?? "Inter Embedded").Trim();

            // Colors
            var bg = ParseColorHex(this.FindControl<TextBox>("ChatBgColor")?.Text, "#111111");
            var altBg = ParseColorHex(this.FindControl<TextBox>("ChatAltBgColor")?.Text, "#191919");
            var msgColor = ParseColorHex(this.FindControl<TextBox>("ChatMsgColor")?.Text, "#ffffff");

            var outline = this.FindControl<CheckBox>("ChatOutline")?.IsChecked == true;
            var altBackgrounds = this.FindControl<CheckBox>("ChatAltBackgrounds")?.IsChecked == true;
            var timestamps = this.FindControl<CheckBox>("ChatTimestamps")?.IsChecked == true;
            var badges = this.FindControl<CheckBox>("ChatBadges")?.IsChecked ?? true;
            var avatars = this.FindControl<CheckBox>("ChatAvatars")?.IsChecked == true;

            var bttv = this.FindControl<CheckBox>("ChatBttv")?.IsChecked ?? true;
            var ffz = this.FindControl<CheckBox>("ChatFfz")?.IsChecked ?? true;
            var stv = this.FindControl<CheckBox>("ChatStv")?.IsChecked ?? true;

            var ffmpegPath = await _ffmpeg.GetPreferredFfmpegPathAsync();

            var renderOpts = new ChatRenderOptions
            {
                InputFile = input,
                OutputFile = Path.GetFullPath(output),
                BackgroundColor = bg,
                AlternateBackgroundColor = altBg,
                AlternateMessageBackgrounds = altBackgrounds,
                MessageColor = msgColor,
                ChatHeight = chatHeight,
                ChatWidth = chatWidth,
                BttvEmotes = bttv,
                FfzEmotes = ffz,
                StvEmotes = stv,
                Outline = outline,
                OutlineSize = 4,
                Font = fontName,
                FontSize = fontSize,
                MessageFontStyle = SKFontStyle.Normal,
                UsernameFontStyle = SKFontStyle.Bold,
                Timestamp = timestamps,
                Framerate = fps,
                UpdateRate = 0.2,
                GenerateMask = false,
                InputArgs = "-framerate {fps} -f rawvideo -analyzeduration {max_int} -probesize {max_int} -pix_fmt {pix_fmt} -video_size {width}x{height} -i -",
                OutputArgs = "-c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p \"{save_path}\"",
                FfmpegPath = ffmpegPath,
                TempFolder = null,
                SubMessages = true,
                ChatBadges = badges,
                AllowUnlistedEmotes = true,
                DisperseCommentOffsets = false,
                RenderUserAvatars = avatars,
                AdjustUsernameVisibility = true
            };

            var progress = new Services.UiTaskProgress(msg => AppendLog(logBox, msg + "\n"),
                status => AppendLog(logBox, status + "\n"),
                p => AppendLog(logBox, $"Progress: {p}%\n"));

            AppendLog(logBox, $"Parsing chat: {renderOpts.InputFile}\n");
            using var cts = new CancellationTokenSource();
            var renderer = new ChatRenderer(renderOpts, progress);
            await renderer.ParseJsonAsync(cts.Token);
            AppendLog(logBox, $"Rendering to: {renderOpts.OutputFile}\n");
            await renderer.RenderVideoAsync(cts.Token);
            renderer.Dispose();
            AppendLog(logBox, "Done.\n");
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Error: " + ex.Message + "\n");
            AppendLog(logBox, GetFriendlyHint(ex) + "\n");
        }
    }

    private (ChatFormat fmt, ChatCompression comp, TimestampFormat timeFmt) GetChatSelectionsFromUi()
    {
        // Defaults
        var fmt = ChatFormat.Json;
        var comp = ChatCompression.None;
        var timeFmt = TimestampFormat.Relative;

        if (this.FindControl<ComboBox>("ChatFormatCombo") is { SelectedIndex: var i1 })
        {
            fmt = i1 switch { 0 => ChatFormat.Json, 1 => ChatFormat.Html, 2 => ChatFormat.Text, _ => ChatFormat.Json };
        }
        if (this.FindControl<ComboBox>("ChatCompressionCombo") is { SelectedIndex: var i2 })
        {
            comp = i2 switch { 0 => ChatCompression.None, 1 => ChatCompression.Gzip, _ => ChatCompression.None };
        }
        if (this.FindControl<ComboBox>("ChatTimeFormat") is { SelectedIndex: var i3 })
        {
            timeFmt = i3 switch { 0 => TimestampFormat.Relative, 1 => TimestampFormat.Utc, 2 => TimestampFormat.UtcFull, 3 => TimestampFormat.None, _ => TimestampFormat.Relative };
        }
        return (fmt, comp, timeFmt);
    }

    private static string EnsureChatOutputExtension(string path, ChatFormat fmt, ChatCompression comp)
    {
        var ext = Path.GetExtension(path);
        bool hasExt = !string.IsNullOrWhiteSpace(ext);
        string desired = fmt switch
        {
            ChatFormat.Json => comp == ChatCompression.Gzip ? ".json.gz" : ".json",
            ChatFormat.Html => ".html",
            ChatFormat.Text => ".txt",
            _ => ".json"
        };

        if (!hasExt)
            return path + desired;

        // If switching to .json.gz or ext doesn't match, strip current and append desired
        if ((fmt == ChatFormat.Json && comp == ChatCompression.Gzip && !path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase)) ||
            !ext.Equals(desired, StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, null) + desired;
        }
        if (fmt == ChatFormat.Json && comp == ChatCompression.Gzip && !path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, null) + ".json.gz";
        }
        return path;
    }

    private static SKColor ParseColorHex(string? text, string fallback)
    {
        try
        {
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(t)) return SKColor.Parse(fallback);
            return SKColor.Parse(t);
        }
        catch
        {
            return SKColor.Parse(fallback);
        }
    }
    private async void ClipDownloadBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("ClipLog");
        try
        {
            var idText = this.FindControl<TextBox>("ClipId")?.Text ?? string.Empty;
            var clipId = ParseClipId(idText);
            if (string.IsNullOrWhiteSpace(clipId))
            {
                AppendLog(logBox, "Invalid clip id or URL\n");
                return;
            }

            var quality = (this.FindControl<TextBox>("ClipQuality")?.Text ?? "1080p60").Trim();
            var output = (this.FindControl<TextBox>("ClipOutput")?.Text ?? "clip.mp4").Trim();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += ".mp4";
            if (!Path.IsPathRooted(output))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var downloads = Path.Combine(home, "Downloads");
                var baseDir = Directory.Exists(downloads) ? downloads : home;
                output = Path.Combine(baseDir, output);
            }
            var outDir = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);

            var ffmpegPath = await _ffmpeg.GetPreferredFfmpegPathAsync();

            var opts = new ClipDownloadOptions
            {
                Id = clipId,
                Quality = quality,
                Filename = Path.GetFullPath(output),
                ThrottleKib = -1,
                TempFolder = null,
                EncodeMetadata = false,
                FfmpegPath = ffmpegPath
            };

            var progress = new Services.UiTaskProgress(msg => AppendLog(logBox, msg + "\n"),
                (status) => AppendLog(logBox, status + "\n"),
                (p) => AppendLog(logBox, $"Progress: {p}%\n"));

            AppendLog(logBox, $"Starting Clip download: {clipId} {quality} -> {opts.Filename}\n");
            using var cts = new CancellationTokenSource();
            var downloader = new ClipDownloader(opts, progress);
            await downloader.DownloadAsync(cts.Token);
            AppendLog(logBox, "Done.\n");
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Error: " + ex.Message + "\n");
        }
    }

    private async void BtnDownloadFfmpegOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("VodLog");
        try
        {
            AppendLog(logBox, "Downloading FFmpeg...\n");
            var path = await _ffmpeg.DownloadLatestAsync();
            AppendLog(logBox, $"FFmpeg ready: {path}\n");
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "FFmpeg download failed: " + ex.Message + "\n");
        }
    }

    private static bool TryParseVodId(string text, out long vodId)
    {
        text = (text ?? string.Empty).Trim();
        if (long.TryParse(text, out vodId)) return true;
        var m = Regex.Match(text, @"videos/(\d+)");
        if (m.Success && long.TryParse(m.Groups[1].Value, out vodId)) return true;
        vodId = 0;
        return false;
    }

    private static bool LooksLikeClipUrl(string text)
    {
        text = (text ?? string.Empty).ToLowerInvariant();
        return text.Contains("clips.twitch.tv/") || text.Contains("/clip/");
    }

    private static string ParseClipId(string text)
    {
        text = (text ?? string.Empty).Trim();

        // 1) clips.twitch.tv/<slug>
        var m = Regex.Match(text, @"clips?\.twitch\.tv/([A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value;

        // 2) twitch.tv/<channel>/clip/<slug> OR twitch.tv/clip/<slug>
        m = Regex.Match(text, @"twitch\.tv/(?:[^/]+/)?clip/([A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value;

        // 3) If it's a bare slug (letters/digits/hyphen), return as-is; otherwise try to strip query/fragment
        if (Regex.IsMatch(text, @"^[A-Za-z0-9-]+$"))
            return text;

        // 4) Attempt to parse URI and take the last path segment as slug
        try
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                var last = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (last.Length > 0)
                {
                    var slug = last[^1];
                    // Strip query-like suffixes just in case
                    var q = slug.IndexOf('?', StringComparison.Ordinal);
                    if (q >= 0) slug = slug[..q];
                    var h = slug.IndexOf('#', StringComparison.Ordinal);
                    if (h >= 0) slug = slug[..h];
                    if (Regex.IsMatch(slug, @"^[A-Za-z0-9-]+$"))
                        return slug;
                }
            }
        }
        catch { }

        // Fallback: return original (downstream will error if invalid)
        return text;
    }

    private static (TimeSpan start, TimeSpan end) NormalizeTimes(string start, string end)
    {
        var s = ParseFlexibleTime(start) ?? TimeSpan.Zero;
        var e = ParseFlexibleTime(end) ?? (s + TimeSpan.FromSeconds(30));
        return (s, e);
    }

    private static TimeSpan? ParseFlexibleTime(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        var suffix = Regex.Match(input, @"^(\d+)(ms|s|m|h)$", RegexOptions.IgnoreCase);
        if (suffix.Success)
        {
            var n = int.Parse(suffix.Groups[1].Value);
            return suffix.Groups[2].Value.ToLower() switch
            {
                "ms" => TimeSpan.FromMilliseconds(n),
                "s" => TimeSpan.FromSeconds(n),
                "m" => TimeSpan.FromMinutes(n),
                "h" => TimeSpan.FromHours(n),
                _ => null
            };
        }

        var parts = input.Split(':', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            if (parts.Length == 1) return TimeSpan.FromSeconds(int.Parse(parts[0]));
            if (parts.Length == 2) return new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1]));
            if (parts.Length >= 3) return new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }
        catch { }
        return null;
    }

    private static void AppendLog(TextBox? box, string text)
    {
        if (box is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            box.Text += text;
            box.CaretIndex = box.Text?.Length ?? 0;
        });
    }

    private void UpdateVodEstimate()
    {
        var estimateLabel = this.FindControl<TextBlock>("VodEstimate");
        if (estimateLabel is null) return;
        try
        {
            var name = this.FindControl<ComboBox>("VodQualityCombo")?.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name) || !_vodQualityMap.TryGetValue(name, out var stream) || stream?.StreamInfo is null)
            {
                estimateLabel.Text = string.Empty;
                return;
            }

            var (start, end) = NormalizeTimes(this.FindControl<TextBox>("VodStart")?.Text ?? "0", this.FindControl<TextBox>("VodEnd")?.Text ?? "30");
            if (end <= start)
            {
                end = start + TimeSpan.FromSeconds(30);
            }

            var bytes = TwitchDownloaderCore.Tools.VideoSizeEstimator.EstimateVideoSize(stream.StreamInfo.Bandwidth, start, end);
            var nice = TwitchDownloaderCore.Tools.VideoSizeEstimator.StringifyByteCount(bytes);
            estimateLabel.Text = $"Estimated size: {nice}";
        }
        catch
        {
            estimateLabel.Text = string.Empty;
        }
    }
}
