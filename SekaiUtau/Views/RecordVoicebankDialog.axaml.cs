using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NAudio.Wave;
using SekaiUtau.Core;

namespace SekaiUtau.App.Views {
    /// <summary>
    /// Guided voicebank recording: shows one syllable at a time, records it from the default
    /// microphone, then creates the voicebank folder with the WAV files and an oto.ini built
    /// from the recording script.
    /// </summary>
    public partial class RecordVoicebankDialog : Window {
        const int SampleRate = 44100;
        // Anything shorter than this is a click, not a sung syllable.
        const int MinTakeMs = 150;

        WaveInEvent? capture;
        WaveOutEvent? playback;
        AudioFileReader? player;
        readonly List<short> take = new();
        readonly Dictionary<string, string> takes = new();
        readonly List<string> takeOrder = new();
        readonly IReadOnlyList<VoicebankLanguage> languages = VoicebankLanguages.All;
        VoicebankLanguage language = VoicebankLanguages.Default;
        IReadOnlyList<RecordingSyllable> script = VoicebankLanguages.Default.Full;
        bool fullScript = true;
        OtoMode otoMode = OtoMode.Automatic;
        string? tempDir;
        string? photoPath;
        string? portraitPath;
        int index;
        bool recording;
        // Automatic stop: the take ends by itself once the singer has been quiet for a moment,
        // so a whole bank can be recorded with the space bar alone.
        bool autoStop = true;
        int silentMs;
        // A chunk below this level is silence. The microphone level is a peak per chunk, so a
        // breath or room noise sits well under it while a consonant does not.
        const int SilenceThreshold = 350;
        const int AutoStopSilenceMs = 650;
        const int AutoStopMinMs = 350;
        // Audio devices offered in the setup page (-1 is the system default).
        readonly List<int> micDevices = new();
        int micDevice = -1;
        // Every take is levelled to the same loudness, so a bank recorded in several sittings
        // sings at one level instead of jumping from note to note.
        bool levelTakes = true;
        bool autoPlay;
        DispatcherTimer? takeTimer;
        bool created;
        bool closingConfirmed;

        public RecordVoicebankDialog() {
            InitializeComponent();
            PathBox.Text = VoicebankCreator.SingersRoot;
            // The voice can be recorded in every language the application can sing, and the
            // syllable list follows the phonemizer of that language.
            LanguageBox.ItemsSource = languages.Select(LanguageName).ToList();
            LanguageBox.SelectedIndex = 0;
            LanguageHint.Text = language.Phonemizer;
            LanguageBox.SelectionChanged += (_, _) => OnLanguageChanged();
            ScriptBox.SelectionChanged += (_, _) => {
                fullScript = ScriptBox.SelectedIndex != 0;
                script = VoicebankLanguages.Script(language, fullScript);
                ClearTakes();
            };
            FillScriptChoices();
            script = VoicebankLanguages.Script(language, fullScript);
            OtoLabel.Text = Localized("voicebank.record.oto");
            OtoBox.ItemsSource = new[] {
                Localized("voicebank.record.oto.auto"),
                Localized("voicebank.record.oto.manual"),
            };
            OtoBox.SelectedIndex = 0;
            UpdateOtoHint();
            OtoBox.SelectionChanged += (_, _) => {
                otoMode = OtoBox.SelectedIndex == 1 ? OtoMode.Manual : OtoMode.Automatic;
                UpdateOtoHint();
            };
            RecordButton.Content = ThemeManager.GetString("voicebank.record.record");
            PlayButton.Content = ThemeManager.GetString("voicebank.record.play");
            SkipButton.Content = ThemeManager.GetString("voicebank.record.skip");
            // (key, English fallback): a language that does not translate these yet shows the
            // English text instead of an empty button, even if the resource chain ever breaks.
            PrevButton.Content = Localized("voicebank.record.prev", "◀");
            NextButton.Content = Localized("voicebank.record.next", "▶");
            AutoStopBox.Content = Localized("voicebank.record.autostop", "Stop automatically");
            AutoStopBox.IsCheckedChanged += (_, _) => autoStop = AutoStopBox.IsChecked == true;
            LevelBox.Content = Localized("voicebank.record.level", "Level the takes");
            LevelBox.IsCheckedChanged += (_, _) => levelTakes = LevelBox.IsChecked == true;
            AutoPlayBox.Content = Localized("voicebank.record.autoplay", "Play the take back");
            AutoPlayBox.IsCheckedChanged += (_, _) => autoPlay = AutoPlayBox.IsChecked == true;
            FillMics();
            takeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            takeTimer.Tick += (_, _) => {
                int ms;
                lock (take) {
                    ms = take.Count * 1000 / SampleRate;
                }
                RecordButton.Content = $"{Localized("voicebank.record.stop", "■ Stop")} ({ms / 1000.0:0.0} s)";
            };
            FinishButton.Content = ThemeManager.GetString("voicebank.record.finish");
            AddHandler(KeyDownEvent, OnDialogKeyDown);
            Closing += OnClosingGuard;
            Closing += (_, _) => Cleanup();
        }

