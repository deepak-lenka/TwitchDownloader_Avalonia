using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            var token = await TwitchHelper.GetVideoToken(vodId, string.IsNullOrWhiteSpace(oauth) ? null : oauth);
            var playlist = await TwitchHelper.GetVideoPlaylist(vodId, token.data.videoPlaybackAccessToken.value, token.data.videoPlaybackAccessToken.signature);

            var m3u8 = M3U8.Parse(playlist);
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
                var best = (qualities.BestQuality()?.Name) ?? (names.Count > 0 ? names[0] : "160p30");
                combo.SelectedItem = best;
            }

            AppendLog(logBox, $"Loaded qualities: {string.Join(", ", names)}\n");
            UpdateVodEstimate();
        }
        catch (Exception ex)
        {
            AppendLog(logBox, "Load Info failed: " + ex.Message + "\n");
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
        var m = Regex.Match(text, @"clips?\.twitch\.tv/([A-Za-z0-9-]+)");
        if (m.Success) return m.Groups[1].Value;
        return text; // assume slug already
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
