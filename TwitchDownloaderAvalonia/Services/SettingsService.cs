using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchDownloaderAvalonia.Services
{
    public class SettingsService
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TwitchDownloader",
            "settings.json");

        private static AppSettings? _settings;

        public static AppSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = LoadSettings();
                }
                return _settings;
            }
        }

        private static AppSettings LoadSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors and return default settings
            }

            return new AppSettings();
        }

        public static void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                
                var json = JsonSerializer.Serialize(Settings, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception)
            {
                // Ignore errors
            }
        }
    }

    public class AppSettings
    {
        // Chat render settings
        public int ChatWidth { get; set; } = 350;
        public int ChatHeight { get; set; } = 600;
        public double ChatFontSize { get; set; } = 12;
        public int ChatFramerate { get; set; } = 30;
        public string ChatFont { get; set; } = "Inter Embedded";
        public string ChatBgColor { get; set; } = "#111111";
        public string ChatAltBgColor { get; set; } = "#191919";
        public string ChatMsgColor { get; set; } = "#ffffff";
        public bool ChatOutline { get; set; } = false;
        public bool ChatAltBackgrounds { get; set; } = false;
        public bool ChatTimestamps { get; set; } = false;
        public bool ChatBadges { get; set; } = true;
        public bool ChatAvatars { get; set; } = false;
        public bool ChatBttv { get; set; } = true;
        public bool ChatFfz { get; set; } = true;
        public bool ChatStv { get; set; } = true;

        // Download settings
        public int VodDownloadThreads { get; set; } = 4;
        public int ChatDownloadThreads { get; set; } = 4;
        public string TempPath { get; set; } = "";
        public int VodTrimMode { get; set; } = 0; // 0 = Exact, 1 = Safe
    }
}