        async void OnChoosePhoto(object sender, RoutedEventArgs args) {
            var file = await FilePicker.OpenFile(
                this,
                "voicebank.create.photo",
                (string?)null,
                FilePickerFileTypes.ImageAll);
            if (string.IsNullOrEmpty(file)) {
                return;
            }
            try {
                // Loading it here also rejects files that are not real pictures.
                using (var bitmap = new Bitmap(file)) {
                    PhotoPreview.Source = new Bitmap(file);
                }
                photoPath = file;
                PhotoName.Text = Path.GetFileName(file);
                SetupError.IsVisible = false;
            } catch (Exception e) {
                ShowSetupError(ThemeManager.GetString("voicebank.create.photo.invalid"));
                Serilog.Log.Error(e, "Loading the voicebank icon failed");
            }
        }

        void OnRemovePhoto(object sender, RoutedEventArgs args) {
            photoPath = null;
            PhotoPreview.Source = null;
            PhotoName.Text = ThemeManager.GetString("voicebank.create.photo.none");
        }

        async void OnChoosePortrait(object sender, RoutedEventArgs args) {
            var file = await FilePicker.OpenFile(
                this,
                "voicebank.create.portrait",
                (string?)null,
                FilePickerFileTypes.ImageAll);
            if (string.IsNullOrEmpty(file)) {
                return;
            }
            try {
                using (var bitmap = new Bitmap(file)) {
                    PortraitPreview.Source = new Bitmap(file);
                }
                portraitPath = file;
                PortraitName.Text = Path.GetFileName(file);
                SetupError.IsVisible = false;
            } catch (Exception e) {
                ShowSetupError(ThemeManager.GetString("voicebank.create.photo.invalid"));
                Serilog.Log.Error(e, "Loading the voicebank portrait failed");
            }
        }

        void OnRemovePortrait(object sender, RoutedEventArgs args) {
            portraitPath = null;
            PortraitPreview.Source = null;
            PortraitName.Text = ThemeManager.GetString("voicebank.create.photo.none");
        }

