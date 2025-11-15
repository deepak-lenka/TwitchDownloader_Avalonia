using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Tools;

namespace TwitchDownloaderAvalonia;

public partial class MainWindow : Window
{
    private readonly Services.FfmpegService _ffmpeg = new();
    private readonly System.Collections.Generic.Dictionary<string, M3U8.Stream> _vodQualityMap = new();
    private CancellationTokenSource? _vodCancellationTokenSource;
    private CancellationTokenSource? _clipCancellationTokenSource;
    private CancellationTokenSource? _chatCancellationTokenSource;
    private CancellationTokenSource? _chatRenderCancellationTokenSource;

    public MainWindow()
    {
        InitializeComponent();
        this.Opened += (_, __) => 
        {
            WireEvents();
            LoadSavedSettings();
        };
    }
    
    private void LoadSavedSettings()
    {
        // Load chat render settings
        if (this.FindControl<TextBox>("ChatWidth") is { } chatWidth)
            chatWidth.Text = Services.SettingsService.Settings.ChatWidth.ToString();
            
        if (this.FindControl<TextBox>("ChatHeight") is { } chatHeight)
            chatHeight.Text = Services.SettingsService.Settings.ChatHeight.ToString();
            
        if (this.FindControl<TextBox>("ChatFontSize") is { } chatFontSize)
            chatFontSize.Text = Services.SettingsService.Settings.ChatFontSize.ToString();
            
        if (this.FindControl<TextBox>("ChatFramerate") is { } chatFramerate)
            chatFramerate.Text = Services.SettingsService.Settings.ChatFramerate.ToString();
            
        if (this.FindControl<TextBox>("ChatFont") is { } chatFont)
            chatFont.Text = Services.SettingsService.Settings.ChatFont;
            
        if (this.FindControl<TextBox>("ChatBgColor") is { } chatBgColor)
            chatBgColor.Text = Services.SettingsService.Settings.ChatBgColor;
            
        if (this.FindControl<TextBox>("ChatAltBgColor") is { } chatAltBgColor)
            chatAltBgColor.Text = Services.SettingsService.Settings.ChatAltBgColor;
            
        if (this.FindControl<TextBox>("ChatMsgColor") is { } chatMsgColor)
            chatMsgColor.Text = Services.SettingsService.Settings.ChatMsgColor;
            
        if (this.FindControl<CheckBox>("ChatOutline") is { } chatOutline)
            chatOutline.IsChecked = Services.SettingsService.Settings.ChatOutline;
            
        if (this.FindControl<CheckBox>("ChatAltBackgrounds") is { } chatAltBackgrounds)
            chatAltBackgrounds.IsChecked = Services.SettingsService.Settings.ChatAltBackgrounds;
            
        if (this.FindControl<CheckBox>("ChatTimestamps") is { } chatTimestamps)
            chatTimestamps.IsChecked = Services.SettingsService.Settings.ChatTimestamps;
            
        if (this.FindControl<CheckBox>("ChatBadges") is { } chatBadges)
            chatBadges.IsChecked = Services.SettingsService.Settings.ChatBadges;
            
        if (this.FindControl<CheckBox>("ChatAvatars") is { } chatAvatars)
            chatAvatars.IsChecked = Services.SettingsService.Settings.ChatAvatars;
            
        if (this.FindControl<CheckBox>("ChatBttv") is { } chatBttv)
            chatBttv.IsChecked = Services.SettingsService.Settings.ChatBttv;
            
        if (this.FindControl<CheckBox>("ChatFfz") is { } chatFfz)
            chatFfz.IsChecked = Services.SettingsService.Settings.ChatFfz;
            
        if (this.FindControl<CheckBox>("ChatStv") is { } chatStv)
            chatStv.IsChecked = Services.SettingsService.Settings.ChatStv;
            
        // Load download settings
        if (this.FindControl<TextBox>("VodThreads") is { } vodThreads)
            vodThreads.Text = Services.SettingsService.Settings.VodDownloadThreads.ToString();
    }

