using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SekaiUtau.Core;

namespace SekaiUtau.Voicebank;

/// <summary>
/// Recording screen: it shows one syllable at a time, records it, and writes the
/// finished voicebank as a ZIP at the end.
/// </summary>
public partial class MainView : UserControl {
    readonly IMicRecorder recorder = VoicebankHost.Platform.CreateRecorder();
    readonly Dictionary<string, VoicebankPack.Take> takes = new();
    readonly DispatcherTimer timer;
    readonly IReadOnlyList<VoicebankLanguage> languages = VoicebankLanguages.All;
    VoicebankLanguage language = VoicebankLanguages.Default;
    IReadOnlyList<RecordingSyllable> script = VoicebankLanguages.Default.Essentials;
    OtoMode otoMode = OtoMode.Automatic;
    int index;
    DateTime startedAt;

    public MainView() {
        InitializeComponent();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => UpdateRecordingLabel();
        RecordButton.Click += OnRecord;
        PrevButton.Click += (_, _) => Previous();
        SkipButton.Click += (_, _) => Next();
        ClearButton.Click += OnClear;
        CreateZipButton.Click += OnCreateZip;
        LanguageBox.ItemsSource = languages.Select(NameOf).ToList();
        LanguageBox.SelectedIndex = 0;
        LanguageBox.SelectionChanged += (_, _) => ChangeLanguage();
        UpdateScriptLabels();
        BasicRadio.IsCheckedChanged += (_, _) => ChangeScript();
        FullRadio.IsCheckedChanged += (_, _) => ChangeScript();
        AutoOtoRadio.IsCheckedChanged += (_, _) => UpdateOtoMode();
        ManualOtoRadio.IsCheckedChanged += (_, _) => UpdateOtoMode();
        UpdateOtoMode();
        BuildChips();
        UpdateUi();
    }

    void UpdateOtoMode() {
        otoMode = ManualOtoRadio.IsChecked == true ? OtoMode.Manual : OtoMode.Automatic;
        OtoHint.Text = otoMode == OtoMode.Automatic
            ? "L'appli repère le début du son, de la voyelle et de la consonne de chaque prise, puis écrit l'oto.ini."
            : "L'oto.ini est écrit à zéro avec les alias romaji et kana : sur PC, tu le règles dans vLabeler, SetParam ou l'éditeur d'oto.";
    }

    static string NameOf(VoicebankLanguage language) => language.Name;

    void ChangeLanguage() {
        int selected = LanguageBox.SelectedIndex;
        var picked = selected >= 0 && selected < languages.Count ? languages[selected] : VoicebankLanguages.Default;
        if (picked == language) {
            return;
        }
        if (recorder.IsRecording) {
            FinishTake();
        }
        language = picked;
        // Aliases change with the language: the old takes would be filed under the wrong names.
        takes.Clear();
        index = 0;
        UpdateScriptLabels();
        ChangeScript();
    }

    void UpdateScriptLabels() {
        LanguageHint.Text = language.Phonemizer;
        BasicRadio.Content = $"Essentiel ({language.Essentials.Count} syllabes)";
        FullRadio.Content = $"Complet ({language.Full.Count} syllabes)";
    }

    void ChangeScript() {
        var wanted = VoicebankLanguages.Script(language, FullRadio.IsChecked == true);
        if (ReferenceEquals(wanted, script)) {
            return;
        }
        if (recorder.IsRecording) {
            FinishTake();
        }
        script = wanted;
        index = 0;
        BuildChips();
        UpdateUi();
    }