        void OnStart(object sender, RoutedEventArgs args) {
            var name = (NameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) {
                ShowSetupError(ThemeManager.GetString("voicebank.create.needsname"));
                return;
            }
            tempDir = Path.Combine(Path.GetTempPath(), "sekai-voicebank-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            index = 0;
            SetupPanel.IsVisible = false;
            RecordPanel.IsVisible = true;
            StartButton.IsVisible = false;
            FinishButton.IsVisible = true;
            UpdatePrompt();
        }

        void OnRecordToggle(object sender, RoutedEventArgs args) {
            if (recording) {
                StopCapture(keep: true);
            } else {
                StartCapture();
            }
        }

        void StartCapture() {
            if (index >= script.Count) {
                return;
            }
            try {
                // The device can be changed between two takes: recreate the capture then.
                if (capture == null || capture.DeviceNumber != Math.Max(0, micDevice)) {
                    capture?.Dispose();
                    capture = new WaveInEvent {
                        WaveFormat = new WaveFormat(SampleRate, 16, 1),
                        BufferMilliseconds = 50,
                        DeviceNumber = Math.Max(0, micDevice),
                    };
                    capture.DataAvailable += OnCaptureData;
                }
                take.Clear();
                silentMs = 0;
                RecordError.IsVisible = false;
                NoticeText.IsVisible = false;
                capture.StartRecording();
                takeTimer?.Start();
                recording = true;
                RecordButton.Content = ThemeManager.GetString("voicebank.record.stop");
                PlayButton.IsEnabled = false;
                SkipButton.IsEnabled = false;
                SyllableHint.Text = ThemeManager.GetString("voicebank.record.recording");
            } catch (Exception e) {
                Serilog.Log.Error(e, "Starting microphone capture failed");
                ShowRecordError(ThemeManager.GetString("voicebank.record.nomic"));
                capture?.Dispose();
                capture = null;
                recording = false;
                RecordButton.Content = ThemeManager.GetString("voicebank.record.record");
            }
        }

        void OnCaptureData(object? sender, WaveInEventArgs args) {
            short[] chunk = new short[args.BytesRecorded / 2];
            Buffer.BlockCopy(args.Buffer, 0, chunk, 0, args.BytesRecorded);
            int peak = 0;
            foreach (short sample in chunk) {
                int value = Math.Abs((int)sample);
                if (value > peak) {
                    peak = value;
                }
            }
            int totalMs;
            lock (take) {
                take.AddRange(chunk);
                totalMs = take.Count * 1000 / SampleRate;
            }
            // The pause before the first word must not end the take, so the silence only counts
            // once enough of the syllable has been captured.
            if (peak < SilenceThreshold) {
                silentMs += chunk.Length * 1000 / SampleRate;
            } else {
                silentMs = 0;
            }
            double level = Math.Min(100, peak / 32768.0 * 100 * 3);
            Dispatcher.UIThread.Post(() => {
                if (recording) {
                    LevelBar.Value = level;
                }
            });
            if (autoStop && silentMs >= AutoStopSilenceMs && totalMs >= AutoStopMinMs) {
                silentMs = 0;
                Dispatcher.UIThread.Post(() => {
                    if (recording) {
                        StopCapture(keep: true);
                    }
                });
            }
        }

        void StopCapture(bool keep) {
            if (capture != null && recording) {
                try {
                    capture.StopRecording();
                } catch (Exception e) {
                    Serilog.Log.Error(e, "Stopping microphone capture failed");
                }
            }
            recording = false;
            takeTimer?.Stop();
            LevelBar.Value = 0;
            RecordButton.Content = ThemeManager.GetString("voicebank.record.record");
            PlayButton.IsEnabled = true;
            SkipButton.IsEnabled = true;

            var syllable = script[index];
            short[] samples;
            lock (take) {
                samples = take.ToArray();
            }
            var (start, length) = VoicebankRecording.TrimSilence(samples, SampleRate);
            if (length <= 0) {
                ShowRecordError(ThemeManager.GetString("voicebank.record.empty"));
                UpdatePrompt();
                return;
            }
            if (length * 1000 / SampleRate < MinTakeMs) {
                ShowRecordError(ThemeManager.GetString("voicebank.record.short"));
                UpdatePrompt();
                return;
            }
            if (tempDir == null) {
                return;
            }
            if (VoicebankRecording.IsClipped(samples, start, length)) {
                ShowNotice(Localized("voicebank.record.clipped",
                    "The take is too loud: it clips. Lower the microphone level and record it again."));
            }
            // Levelled copy: every take of the bank ends up at the same loudness.
            var kept = levelTakes
                ? VoicebankRecording.LevelTake(samples, start, length)
                : Slice(samples, start, length);
            string file = VoicebankRecording.FileName(syllable);
            string path = Path.Combine(tempDir, file);
            try {
                using (var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 1))) {
                    var bytes = new byte[kept.Length * 2];
                    Buffer.BlockCopy(kept, 0, bytes, 0, bytes.Length);
                    writer.Write(bytes, 0, bytes.Length);
                }
            } catch (Exception e) {
                Serilog.Log.Error(e, "Writing the recorded sample failed");
                ShowRecordError(e.Message);
                return;
            }
            if (!takes.ContainsKey(syllable.Alias)) {
                takeOrder.Add(syllable.Alias);
            }
            takes[syllable.Alias] = path;
            if (autoPlay) {
                PlayFile(path);
            }
            if (keep) {
                Advance();
            }
        }

