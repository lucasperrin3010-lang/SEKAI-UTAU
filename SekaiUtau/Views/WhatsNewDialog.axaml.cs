using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SekaiUtau.App.ViewModels;
using SekaiUtau.Core;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.App.Views {
    /// <summary>Shows what changed in the running version, with illustrations.</summary>
    public partial class WhatsNewDialog : Window {
        public readonly WhatsNewViewModel ViewModel;

        public WhatsNewDialog() {
            InitializeComponent();
            DataContext = ViewModel = new WhatsNewViewModel();
        }

        void OnClose(object sender, RoutedEventArgs args) => Close();

        void OnGithub(object sender, RoutedEventArgs args) {
            try {
                OS.OpenWeb("https://github.com/lucasperrin3010-lang/SEKAI-UTAU/releases");
            } catch (Exception e) {
                Log.Error(e, "Failed to open the releases page.");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
    }
}
