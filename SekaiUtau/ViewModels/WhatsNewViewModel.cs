using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SekaiUtau.Core;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.App.ViewModels {
    /// <summary>One bullet point of a release.</summary>
    public class WhatsNewChange {
        public string Text { get; set; } = string.Empty;
        public string Bullet => "\u2022";
    }

    /// <summary>An illustration attached to a release.</summary>
    public class WhatsNewImage {
        public Bitmap? Image { get; set; }
        public string Caption { get; set; } = string.Empty;
        public bool HasCaption => !string.IsNullOrWhiteSpace(Caption);
    }

    /// <summary>One release described in Assets/Changelog/changelog.json.</summary>
    public class WhatsNewEntry {
        public string Version { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<WhatsNewChange> Changes { get; set; } = new List<WhatsNewChange>();
        public List<WhatsNewImage> Images { get; set; } = new List<WhatsNewImage>();
        public string Header => $"SEKAI UTAU v{Version}";
        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    }

    /// <summary>
    /// Reads the bundled changelog in the application language and remembers
    /// which version the user has already been shown.
    /// </summary>
    public class WhatsNewViewModel {
        public const string ChangelogUri = "avares://SekaiUtau/Assets/Changelog/changelog.json";

        public static string CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        /// <summary>Language used by the app: the preference, else the system culture.</summary>
        public static string LanguageCode {
            get {
                string pref = Preferences.Default.Language;
                string name = string.IsNullOrWhiteSpace(pref) ? CultureInfo.CurrentUICulture.Name : pref;
                if (string.IsNullOrWhiteSpace(name)) {
                    name = "en";
                }
                return name;
            }
        }

        public bool IsDarkMode => ThemeManager.IsDarkMode;

        public string CurrentVersionLabel =>
            string.Format(ThemeManager.GetString("whatsnew.current"), CurrentVersion);

        public List<WhatsNewEntry> Entries { get; } = new List<WhatsNewEntry>();

        static string SeenFilePath => Path.Combine(PathManager.Inst.DataPath, "WhatsNewSeen.txt");

        public WhatsNewViewModel() {
            try {
                string lang = LanguageCode;
                using var stream = AssetLoader.Open(new Uri(ChangelogUri));
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("entries", out var entries)) {
                    Log.Warning("Changelog has no entries.");
                    return;
                }
                foreach (var element in entries.EnumerateArray()) {
                    var entry = new WhatsNewEntry {
                        Version = Localized(element, "version", lang),
                        Date = Localized(element, "date", lang),
                        Title = Localized(element, "title", lang),
                    };
                    if (element.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object) {
                        foreach (string text in LocalizedList(changes, lang)) {
                            entry.Changes.Add(new WhatsNewChange { Text = text });
                        }
                    }
                    if (element.TryGetProperty("images", out var images)) {
                        foreach (var image in images.EnumerateArray()) {
                            string path = Localized(image, "path", lang);
                            if (string.IsNullOrWhiteSpace(path)) {
                                continue;
                            }
                            try {
                                using var imageStream = AssetLoader.Open(new Uri(path));
                                entry.Images.Add(new WhatsNewImage {
                                    Image = new Bitmap(imageStream),
                                    Caption = Localized(image, "caption", lang),
                                });
                            } catch (Exception e) {
                                Log.Warning(e, $"Failed to load changelog image {path}");
                            }
                        }
                    }
                    Entries.Add(entry);
                }
                Log.Information($"Changelog loaded: {Entries.Count} release(s) in {lang}.");
            } catch (Exception e) {
                Log.Error(e, "Failed to load the changelog.");
            }
        }

        /// <summary>Reads a string, or the best match for the language in a { "fr": … } object.</summary>
        static string Localized(JsonElement parent, string name, string lang) {
            if (!parent.TryGetProperty(name, out var value)) {
                return string.Empty;
            }
            return PickLocalized(value, lang);
        }

        static string PickLocalized(JsonElement value, string lang) {
            if (value.ValueKind == JsonValueKind.String) {
                return value.GetString() ?? string.Empty;
            }
            if (value.ValueKind != JsonValueKind.Object) {
                return string.Empty;
            }
            foreach (string candidate in LanguageCandidates(lang)) {
                if (value.TryGetProperty(candidate, out var exact) && exact.ValueKind == JsonValueKind.String) {
                    return exact.GetString() ?? string.Empty;
                }
            }
            foreach (var property in value.EnumerateObject()) {
                if (property.Value.ValueKind == JsonValueKind.String) {
                    return property.Value.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        static IEnumerable<string> LanguageCandidates(string lang) {
            yield return lang;
            string two = lang.Split('-')[0];
            if (two != lang) {
                yield return two;
            }
            yield return "en";
        }

        static IEnumerable<string> LocalizedList(JsonElement value, string lang) {
            foreach (string candidate in LanguageCandidates(lang)) {
                if (value.TryGetProperty(candidate, out var list) && list.ValueKind == JsonValueKind.Array) {
                    return ReadList(list);
                }
            }
            foreach (var property in value.EnumerateObject()) {
                if (property.Value.ValueKind == JsonValueKind.Array) {
                    return ReadList(property.Value);
                }
            }
            return new List<string>();
        }

        static List<string> ReadList(JsonElement list) {
            var result = new List<string>();
            foreach (var item in list.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.String) {
                    string? text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) {
                        result.Add(text);
                    }
                }
            }
            return result;
        }

        /// <summary>True when the running version differs from the last one shown.</summary>
        public static bool ShouldShow() {
            try {
                string seen = File.Exists(SeenFilePath)
                    ? File.ReadAllText(SeenFilePath).Trim()
                    : string.Empty;
                return seen != CurrentVersion;
            } catch (Exception e) {
                Log.Warning(e, "Failed to read the shown version.");
                return false;
            }
        }

        public static void MarkSeen() {
            try {
                File.WriteAllText(SeenFilePath, CurrentVersion);
            } catch (Exception e) {
                Log.Warning(e, "Failed to store the shown version.");
            }
        }
    }
}
