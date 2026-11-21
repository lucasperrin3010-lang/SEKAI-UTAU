using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using SekaiUtau.App.Update;
using SekaiUtau.App.ViewModels;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.App.Views {
    public partial class UpdaterDialog : Window {
        public readonly UpdaterViewModel ViewModel;
        public UpdaterDialog() {
            InitializeComponent();
            DataContext = ViewModel = new UpdaterViewModel();
        }

        void OnClosing(object sender, WindowClosingEventArgs e) {
            ViewModel.OnClosing();
        }

        /// <summary>Whether CheckForUpdate runs. UI tests turn it off to stay offline.</summary>
        public static bool CheckForUpdateEnabled { get; set; } = true;

        /// <summary>Asks GitHub for the latest release and opens the dialog when it is newer.</summary>
        public static void CheckForUpdate(Action<Window> showDialog, Action closeApplication, TaskScheduler scheduler) {
            if (!CheckForUpdateEnabled) {
                return;
            }
            Task.Run(async () => {
                var latest = await AppUpdater.CheckAsync();
                if (latest == null || !AppUpdater.IsNewer(latest.Version)) {
                    return false;
                }
                string version = latest.Version.ToString();
                // SkipUpdate is stored as "<channel>:<version>"; the bare form is the legacy format.
                if (Preferences.Default.SkipUpdate == version ||
                    Preferences.Default.SkipUpdate == $"{Preferences.Default.Channel}:{version}") {
                    return false;
                }
                return true;
            }).ContinueWith(t => {
                if (t.IsCompletedSuccessfully && t.Result) {
                    var dialog = new UpdaterDialog();
                    dialog.ViewModel.CloseApplication = closeApplication;
                    showDialog.Invoke(dialog);
                }
                if (t.IsFaulted) {
                    Log.Error(t.Exception, "Failed to check for update");
                }
            }, scheduler);
        }
    }
}