    void BuildChips() {
        SyllablePanel.Children.Clear();
        for (int i = 0; i < script.Count; i++) {
            var syllable = script[i];
            bool done = takes.ContainsKey(syllable.Alias);
            // Each chip jumps straight to its syllable: on a 71-syllable bank, walking there
            // with the arrow button is far too slow.
            var chip = new Button {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 5),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse(done ? "#3A4A2A" : "#24262A")),
                Content = (done ? "✓ " : "") + syllable.Alias,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse(done ? "#C7E48A" : "#B9BDC4")),
            };
            int target = i;
            chip.Click += (_, _) => GoTo(target);
            SyllablePanel.Children.Add(chip);
        }
    }

    void GoTo(int target) {
        if (recorder.IsRecording) {
            FinishTake();
        }
        index = Math.Clamp(target, 0, Math.Max(0, script.Count - 1));
        UpdateUi();
    }

    void Previous() {
        if (recorder.IsRecording) {
            FinishTake();
            return;
        }
        if (index > 0) {
            index--;
        }
        UpdateUi();
    }

    void UpdateUi() {
        if (index >= script.Count) {
            CurrentAlias.Text = "Terminé";
            CurrentKana.Text = "";
            CurrentHint.Text = "Toutes les syllabes sont enregistrées. Tu peux créer le ZIP.";
            RecordButton.IsEnabled = false;
            SkipButton.IsEnabled = false;
            ClearButton.IsEnabled = false;
            PrevButton.IsEnabled = script.Count > 0;
        } else {
            var syllable = script[index];
            CurrentAlias.Text = syllable.Alias;
            CurrentKana.Text = syllable.Kana;
            CurrentHint.Text = takes.ContainsKey(syllable.Alias)
                ? "Déjà enregistrée. Tu peux la refaire."
                : "Dis la syllabe, puis appuie sur Arrêter.";
            RecordButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
            ClearButton.IsEnabled = true;
            PrevButton.IsEnabled = index > 0;
        }
        ProgressText.Text = $"{takes.Count} / {script.Count} enregistrées";
        BuildChips();
    }

    void UpdateRecordingLabel() {
        var elapsed = DateTime.Now - startedAt;
        RecordButton.Content = $"■ Arrêter ({elapsed.TotalSeconds:0.0} s)";
    }

    void OnRecord(object? sender, RoutedEventArgs args) {
        if (recorder.IsRecording) {
            FinishTake();
            return;
        }
        if (index >= script.Count) {
            return;
        }
        try {
            recorder.Start();
        } catch (Exception e) {
            ShowResult("Micro impossible : " + e.Message, isError: true);
            return;
        }
        startedAt = DateTime.Now;
        RecordButton.Content = "■ Arrêter (0.0 s)";
        timer.Start();
    }

    void FinishTake() {
        timer.Stop();
        RecordButton.Content = "● Enregistrer";
        short[] samples;
        try {
            samples = recorder.Stop();
        } catch (Exception e) {
            // A recorder that fails on stop (device unplugged, permission revoked) must not
            // take the whole screen down with it.
            ShowResult("Arrêt du micro impossible : " + e.Message, isError: true);
            UpdateUi();
            return;
        }
        if (index >= script.Count) {
            return;
        }
        var syllable = script[index];
        var (start, length) = VoicebankRecording.TrimSilence(samples, VoicebankHost.Platform.SampleRate);
        if (length <= 0) {
            // Nothing above the silence threshold: keep the raw take so the user still
            // hears what the phone captured instead of an empty file.
            start = 0;
            length = samples.Length;
        }
        if (length > 0) {
            takes[syllable.Alias] = new VoicebankPack.Take(samples, start, length);
            index++;
        }
        UpdateUi();
    }

    void OnClear(object? sender, RoutedEventArgs args) {
        if (recorder.IsRecording) {
            FinishTake();
        }
        if (index < script.Count) {
            takes.Remove(script[index].Alias);
        }
        UpdateUi();
    }

    void Next() {
        if (recorder.IsRecording) {
            FinishTake();
            return;
        }
        // Jump to the next syllable that still has no take, so finishing a bank recorded in
        // several sittings does not mean walking through the ones already done.
        for (int i = index + 1; i < script.Count; i++) {
            if (!takes.ContainsKey(script[i].Alias)) {
                index = i;
                UpdateUi();
                return;
            }
        }
        for (int i = 0; i < script.Count; i++) {
            if (!takes.ContainsKey(script[i].Alias)) {
                index = i;
                UpdateUi();
                return;
            }
        }
        UpdateUi();
    }

    void OnCreateZip(object? sender, RoutedEventArgs args) {
        if (recorder.IsRecording) {
            FinishTake();
        }
        string name = (NameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0) {
            ShowResult("Donne d'abord un nom à la voix.", isError: true);
            return;
        }
        if (takes.Count == 0) {
            ShowResult("Enregistre au moins une syllabe avant de créer le ZIP.", isError: true);
            return;
        }
        try {
            string author = (AuthorBox.Text ?? string.Empty).Trim();
            string zip = VoicebankPack.BuildZip(name, author, script, takes, otoMode, language.Id);
            string where = VoicebankPack.Save(zip);
            var info = new System.IO.FileInfo(zip);
            ShowResult($"ZIP créé : {where} ({info.Length / 1024} Ko, {takes.Count} syllabes).", isError: false);
        } catch (Exception e) {
            ShowResult("Création impossible : " + e.Message, isError: true);
        }
    }

    void ShowResult(string message, bool isError) {
        ResultText.Text = message;
        ResultText.IsVisible = true;
        ResultText.Foreground = new SolidColorBrush(Color.Parse(isError ? "#F08A8A" : "#C7E48A"));
    }
}