        static short[] Slice(short[] samples, int start, int length) {
            var copy = new short[length];
            Array.Copy(samples, start, copy, 0, length);
            return copy;
        }

        /// <summary>Lists the microphones of the machine so a laptop's built-in one is not forced on the user.</summary>
        void FillMics() {
            var names = new List<string> { Localized("voicebank.record.mic.default", "Default microphone") };
            micDevices.Clear();
            try {
                for (int i = 0; i < WaveInEvent.DeviceCount; i++) {
                    var caps = WaveInEvent.GetCapabilities(i);
                    names.Add(string.IsNullOrWhiteSpace(caps.ProductName) ? $"Micro {i + 1}" : caps.ProductName);
                    micDevices.Add(i);
                }
            } catch (Exception e) {
                // No audio device at all: the default entry stays and capture will report the error.
                Serilog.Log.Error(e, "Listing the microphones failed");
            }
            MicBox.ItemsSource = names;
            MicBox.SelectedIndex = 0;
            MicBox.SelectionChanged += (_, _) => {
                int selected = MicBox.SelectedIndex;
                micDevice = selected > 0 && selected - 1 < micDevices.Count ? micDevices[selected - 1] : -1;
            };
        }

        void Advance() {
            index++;
            UpdatePrompt();
        }

        void UpdatePrompt() {
            RecordError.IsVisible = false;
            ScriptBar.Maximum = Math.Max(1, script.Count);
            ScriptBar.Value = takes.Count;
            PrevButton.IsEnabled = index > 0;
            NextButton.IsEnabled = index < script.Count - 1;
            if (index >= script.Count) {
                SyllableText.Text = "✓";
                SyllableHint.Text = ThemeManager.GetString("voicebank.record.all");
                RecordButton.IsEnabled = false;
                PlayButton.IsEnabled = false;
                SkipButton.IsEnabled = false;
                ProgressText.Text = string.Format(ThemeManager.GetString("voicebank.record.progress"),
                    script.Count, script.Count);
            } else {
                var syllable = script[index];
                bool done = takes.ContainsKey(syllable.Alias);
                SyllableText.Text = syllable.Alias;
                string hint = done
                    ? Localized("voicebank.record.redo", "Already recorded: you can do it again.")
                    : Localized("voicebank.record.hint", "Say the syllable, then pause (or click Stop).");
                SyllableHint.Text = string.IsNullOrEmpty(syllable.Kana) ? hint : $"{syllable.Kana}\n{hint}";
                RecordButton.IsEnabled = true;
                PlayButton.IsEnabled = done;
                SkipButton.IsEnabled = true;
                ProgressText.Text = string.Format(ThemeManager.GetString("voicebank.record.progress"),
                    index + 1, script.Count);
            }
            RecordedText.Text = string.Format(ThemeManager.GetString("voicebank.record.recorded"), takes.Count);
        }

        void OnPlay(object sender, RoutedEventArgs args) {
            if (index >= script.Count) {
                return;
            }
            if (!takes.TryGetValue(script[index].Alias, out var path)) {
                return;
            }
            PlayFile(path);
        }

        void PlayFile(string path) {
            StopPlayback();
            try {
                player = new AudioFileReader(path);
                playback = new WaveOutEvent();
                playback.Init(player);
                playback.Play();
            } catch (Exception e) {
                Serilog.Log.Error(e, "Playing back the recorded sample failed");
                ShowRecordError(e.Message);
            }
        }

