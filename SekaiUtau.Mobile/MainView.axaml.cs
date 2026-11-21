using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SekaiUtau.Mobile;

/// <summary>
/// Main screen of the mobile edition: it opens a SEKAI UTAU project from the phone
/// storage and shows what it contains. Singing (resamplers, audio output) is not
/// ported yet, so this is a reader, not a full editor.
/// </summary>
public partial class MainView : UserControl {
    public MainView() {
        InitializeComponent();
        string version = typeof(MainView).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(MainView).Assembly.GetName().Version?.ToString(3)
            ?? "1.0.28";
        version = version.Split('+')[0];
        VersionText.Text = $"Édition mobile {version}";
        OpenButton.Click += OnOpenProject;
    }

    async void OnOpenProject(object? sender, RoutedEventArgs args) {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) {
            return;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Ouvrir un projet SEKAI UTAU",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("Projets SEKAI UTAU") {
                    Patterns = new[] { "*.sekaiu", "*.ustx" },
                },
            },
        });
        var file = files.FirstOrDefault();
        if (file == null) {
            return;
        }
        try {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            string text = await reader.ReadToEndAsync();
            ProjectName.Text = file.Name;
            ProjectInfo.Text = $"{text.Length / 1024.0:0.0} Ko";
            ProjectSummary.Text = Summarize(text);
            ProjectCard.IsVisible = true;
        } catch (Exception e) {
            ProjectName.Text = file.Name;
            ProjectInfo.Text = string.Empty;
            ProjectSummary.Text = "Lecture impossible : " + e.Message;
            ProjectCard.IsVisible = true;
        }
    }

    /// <summary>
    /// Light summary until the project model is ported: counts the tracks and the notes
    /// declared in the YAML project file, and reads the tempo (.ustx uses bpm:).
    /// </summary>
    static string Summarize(string text) {
        int tracks = CountOccurrences(text, "track_name:");
        int notes = CountOccurrences(text, "- position:");
        string tempo = "inconnu";
        foreach (string key in new[] { "bpm:", "tempo:" }) {
            int index = text.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) {
                continue;
            }
            int end = text.IndexOf((char)10, index);
            string value = text.Substring(index + key.Length, Math.Max(0, (end < 0 ? text.Length : end) - index - key.Length)).Trim();
            if (value.Length > 0) {
                tempo = value;
                break;
            }
        }
        return $"{tracks} piste(s), {notes} note(s), tempo {tempo}";
    }

    static int CountOccurrences(string text, string token) {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += token.Length;
        }
        return count;
    }
}