    private static readonly HttpClient _http = new();

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) || ch == ':' ? '-' : ch).ToArray());
        // Collapse spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static async Task SetImageFromUrlAsync(Image? image, string? url)
    {
        if (image == null || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            await using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            await Dispatcher.UIThread.InvokeAsync(() => image.Source = bmp);
        }
        catch { /* ignore thumbnail errors */ }
    }

    private async void ClipLoadInfoBtnOnClickAsync(object? sender, RoutedEventArgs e)
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

            AppendLog(logBox, "Loading clip info...\n");
            var clipStatus = await TwitchHelper.GetShareClipRenderStatus(clipId);
            var clip = clipStatus?.data?.clip;
            if (clip is null || clip.assets is null)
            {
                AppendLog(logBox, "Unable to load clip info.\n");
                return;
            }

            var qualities = VideoQualities.FromClip(clip);
            var names = qualities.Qualities.Select(q => q.Name).ToList();
            if (this.FindControl<ComboBox>("ClipQualityCombo") is { } combo)
            {
                combo.ItemsSource = names;
                if (names.Count > 0)
                    combo.SelectedIndex = 0;
            }
            // Thumbnail
            await SetImageFromUrlAsync(this.FindControl<Image>("ClipThumb"), clip.thumbnailURL);

            // Metadata text
            if (this.FindControl<TextBlock>("ClipTitle") is { } clipTitle)
                clipTitle.Text = clip.title ?? string.Empty;
            if (this.FindControl<TextBlock>("ClipMeta") is { } clipMeta)
            {
                var when = clip.createdAt.ToLocalTime().ToString("g");
                var ch = clip.broadcaster?.displayName ?? "";
                var dur = TimeSpan.FromSeconds(clip.durationSeconds);
                clipMeta.Text = $"{ch} · {when} · {dur:c}";
            }

            // Suggested filename: [yyyy-MM-dd] Channel - Clip.mp4
            var date = clip.createdAt.ToLocalTime().ToString("dd-MM-yyyy");
            var channel = clip.broadcaster?.displayName ?? "Twitch";
            var suggested = SanitizeFileName($"[{date}] {channel} - Clip.mp4");
            if (this.FindControl<TextBox>("ClipOutput") is { } outBox)
            {
                if (string.IsNullOrWhiteSpace(outBox.Text) || outBox.Text.Equals("clip.mp4", StringComparison.OrdinalIgnoreCase))
                    outBox.Text = suggested;
            }
            AppendLog(logBox, $"Loaded qualities: {string.Join(", ", names)}\n");
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Load Info failed: " + ex.Message + "\n");
            AppendLog(logBox, GetFriendlyHint(ex) + "\n");
        }
    }

    private async void ChatLoadInfoBtnOnClickAsync(object? sender, RoutedEventArgs e)
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

            string normalizedId;
            bool isVod = false;
            if (TryParseVodId(idText, out var vodId))
            {
                normalizedId = vodId.ToString();
                isVod = true;
            }
            else if (LooksLikeClipUrl(idText))
            {
                var slug = ParseClipId(idText);
                if (string.IsNullOrWhiteSpace(slug))
                {
                    AppendLog(logBox, "Invalid clip URL or slug.\n");
                    return;
                }
                normalizedId = slug;
            }
            else
            {
                AppendLog(logBox, "Invalid VOD/Clip id or URL\n");
                return;
            }

            if (isVod)
            {
                var info = await TwitchHelper.GetVideoInfo(long.Parse(normalizedId));
                var lenSec = info?.data?.video?.lengthSeconds ?? 0;
                AppendLog(logBox, $"Title: {info?.data?.video?.title}\n");
                AppendLog(logBox, $"Streamer: {info?.data?.video?.owner?.displayName}\n");
                AppendLog(logBox, $"Length: {TimeSpan.FromSeconds(lenSec):c}\n");

                if (this.FindControl<TextBox>("ChatStart") is { } start && this.FindControl<TextBox>("ChatEnd") is { } end)
                {
                    if (string.IsNullOrWhiteSpace(start.Text)) start.Text = "0:00:00";
                    end.Text = TimeSpan.FromSeconds(lenSec).ToString("c");
                }

                // Thumbnail
                var thumb = info?.data?.video?.thumbnailURLs?.FirstOrDefault();
                await SetImageFromUrlAsync(this.FindControl<Image>("ChatThumb"), thumb);
                await SetImageFromUrlAsync(this.FindControl<Image>("VodThumb"), thumb);

                // Metadata text
                if (this.FindControl<TextBlock>("VodTitle") is { } vodTitle)
                    vodTitle.Text = info?.data?.video?.title ?? string.Empty;
                if (this.FindControl<TextBlock>("VodMeta") is { } vodMeta)
                {
                    var when = info?.data?.video?.createdAt.ToLocalTime().ToString("g");
                    var ch = info?.data?.video?.owner?.displayName ?? "";
                    var len = TimeSpan.FromSeconds(lenSec);
                    vodMeta.Text = $"{ch} · {when} · {len:c}";
                }
                if (this.FindControl<TextBlock>("ChatTitle") is { } chatTitle)
                    chatTitle.Text = info?.data?.video?.title ?? string.Empty;
                if (this.FindControl<TextBlock>("ChatMeta") is { } chatMeta)
                {
                    var when = info?.data?.video?.createdAt.ToLocalTime().ToString("g");
                    var ch = info?.data?.video?.owner?.displayName ?? "";
                    var len = TimeSpan.FromSeconds(lenSec);
                    chatMeta.Text = $"{ch} · {when} · {len:c}";
                }

                // Suggested filenames
                var date = info?.data?.video?.createdAt.ToLocalTime().ToString("dd-MM-yyyy");
                var channel = info?.data?.video?.owner?.displayName ?? "Twitch";
                var vodName = SanitizeFileName($"[{date}] {channel} - VOD.mp4");
                if (this.FindControl<TextBox>("VodOutput") is { } vodOut)
                {
                    if (string.IsNullOrWhiteSpace(vodOut.Text) || vodOut.Text.Equals("vod_clip.mp4", StringComparison.OrdinalIgnoreCase))
                        vodOut.Text = vodName;
                }
                var (fmt, comp, _) = GetChatSelectionsFromUi();
                var chatBase = SanitizeFileName($"[{date}] {channel} - Chat");
                if (this.FindControl<TextBox>("ChatOutput") is { } chatOut)
                {
                    var suggested = EnsureChatOutputExtension(chatBase, fmt, comp);
                    if (string.IsNullOrWhiteSpace(chatOut.Text) || chatOut.Text.Equals("chat.json", StringComparison.OrdinalIgnoreCase))
                        chatOut.Text = suggested;
                }
            }
            else
            {
                var clipInfo = await TwitchHelper.GetClipInfo(normalizedId);
                var durSec = clipInfo?.data?.clip?.durationSeconds ?? 0;
                AppendLog(logBox, $"Clip: {clipInfo?.data?.clip?.title}\n");
                AppendLog(logBox, $"Broadcaster: {clipInfo?.data?.clip?.broadcaster?.displayName}\n");
                AppendLog(logBox, $"Duration: {TimeSpan.FromSeconds(durSec):c}\n");

                if (this.FindControl<TextBox>("ChatStart") is { } start && this.FindControl<TextBox>("ChatEnd") is { } end)
                {
                    start.Text = "0:00:00";
                    end.Text = TimeSpan.FromSeconds(durSec).ToString("c");
                }

                // Thumbnail
                await SetImageFromUrlAsync(this.FindControl<Image>("ChatThumb"), clipInfo?.data?.clip?.thumbnailURL);

                // Metadata text
                if (this.FindControl<TextBlock>("ChatTitle") is { } chatTitle2)
                    chatTitle2.Text = clipInfo?.data?.clip?.title ?? string.Empty;
                if (this.FindControl<TextBlock>("ChatMeta") is { } chatMeta2)
                {
                    var when = clipInfo?.data?.clip?.createdAt.ToLocalTime().ToString("g");
                    var ch = clipInfo?.data?.clip?.broadcaster?.displayName ?? "";
                    var len = TimeSpan.FromSeconds(durSec);
                    chatMeta2.Text = $"{ch} · {when} · {len:c}";
                }

                // Suggested Chat filename
                var date = clipInfo?.data?.clip?.createdAt.ToLocalTime().ToString("dd-MM-yyyy");
                var channel = clipInfo?.data?.clip?.broadcaster?.displayName ?? "Twitch";
                var (fmt, comp, _) = GetChatSelectionsFromUi();
                var chatBase = SanitizeFileName($"[{date}] {channel} - Chat");
                if (this.FindControl<TextBox>("ChatOutput") is { } chatOut)
                {
                    var suggested = EnsureChatOutputExtension(chatBase, fmt, comp);
                    if (string.IsNullOrWhiteSpace(chatOut.Text) || chatOut.Text.Equals("chat.json", StringComparison.OrdinalIgnoreCase))
                        chatOut.Text = suggested;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Load Info failed: " + ex.Message + "\n");
            AppendLog(logBox, GetFriendlyHint(ex) + "\n");
        }
    }

    private void WireEvents()
    {
        if (this.FindControl<Button>("VodDownloadBtn") is { } vodBtn)
            vodBtn.Click += VodDownloadBtnOnClickAsync;
        if (this.FindControl<Button>("VodLoadInfoBtn") is { } vodLoad)
            vodLoad.Click += VodLoadInfoBtnOnClickAsync;
        if (this.FindControl<Button>("VodCancelBtn") is { } vodCancel)
            vodCancel.Click += VodCancelBtnOnClickAsync;
        if (this.FindControl<Button>("ClipDownloadBtn") is { } clipBtn)
            clipBtn.Click += ClipDownloadBtnOnClickAsync;
        if (this.FindControl<Button>("ClipLoadInfoBtn") is { } clipLoad)
            clipLoad.Click += ClipLoadInfoBtnOnClickAsync;
        if (this.FindControl<Button>("ClipCancelBtn") is { } clipCancel)
            clipCancel.Click += ClipCancelBtnOnClickAsync;
        if (this.FindControl<Button>("BtnDownloadFFmpeg") is { } ffmpegBtn)
            ffmpegBtn.Click += BtnDownloadFfmpegOnClickAsync;

        if (this.FindControl<Button>("ChatDownloadBtn") is { } chatDlBtn)
            chatDlBtn.Click += ChatDownloadBtnOnClickAsync;
        if (this.FindControl<Button>("ChatCancelBtn") is { } chatCancel)
            chatCancel.Click += ChatCancelBtnOnClickAsync;
        if (this.FindControl<Button>("ChatRenderBtn") is { } chatRenderBtn)
            chatRenderBtn.Click += ChatRenderBtnOnClickAsync;
        if (this.FindControl<Button>("ChatRenderCancelBtn") is { } chatRenderCancel)
            chatRenderCancel.Click += ChatRenderCancelBtnOnClickAsync;
        if (this.FindControl<Button>("ChatLoadInfoBtn") is { } chatLoad)
            chatLoad.Click += ChatLoadInfoBtnOnClickAsync;

        if (this.FindControl<ComboBox>("VodQualityCombo") is { } qCombo)
            qCombo.SelectionChanged += (_, __) => UpdateVodEstimate();
        if (this.FindControl<TextBox>("VodStart") is { } t1)
            t1.GetObservable(TextBox.TextProperty).Subscribe(_ => UpdateVodEstimate());
        if (this.FindControl<TextBox>("VodEnd") is { } t2)
            t2.GetObservable(TextBox.TextProperty).Subscribe(_ => UpdateVodEstimate());
    }

    private void UpdateVodActionButtons(bool isDownloading)
    {
        if (this.FindControl<Button>("VodDownloadBtn") is { } downloadBtn && 
            this.FindControl<Button>("VodCancelBtn") is { } cancelBtn)
        {
            if (isDownloading)
            {
                downloadBtn.IsVisible = false;
                cancelBtn.IsVisible = true;
            }
            else
            {
                downloadBtn.IsVisible = true;
                cancelBtn.IsVisible = false;
            }
        }
    }

    private void UpdateClipActionButtons(bool isDownloading)
    {
        if (this.FindControl<Button>("ClipDownloadBtn") is { } downloadBtn && 
            this.FindControl<Button>("ClipCancelBtn") is { } cancelBtn)
        {
            if (isDownloading)
            {
                downloadBtn.IsVisible = false;
                cancelBtn.IsVisible = true;
            }
            else
            {
                downloadBtn.IsVisible = true;
                cancelBtn.IsVisible = false;
            }
        }
    }

    private void UpdateChatActionButtons(bool isDownloading)
    {
        if (this.FindControl<Button>("ChatDownloadBtn") is { } downloadBtn && 
            this.FindControl<Button>("ChatCancelBtn") is { } cancelBtn)
        {
            if (isDownloading)
            {
                downloadBtn.IsVisible = false;
                cancelBtn.IsVisible = true;
            }
            else
            {
                downloadBtn.IsVisible = true;
                cancelBtn.IsVisible = false;
            }
        }
    }

    private void UpdateChatRenderActionButtons(bool isRendering)
    {
        if (this.FindControl<Button>("ChatRenderBtn") is { } renderBtn && 
            this.FindControl<Button>("ChatRenderCancelBtn") is { } cancelBtn)
        {
            if (isRendering)
            {
                renderBtn.IsVisible = false;
                cancelBtn.IsVisible = true;
            }
            else
            {
                renderBtn.IsVisible = true;
                cancelBtn.IsVisible = false;
            }
        }
    }

    private void VodCancelBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("VodLog");
        AppendLog(logBox, "Canceling download...\n");
        try
        {
            _vodCancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private void ClipCancelBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("ClipLog");
        AppendLog(logBox, "Canceling download...\n");
        try
        {
            _clipCancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private void ChatCancelBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("ChatLog");
        AppendLog(logBox, "Canceling download...\n");
        try
        {
            _chatCancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private void ChatRenderCancelBtnOnClickAsync(object? sender, RoutedEventArgs e)
    {
        var logBox = this.FindControl<TextBox>("ChatRenderLog");
        AppendLog(logBox, "Canceling render...\n");
        try
        {
            _chatRenderCancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) { }
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
            
            // Show save file dialog
            var fileExt = "mp4";
            var saveFileDialog = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save VOD As",
                SuggestedFileName = output,
                DefaultExtension = fileExt,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("MP4 Files")
                    {
                        Patterns = new[] { $"*.{fileExt}" },
                        MimeTypes = new[] { $"video/{fileExt}" }
                    }
                }
            });
            
            if (saveFileDialog == null)
            {
                AppendLog(logBox, "Download canceled.\n");
                return;
            }
            
            output = saveFileDialog.Path.LocalPath;
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

            _vodCancellationTokenSource?.Dispose();
            _vodCancellationTokenSource = new CancellationTokenSource();
            UpdateVodActionButtons(true);
            var downloader = new VideoDownloader(opts, progress);
            try
            {
                await downloader.DownloadAsync(_vodCancellationTokenSource.Token);
                AppendLog(logBox, "Done.\n");
            }
            catch (OperationCanceledException)
            {
                AppendLog(logBox, "Download canceled.\n");
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Error: " + ex.Message + "\n");
            }
            finally
            {
                _vodCancellationTokenSource?.Dispose();
                _vodCancellationTokenSource = null;
                UpdateVodActionButtons(false);
            }
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

            // Fetch video info for metadata/thumbnail and naming
            TwitchDownloaderCore.TwitchObjects.Gql.GqlVideoResponse? videoInfo = null;
            try
            {
                videoInfo = await TwitchHelper.GetVideoInfo(vodId);
            }
            catch { }

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

            // Thumbnail & suggested names
            var thumbUrl = videoInfo?.data?.video?.thumbnailURLs?.FirstOrDefault();
            await SetImageFromUrlAsync(this.FindControl<Image>("VodThumb"), thumbUrl);
            if (this.FindControl<TextBlock>("VodTitle") is { } vodTitle2)
                vodTitle2.Text = videoInfo?.data?.video?.title ?? string.Empty;
            if (this.FindControl<TextBlock>("VodMeta") is { } vodMeta2)
            {
                var when = videoInfo?.data?.video?.createdAt.ToLocalTime().ToString("g");
                var ch = videoInfo?.data?.video?.owner?.displayName ?? "";
                var len = TimeSpan.FromSeconds(videoInfo?.data?.video?.lengthSeconds ?? 0);
                vodMeta2.Text = $"{ch} · {when} · {len:c}";
            }

            var date = videoInfo?.data?.video?.createdAt.ToLocalTime().ToString("dd-MM-yyyy");
            var channel = videoInfo?.data?.video?.owner?.displayName ?? "Twitch";
            var vodName = SanitizeFileName($"[{date}] {channel} - VOD.mp4");
            if (this.FindControl<TextBox>("VodOutput") is { } vodOut)
            {
                if (string.IsNullOrWhiteSpace(vodOut.Text) || vodOut.Text.Equals("vod_clip.mp4", StringComparison.OrdinalIgnoreCase))
                    vodOut.Text = vodName;
            }
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
            
            // Determine file extension based on format and compression
            string fileExt = fmt.ToString().ToLower();
            if (comp == ChatCompression.Gzip)
                fileExt += ".gz";
                
            // Show save file dialog
            var saveFileDialog = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Chat As",
                SuggestedFileName = output,
                DefaultExtension = fileExt,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(fmt.ToString() + " Files")
                    {
                        Patterns = new[] { $"*.{fileExt}" },
                        MimeTypes = comp == ChatCompression.Gzip 
                            ? new[] { "application/gzip" } 
                            : new[] { "application/json", "text/html", "text/plain" }
                    }
                }
            });
            
            if (saveFileDialog == null)
            {
                AppendLog(logBox, "Download canceled.\n");
                return;
            }
            
            output = saveFileDialog.Path.LocalPath;
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
            _chatCancellationTokenSource?.Dispose();
            _chatCancellationTokenSource = new CancellationTokenSource();
            UpdateChatActionButtons(true);
            var downloader = new ChatDownloader(opts, progress);
            try
            {
                await downloader.DownloadAsync(_chatCancellationTokenSource.Token);
                AppendLog(logBox, "Done.\n");
                
                // Prefill Chat Render input with the just-downloaded file for convenience
                var renderInput = this.FindControl<TextBox>("ChatRenderInput");
                if (renderInput is not null)
                {
                    renderInput.Text = opts.Filename;
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog(logBox, "Download canceled.\n");
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Error: " + ex.Message + "\n");
                AppendLog(logBox, GetFriendlyHint(ex) + "\n");
            }
            finally
            {
                _chatCancellationTokenSource?.Dispose();
                _chatCancellationTokenSource = null;
                UpdateChatActionButtons(false);
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
            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
            {
                // Allow selecting input file
                var openFileDialog = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Chat JSON File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("JSON Files")
                        {
                            Patterns = new[] { "*.json", "*.json.gz" },
                            MimeTypes = new[] { "application/json", "application/gzip" }
                        }
                    }
                });
                
                if (openFileDialog == null || openFileDialog.Count == 0)
                {
                    AppendLog(logBox, "No input file selected\n");
                    return;
                }
                
                input = openFileDialog[0].Path.LocalPath;
                if (this.FindControl<TextBox>("ChatRenderInput") is { } inputBox)
                    inputBox.Text = input;
            }
            
            if (!File.Exists(input))
            {
                AppendLog(logBox, $"Input not found: {input}\n");
                return;
            }

            var output = (this.FindControl<TextBox>("ChatRenderOutput")?.Text ?? "chat.mp4").Trim();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += ".mp4";
            
            // Show save file dialog
            var fileExt = "mp4";
            var saveFileDialog = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Chat Render As",
                SuggestedFileName = output,
                DefaultExtension = fileExt,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("MP4 Files")
                    {
                        Patterns = new[] { $"*.{fileExt}" },
                        MimeTypes = new[] { $"video/{fileExt}" }
                    }
                }
            });
            
            if (saveFileDialog == null)
            {
                AppendLog(logBox, "Render canceled.\n");
                return;
            }
            
            output = saveFileDialog.Path.LocalPath;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // Load settings from UI or use saved settings
            _ = int.TryParse(this.FindControl<TextBox>("ChatWidth")?.Text, out var chatWidth);
            if (chatWidth <= 0) chatWidth = Services.SettingsService.Settings.ChatWidth;
            
            _ = int.TryParse(this.FindControl<TextBox>("ChatHeight")?.Text, out var chatHeight);
            if (chatHeight <= 0) chatHeight = Services.SettingsService.Settings.ChatHeight;
            
            _ = double.TryParse(this.FindControl<TextBox>("ChatFontSize")?.Text, out var fontSize);
            if (fontSize <= 0) fontSize = Services.SettingsService.Settings.ChatFontSize;
            
            _ = int.TryParse(this.FindControl<TextBox>("ChatFramerate")?.Text, out var fps);
            if (fps <= 0) fps = Services.SettingsService.Settings.ChatFramerate;
            
            var fontName = (this.FindControl<TextBox>("ChatFont")?.Text ?? Services.SettingsService.Settings.ChatFont).Trim();
            if (string.IsNullOrWhiteSpace(fontName)) fontName = "Inter Embedded";

            // Colors
            var bgText = this.FindControl<TextBox>("ChatBgColor")?.Text ?? Services.SettingsService.Settings.ChatBgColor;
            var altBgText = this.FindControl<TextBox>("ChatAltBgColor")?.Text ?? Services.SettingsService.Settings.ChatAltBgColor;
            var msgColorText = this.FindControl<TextBox>("ChatMsgColor")?.Text ?? Services.SettingsService.Settings.ChatMsgColor;
            
            var bg = ParseColorHex(bgText, "#111111");
            var altBg = ParseColorHex(altBgText, "#191919");
            var msgColor = ParseColorHex(msgColorText, "#ffffff");

            var outline = this.FindControl<CheckBox>("ChatOutline")?.IsChecked ?? Services.SettingsService.Settings.ChatOutline;
            var altBackgrounds = this.FindControl<CheckBox>("ChatAltBackgrounds")?.IsChecked ?? Services.SettingsService.Settings.ChatAltBackgrounds;
            var timestamps = this.FindControl<CheckBox>("ChatTimestamps")?.IsChecked ?? Services.SettingsService.Settings.ChatTimestamps;
            var badges = this.FindControl<CheckBox>("ChatBadges")?.IsChecked ?? Services.SettingsService.Settings.ChatBadges;
            var avatars = this.FindControl<CheckBox>("ChatAvatars")?.IsChecked ?? Services.SettingsService.Settings.ChatAvatars;

            var bttv = this.FindControl<CheckBox>("ChatBttv")?.IsChecked ?? Services.SettingsService.Settings.ChatBttv;
            var ffz = this.FindControl<CheckBox>("ChatFfz")?.IsChecked ?? Services.SettingsService.Settings.ChatFfz;
            var stv = this.FindControl<CheckBox>("ChatStv")?.IsChecked ?? Services.SettingsService.Settings.ChatStv;
            
            // Save settings for next time
            Services.SettingsService.Settings.ChatWidth = chatWidth;
            Services.SettingsService.Settings.ChatHeight = chatHeight;
            Services.SettingsService.Settings.ChatFontSize = fontSize;
            Services.SettingsService.Settings.ChatFramerate = fps;
            Services.SettingsService.Settings.ChatFont = fontName;
            Services.SettingsService.Settings.ChatBgColor = bgText;
            Services.SettingsService.Settings.ChatAltBgColor = altBgText;
            Services.SettingsService.Settings.ChatMsgColor = msgColorText;
            Services.SettingsService.Settings.ChatOutline = outline;
            Services.SettingsService.Settings.ChatAltBackgrounds = altBackgrounds;
            Services.SettingsService.Settings.ChatTimestamps = timestamps;
            Services.SettingsService.Settings.ChatBadges = badges;
            Services.SettingsService.Settings.ChatAvatars = avatars;
            Services.SettingsService.Settings.ChatBttv = bttv;
            Services.SettingsService.Settings.ChatFfz = ffz;
            Services.SettingsService.Settings.ChatStv = stv;
            Services.SettingsService.SaveSettings();

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
            _chatRenderCancellationTokenSource?.Dispose();
            _chatRenderCancellationTokenSource = new CancellationTokenSource();
            UpdateChatRenderActionButtons(true);
            var renderer = new ChatRenderer(renderOpts, progress);
            try
            {
                await renderer.ParseJsonAsync(_chatRenderCancellationTokenSource.Token);
                AppendLog(logBox, $"Rendering to: {renderOpts.OutputFile}\n");
                await renderer.RenderVideoAsync(_chatRenderCancellationTokenSource.Token);
                renderer.Dispose();
                AppendLog(logBox, "Done.\n");
            }
            catch (OperationCanceledException)
            {
                AppendLog(logBox, "Render canceled.\n");
                renderer.Dispose();
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Error: " + ex.Message + "\n");
                AppendLog(logBox, GetFriendlyHint(ex) + "\n");
                renderer.Dispose();
            }
            finally
            {
                _chatRenderCancellationTokenSource?.Dispose();
                _chatRenderCancellationTokenSource = null;
                UpdateChatRenderActionButtons(false);
            }
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

            var quality = (this.FindControl<ComboBox>("ClipQualityCombo")?.SelectedItem as string) ?? "1080p60";
            var output = (this.FindControl<TextBox>("ClipOutput")?.Text ?? "clip.mp4").Trim();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += ".mp4";
            
            // Show save file dialog
            var fileExt = "mp4";
            var saveFileDialog = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Clip As",
                SuggestedFileName = output,
                DefaultExtension = fileExt,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("MP4 Files")
                    {
                        Patterns = new[] { $"*.{fileExt}" },
                        MimeTypes = new[] { $"video/{fileExt}" }
                    }
                }
            });
            
            if (saveFileDialog == null)
            {
                AppendLog(logBox, "Download canceled.\n");
                return;
            }
            
            output = saveFileDialog.Path.LocalPath;
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
            _clipCancellationTokenSource?.Dispose();
            _clipCancellationTokenSource = new CancellationTokenSource();
            UpdateClipActionButtons(true);
            var downloader = new ClipDownloader(opts, progress);
            try
            {
                await downloader.DownloadAsync(_clipCancellationTokenSource.Token);
                AppendLog(logBox, "Done.\n");
            }
            catch (OperationCanceledException)
            {
                AppendLog(logBox, "Download canceled.\n");
            }
            catch (Exception ex)
            {
                AppendLog(logBox, "Error: " + ex.Message + "\n");
            }
            finally
            {
                _clipCancellationTokenSource?.Dispose();
                _clipCancellationTokenSource = null;
                UpdateClipActionButtons(false);
            }
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