        /// <summary>
        /// Skips to the next syllable that still has no take, so finishing a bank that was
        /// recorded in several sittings does not mean clicking through the done ones.
        /// </summary>
        void OnSkip(object sender, RoutedEventArgs args) {
            if (recording) {
                StopCapture(keep: false);
                return;
            }
            for (int i = index + 1; i < script.Count; i++) {
                if (!takes.ContainsKey(script[i].Alias)) {
                    index = i;
                    UpdatePrompt();
                    return;
                }
            }
            for (int i = 0; i < script.Count; i++) {
                if (!takes.ContainsKey(script[i].Alias)) {
                    index = i;
                    UpdatePrompt();
                    return;
                }
            }
        }

        void OnPrevious(object sender, RoutedEventArgs args) => Step(-1);

        void OnNext(object sender, RoutedEventArgs args) => Step(1);

        /// <summary>Moves through the script; while recording, the take is kept and the script advances by itself.</summary>
        void Step(int delta) {
            if (recording) {
                StopCapture(keep: true);
                return;
            }
            int next = Math.Clamp(index + delta, 0, Math.Max(0, script.Count - 1));
            if (next == index) {
                return;
            }
            index = next;
            UpdatePrompt();
        }

        void OnFinish(object sender, RoutedEventArgs args) {
            if (recording) {
                StopCapture(keep: true);
            }
            if (takes.Count == 0) {
                ShowRecordError(ThemeManager.GetString("voicebank.record.nothing"));
                return;
            }
            string name = (NameBox.Text ?? string.Empty).Trim();
            try {
                var dir =                 VoicebankCreator.Create(name, (AuthorBox.Text ?? string.Empty).Trim(), photoPath, portraitPath,
                    language: language.Id);
                var recorded = new List<RecordingSyllable>();
                foreach (string alias in takeOrder) {
                    var syllable = script.First(s => s.Alias == alias);
                    File.Copy(takes[alias], Path.Combine(dir, VoicebankRecording.FileName(syllable)), overwrite: true);
                    recorded.Add(syllable);
                }
                // Each take was cut around its sound when it was recorded, so the WAV starts on
                // the syllable and the automatic oto must not look for an onset inside it.
                VoicebankRecording.WriteOto(dir, recorded, otoMode, trimmedTakes: true);
                created = true;
                SingerManager.Inst.SearchAllSingers();
                DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
                Close(dir);
            } catch (IOException) {
                ShowRecordError(ThemeManager.GetString("voicebank.create.exists"));
            } catch (Exception e) {
                Serilog.Log.Error(e, "Creating the recorded voicebank failed");
                ShowRecordError(e.Message);
            }
        }

        void OnCancel(object sender, RoutedEventArgs args) {
            Close();
        }

        void OnDialogKeyDown(object? sender, KeyEventArgs args) {
            if (!RecordPanel.IsVisible) {
                return;
            }
            switch (args.Key) {
                case Key.Space:
                    args.Handled = true;
                    OnRecordToggle(this, new RoutedEventArgs());
                    break;
                case Key.Left:
                    args.Handled = true;
                    Step(-1);
                    break;
                case Key.Right:
                    args.Handled = true;
                    Step(1);
                    break;
                case Key.Enter:
                    // Enter creates the voicebank, so a session is never only keyboard-driven
                    // until the very end.
                    if (!recording && takes.Count > 0) {
                        args.Handled = true;
                        OnFinish(this, new RoutedEventArgs());
                    }
                    break;
            }
        }

        /// <summary>
        /// A close with recorded takes used to throw them away without a word. Asking once is
        /// enough: a half-recorded bank takes a long time to make again.
        /// </summary>
        async void OnClosingGuard(object? sender, WindowClosingEventArgs args) {
            if (created || closingConfirmed || takes.Count == 0) {
                return;
            }
            args.Cancel = true;
            var answer = await MessageBox.Show(this,
                string.Format(Localized("voicebank.record.close.save",
                    "You already recorded {0} syllable(s). Create the voicebank with them?"), takes.Count),
                Title ?? "SEKAI UTAU",
                MessageBox.MessageBoxButtons.YesNo);
            closingConfirmed = true;
            if (answer == MessageBox.MessageBoxResult.Yes) {
                OnFinish(this, new RoutedEventArgs());
            } else {
                Close();
            }
        }

