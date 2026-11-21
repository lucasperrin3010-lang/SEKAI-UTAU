using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SekaiUtau.Core;

namespace SekaiUtau.App.Views {
    /// <summary>
    /// Asks for a name, an author and an optional picture, then creates an empty voicebank
    /// folder in the singers directory and refreshes the singer list.
    /// </summary>
    public partial class CreateVoicebankDialog : Window {
        string? photoPath;
        string? portraitPath;

        public CreateVoicebankDialog() {
            InitializeComponent();
            PathBox.Text = VoicebankCreator.SingersRoot;
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
                ErrorBox.IsVisible = false;
            } catch (Exception e) {
                ShowError(ThemeManager.GetString("voicebank.create.photo.invalid"));
                LogError(e);
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
                ErrorBox.IsVisible = false;
            } catch (Exception e) {
                ShowError(ThemeManager.GetString("voicebank.create.photo.invalid"));
                LogError(e);
            }
        }

        void OnRemovePortrait(object sender, RoutedEventArgs args) {
            portraitPath = null;
            PortraitPreview.Source = null;
            PortraitName.Text = ThemeManager.GetString("voicebank.create.photo.none");
        }

        void OnCancel(object sender, RoutedEventArgs args) {
            Close();
        }

        void OnCreate(object sender, RoutedEventArgs args) {
            var name = NameBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) {
                ShowError(ThemeManager.GetString("voicebank.create.needsname"));
                return;
            }
            try {
                var dir = VoicebankCreator.Create(name, AuthorBox.Text ?? string.Empty, photoPath, portraitPath);
                SingerManager.Inst.SearchAllSingers();
                DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
                Close(dir);
            } catch (IOException) {
                ShowError(ThemeManager.GetString("voicebank.create.exists"));
            } catch (Exception e) {
                ShowError(e.Message);
                LogError(e);
            }
        }

        void ShowError(string message) {
            ErrorBox.Text = message;
            ErrorBox.IsVisible = true;
        }

        static void LogError(Exception e) => Serilog.Log.Error(e, "Create voicebank failed");
    }
}
