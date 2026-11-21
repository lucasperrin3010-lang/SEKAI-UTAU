using Avalonia.Controls;
using Avalonia.Interactivity;
using SekaiUtau.App.ViewModels;
using SekaiUtau.Core;
using SekaiUtau.Core.Ustx;

namespace SekaiUtau.App.Views {
    public partial class TrackSettingsDialog : Window {

        TrackSettingsViewModel viewModel;

        public TrackSettingsDialog() : this(new UTrack(DocManager.Inst.Project)) { }

        public TrackSettingsDialog(UTrack track) {
            InitializeComponent();
            DataContext = viewModel = new TrackSettingsViewModel(track);
        }

        public void OnOkClicked(object sender, RoutedEventArgs e) {
            viewModel.Finish();
            Close();
        }
    }
}