        void OnLanguageChanged() {
            int index = LanguageBox.SelectedIndex;
            var picked = index >= 0 && index < languages.Count ? languages[index] : VoicebankLanguages.Default;
            if (picked == language) {
                return;
            }
            language = picked;
            LanguageHint.Text = language.Phonemizer;
            FillScriptChoices();
            script = VoicebankLanguages.Script(language, fullScript);
            ClearTakes();
        }

        void FillScriptChoices() {
            ScriptBox.ItemsSource = new[] {
                string.Format(Localized("voicebank.record.script.basic"), language.Essentials.Count),
                string.Format(Localized("voicebank.record.script.full"), language.Full.Count),
            };
            ScriptBox.SelectedIndex = fullScript ? 1 : 0;
        }

        static string LanguageName(VoicebankLanguage language) =>
            Localized($"voicebank.language.{language.Id}", language.Name);

        void UpdateOtoHint() {
            OtoHint.Text = Localized(otoMode == OtoMode.Automatic
                ? "voicebank.record.oto.auto.hint"
                : "voicebank.record.oto.manual.hint");
        }

        static readonly Dictionary<string, string> OtoStrings = new() {
            ["voicebank.record.mic"] = "Microphone",
            ["voicebank.record.mic.default"] = "Default microphone",
            ["voicebank.record.level"] = "Level the takes",
            ["voicebank.record.autoplay"] = "Play the take back",
            ["voicebank.record.close.save"] =
                "You already recorded {0} syllable(s). Create the voicebank with them?",
            ["voicebank.record.clipped"] =
                "The take is too loud: it clips. Lower the microphone level and record it again.",
            ["voicebank.record.prev"] = "◀",
            ["voicebank.record.next"] = "▶",
            ["voicebank.record.autostop"] = "Stop automatically",
            ["voicebank.record.redo"] = "Already recorded: you can do it again.",
            ["voicebank.record.hint"] = "Say the syllable, then pause (or click Stop).",
            ["voicebank.record.oto"] = "Cut points (oto.ini)",
            ["voicebank.record.oto.auto"] = "Automatic: estimated from each take",
            ["voicebank.record.oto.manual"] = "Manual: I tune it in vLabeler",
            ["voicebank.record.oto.auto.hint"] =
                "The application finds the start of the sound, of the vowel and of the consonant in every take and writes the oto.ini from it.",
            ["voicebank.record.oto.manual.hint"] =
                "The oto.ini is written at zero with the romaji and kana aliases: tune it in vLabeler, SetParam or the oto editor of the voice window.",
        };

        /// <summary>Localised label, with an English fallback for languages not translated yet.</summary>
        static string Localized(string key) => Localized(key, key);

        static string Localized(string key, string fallback) {
            if (ThemeManager.TryGetString(key, out var value) && value != key && !string.IsNullOrEmpty(value)) {
                return value;
            }
            return OtoStrings.TryGetValue(key, out var known) ? known : fallback;
        }

        void ClearTakes() {
            takes.Clear();
            takeOrder.Clear();
        }

        void ShowSetupError(string message) {
            SetupError.Text = message;
            SetupError.IsVisible = true;
        }

        void ShowRecordError(string message) {
            RecordError.Text = message;
            RecordError.IsVisible = true;
        }

        /// <summary>A warning that does not stop the take from being kept (clipping, for instance).</summary>
        void ShowNotice(string message) {
            NoticeText.Text = message;
            NoticeText.IsVisible = true;
        }

        void StopPlayback() {
            try {
                playback?.Stop();
                playback?.Dispose();
                player?.Dispose();
            } catch (Exception) {
                // Playback teardown never blocks the dialog.
            }
            playback = null;
            player = null;
        }

        void Cleanup() {
            recording = false;
            try {
                capture?.StopRecording();
            } catch (Exception) {
                // Ignore: the device may already be gone.
            }
            capture?.Dispose();
            capture = null;
            StopPlayback();
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir)) {
                try {
                    Directory.Delete(tempDir, true);
                } catch (Exception) {
                    // A leftover temp folder is harmless.
                }
            }
        }
    }
}
