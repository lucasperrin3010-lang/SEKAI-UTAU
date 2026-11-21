using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using SekaiUtau.App.Update;
using SekaiUtau.Core;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.App.ViewModels {
    /// <summary>
    /// Compares the running version with the latest GitHub release and installs
    /// the matching package for this platform.
    /// </summary>
    public partial class UpdaterViewModel : ViewModelBase {
        public string AppVersion => $"v{AppUpdater.CurrentVersion}";
        public bool IsDarkMode => ThemeManager.IsDarkMode;

        [Reactive] public partial string UpdaterStatus { get; set; }
        [Reactive] public partial string LatestVersionLabel { get; set; }
        [Reactive] public partial string ReleaseNotes { get; set; }
        [Reactive] public partial bool UpdateAvailable { get; set; }
        [Reactive] public partial FontWeight UpdateButtonFontWeight { get; set; }
        [Reactive] public partial int Channel { get; set; }
        [Reactive] public partial bool UpdateAccepted { get; set; }

        public Action? CloseApplication { get; set; }

        UpdateCandidate? candidate;
        // Bumped on every check so a check still in flight cannot overwrite a newer result.
        int generation;

        public UpdaterViewModel() {
            UpdaterStatus = string.Empty;
            LatestVersionLabel = string.Empty;
            ReleaseNotes = string.Empty;
            UpdateAvailable = false;
            UpdateButtonFontWeight = FontWeight.Normal;
            UpdateAccepted = false;
            Channel = Preferences.Default.Channel switch {
                "beta" => 1,
                "alpha" => 2,
                _ => 0
            };
            _ = InitAsync();
        }

        public async Task InitAsync() {
            int current = ++generation;
            UpdateAvailable = false;
            UpdateButtonFontWeight = FontWeight.Normal;
            UpdaterStatus = ThemeManager.GetString("updater.status.checking");
            var found = await AppUpdater.CheckAsync();
            if (current != generation) {
                return;
            }
            candidate = found;
            if (found == null) {
                LatestVersionLabel = string.Empty;
                ReleaseNotes = string.Empty;
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                return;
            }
            LatestVersionLabel = string.Format(ThemeManager.GetString("updater.latest"), found.Tag);
            ReleaseNotes = Clean(found.ReleaseNotes);
            if (AppUpdater.IsNewer(found.Version)) {
                UpdateAvailable = true;
                UpdateButtonFontWeight = FontWeight.Bold;
                UpdaterStatus = string.Format(ThemeManager.GetString("updater.status.available"), found.Version);
            } else {
                UpdaterStatus = ThemeManager.GetString("updater.status.notavailable");
            }
        }

        /// <summary>Turns the Markdown release body into plain lines for the dialog.</summary>
        static string Clean(string body) {
            if (string.IsNullOrWhiteSpace(body)) {
                return string.Empty;
            }
            var builder = new StringBuilder();
            foreach (string raw in body.Replace("\r\n", "\n").Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0) {
                    continue;
                }
                line = line.TrimStart('#').Trim();
                if (line.StartsWith("- ")) {
                    line = "\u2022 " + line.Substring(2);
                }
                line = line.Replace("**", string.Empty).Replace("`", string.Empty);
                builder.AppendLine(line);
            }
            return builder.ToString().TrimEnd();
        }

        public void OnGithub() {
            try {
                OS.OpenWeb($"https://github.com/{AppUpdater.Repo}/releases");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async void OnUpdate() {
            var target = candidate;
            if (target == null || !AppUpdater.IsNewer(target.Version)) {
                return;
            }
            UpdateAvailable = false;
            UpdateAccepted = true;
            UpdaterStatus = ThemeManager.GetString("updater.install");
            var progress = new Progress<int>(percent =>
                UpdaterStatus = string.Format(ThemeManager.GetString("updater.downloading"), percent));
            bool installed = await AppUpdater.DownloadAndInstallAsync(target, progress);
            if (!installed) {
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                UpdateAvailable = true;
                UpdateAccepted = false;
                return;
            }
            UpdaterStatus = ThemeManager.GetString("updater.restart");
            Log.Information($"Update {target.Tag} installed, restarting.");
            CloseApplication?.Invoke();
        }

        public void OnClosing() {
            if (!UpdateAccepted && candidate != null && AppUpdater.IsNewer(candidate.Version)) {
                Log.Information($"Skipping update {candidate.Version}");
                Preferences.Default.SkipUpdate = $"{Preferences.Default.Channel}:{candidate.Version}";
                Preferences.Save();
            }
        }
    }
}
