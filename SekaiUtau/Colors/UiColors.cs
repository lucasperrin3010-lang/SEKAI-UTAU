using System;
using Avalonia;
using Avalonia.Media;
using SekaiUtau.Core.Util;

namespace SekaiUtau.App;

/// <summary>
/// Colours the user can pick in Preferences > Appearance. They are applied on top of the
/// active theme, and only for the colours actually customised, so the built-in Light and
/// Dark themes keep working normally.
/// </summary>
static class UiColors {
    public const string DefaultBackground = "#1B1B1B";
    public const string DefaultPanel = "#222222";
    public const string DefaultText = "#D4D4D4";
    public const string DefaultBorder = "#343434";
    public const string DefaultNote = "#8CC63F";
    public const string DefaultSelectedNote = "#A8D95A";
    public const string DefaultGrid = "#2B2B2B";
    public const string DefaultPianoWhiteKey = "#D2D5D9";
    public const string DefaultPianoBlackKey = "#14161A";
    public const string DefaultPianoCenterKey = "#8CBF3E";

    public static Color Background => Current(Preferences.Default.UiBackgroundColor, "BackgroundColor", DefaultBackground);
    public static Color Panel => Current(Preferences.Default.UiPanelColor, "TrackBackgroundAltColor", DefaultPanel);
    public static Color Text => Current(Preferences.Default.UiTextColor, "ForegroundColor", DefaultText);
    public static Color Border => Current(Preferences.Default.UiBorderColor, "BorderColor", DefaultBorder);
    public static Color Note => Current(Preferences.Default.UiNoteColor, "AccentColor1", DefaultNote);
    public static Color SelectedNote => Current(Preferences.Default.UiSelectedNoteColor, "AccentColor2", DefaultSelectedNote);
    public static Color Grid => Current(Preferences.Default.UiGridColor, "TickLineColor", DefaultGrid);
    public static Color PianoWhiteKey => Current(Preferences.Default.UiPianoWhiteKeyColor, "SekaiPianoWhiteKey", DefaultPianoWhiteKey);
    public static Color PianoBlackKey => Current(Preferences.Default.UiPianoBlackKeyColor, "SekaiPianoBlackKey", DefaultPianoBlackKey);
    public static Color PianoCenterKey => Current(Preferences.Default.UiPianoCenterKeyColor, "SekaiPianoCenterKey", DefaultPianoCenterKey);

    /// <summary>Pushes the customised colours into the application resources.</summary>
    public static void ApplyFromPreferences() {
        if (Application.Current == null) {
            return;
        }
        var res = Application.Current.Resources;
        Apply(res, "BackgroundColor", Preferences.Default.UiBackgroundColor);
        Apply(res, "TrackBackgroundAltColor", Preferences.Default.UiPanelColor);
        Apply(res, "ForegroundColor", Preferences.Default.UiTextColor);
        Apply(res, "BorderColor", Preferences.Default.UiBorderColor);
        Apply(res, "TickLineColor", Preferences.Default.UiGridColor);
        // Accent 1 draws the notes and accent 3 their darker details; both follow the note colour.
        Apply(res, "AccentColor1", Preferences.Default.UiNoteColor);
        Apply(res, "AccentColor3", Preferences.Default.UiNoteColor);
        Apply(res, "SystemAccentColor", Preferences.Default.UiNoteColor);
        Apply(res, "SystemAccentColorDark1", Preferences.Default.UiNoteColor);
        Apply(res, "AccentColor2", Preferences.Default.UiSelectedNoteColor);
    }

    static void Apply(Avalonia.Controls.IResourceDictionary res, string key, string value) {
        if (!string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out var color)) {
            res[key] = color;
        }
    }

    static Color Current(string preference, string resourceKey, string fallback) {
        if (!string.IsNullOrWhiteSpace(preference) && Color.TryParse(preference, out var parsed)) {
            return parsed;
        }
        if (Application.Current != null &&
            Application.Current.Resources.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var resource) &&
            resource is Color color) {
            return color;
        }
        return Color.Parse(fallback);
    }
}
