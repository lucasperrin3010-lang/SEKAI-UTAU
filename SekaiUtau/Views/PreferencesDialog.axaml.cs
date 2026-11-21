using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SekaiUtau.App.ViewModels;
using SekaiUtau.Colors;
using SekaiUtau.Core;

namespace SekaiUtau.App.Views {
    public partial class PreferencesDialog : Window {
        private PreferencesViewModel? viewModel => this.DataContext as PreferencesViewModel;   

        public PreferencesDialog() {
            InitializeComponent();
        }

        void OnMetronomeSliderPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (sender is not Slider slider || viewModel == null) {
                return;
            }
            var point = e.GetCurrentPoint(slider);
            if (!point.Properties.IsRightButtonPressed) {
                return;
            }
            switch (slider.Tag as string) {
                case "MetronomeVolume":
                    viewModel.ResetMetronomeVolume();
                    break;
                case "MetronomeHighFrequency":
                    viewModel.ResetMetronomeHighFrequency();
                    break;
                case "MetronomeLowFrequency":
                    viewModel.ResetMetronomeLowFrequency();
                    break;
            }
            e.Handled = true;
        }

        void OpenSingersFolder(object sender, RoutedEventArgs e) {
            try {
                Directory.CreateDirectory(viewModel!.SingerPath);
                OS.OpenFolder(viewModel!.SingerPath);
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OpenAddlSingersFolder(object sender, RoutedEventArgs e) {
            try {
                if (Directory.Exists(viewModel!.AdditionalSingersPath)) {
                    OS.OpenFolder(viewModel!.AdditionalSingersPath);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void ResetAddlSingersPath(object sender, RoutedEventArgs e) {
            viewModel!.SetAddlSingersPath(string.Empty);
        }

        async void SelectRecordingFolder(object sender, RoutedEventArgs e) {
            try {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                    AllowMultiple = false,
                });
                var folder = folders.FirstOrDefault();
                if (folder == null) {
                    return;
                }
                string? path = folder.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) {
                    viewModel!.SetRecordingOutputFolder(path);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void ResetRecordingFolder(object sender, RoutedEventArgs e) {
            viewModel!.SetRecordingOutputFolder(string.Empty);
        }

        async void SelectAddlSingersPath(object sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFolderAboutSinger(this, "prefs.paths.addlsinger");
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (Directory.Exists(path)) {
                viewModel!.SetAddlSingersPath(path);
            }
        }

        async void ReloadSingers(object sender, RoutedEventArgs e) {
            LoadingWindow.BeginLoading(this);
            await Task.Run(() => {
                SingerManager.Inst.SearchAllSingers();
            });
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            LoadingWindow.EndLoading();
        }

        void ResetVLabelerPath(object sender, RoutedEventArgs e) {
            viewModel!.SetVLabelerPath(string.Empty);
        }

        async void SelectVLabelerPath(object sender, RoutedEventArgs e) {
            var type = OS.IsWindows() ? FilePicker.EXE : OS.IsMacOS() ? FilePicker.APP : FilePickerFileTypes.All;
            var path = await FilePicker.OpenFile(this, "prefs.advanced.vlabelerpath", type);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (OS.AppExists(path)) {
                viewModel!.SetVLabelerPath(path);
            }
        }

        void ResetSetParamPath(object sender, RoutedEventArgs e) {
            viewModel!.SetSetParamPath(string.Empty);
        }

        async void SelectSetParamPath(object sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFile(this, "prefs.otoeditor.setparampath", FilePicker.EXE);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (File.Exists(path)) {
                viewModel!.SetSetParamPath(path);
            }
        }

        async void SelectUiFont(object sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFile(this, "prefs.appearance.customfont.select", FilePicker.FontFiles);
            if (!string.IsNullOrEmpty(path) && UiFontManager.IsSupportedFontPath(path)) {
                viewModel!.SetUiFontPath(path);
            }
        }

        void ResetUiFont(object sender, RoutedEventArgs e) {
            viewModel!.ResetUiFont();
        }

        async void SelectWallpaper(object sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFile(this, "prefs.appearance.wallpaper", FilePicker.ImageFiles);
            if (!string.IsNullOrEmpty(path)) {
                viewModel!.SetWallpaperPath(path);
            }
        }

        void ClearWallpaper(object sender, RoutedEventArgs e) {
            viewModel!.ClearWallpaper();
        }
        
        void ResetWinePath(object sender, RoutedEventArgs e) {
            ((PreferencesViewModel)DataContext!).SetWinePath(string.Empty);
        }

        async void SelectWinePath(object sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFile(this, "prefs.advanced.winepath", FilePicker.UnixExecutable);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (File.Exists(path)) {
                ((PreferencesViewModel)DataContext!).SetWinePath(path);
            }
        }

        void DetectWinePath(object sender, RoutedEventArgs e) {
            string[] wineNames = { "wine", "wine64", "wine32", "wine32on64" };
            string winePath = string.Empty;

            foreach (string wineName in wineNames) {
                winePath = OS.WhereIs(wineName);
                if (!string.IsNullOrEmpty(winePath)) {
                    break;
                }
            }

            if (string.IsNullOrEmpty(winePath)) {
                return;
            }

            ((PreferencesViewModel)DataContext!).SetWinePath(winePath);
        }

        void OnResetUiColors(object sender, RoutedEventArgs e) {
            viewModel?.ResetUiColors();
        }

        async void OpenCustomThemeEditor(object sender, RoutedEventArgs e) {
            try {
                string? path = viewModel?.EnsureEditableThemePath();
                if (string.IsNullOrEmpty(path)) {
                    await MessageBox.Show(this,
                        ThemeManager.GetString("prefs.appearance.customtheme.edit.failed"),
                        ThemeManager.GetString("prefs.appearance.customtheme.edit.title"),
                        MessageBox.MessageBoxButtons.Ok);
                    return;
                }
                ThemeEditorWindow.Show(path);
            } catch (Exception ex) {
                Serilog.Log.Error(ex, "Failed to open the theme editor");
                await MessageBox.Show(this, ex.Message,
                    ThemeManager.GetString("prefs.appearance.customtheme.edit.title"),
                    MessageBox.MessageBoxButtons.Ok);
            }
        }

        void OnCustomThemeCreate(object sender, RoutedEventArgs e) {
            var dialog = new TypeInDialog {
                Title = ThemeManager.GetString("prefs.appearance.customtheme.create.title")
            };
            dialog.SetPrompt(ThemeManager.GetString("prefs.appearance.customtheme.create.prompt"));
            dialog.onFinish = s => {
                if (string.IsNullOrEmpty(s)) {
                    MessageBox.ShowModal(this, 
                        ThemeManager.GetString("prefs.appearance.customtheme.create.empty"),
                        ThemeManager.GetString("prefs.appearance.customtheme.create.title"));
                    return;
                }

                string filename = string.Join("", s.Where(c => Char.IsLetterOrDigit(c) || c == ' '))
                                        .Replace(" ", "-").ToLower() + ".yaml";

                var themeYaml = new CustomTheme.ThemeYaml { Name = s };

                File.WriteAllText(Path.Join(PathManager.Inst.ThemesPath, filename),
                    Yaml.DefaultSerializer.Serialize(themeYaml));
                viewModel!.RefreshThemes();
            };
            dialog.ShowDialog(this);
        }

        async void OnCustomThemeDelete(object sender, RoutedEventArgs e) {
            if (CustomTheme.IsPackageTheme(viewModel!.ThemeName)) return;
            var result = await MessageBox.Show(
                this,
                ThemeManager.GetString("prefs.appearance.customtheme.delete.message"),
                ThemeManager.GetString("prefs.appearance.customtheme.delete.title"),
                MessageBox.MessageBoxButtons.YesNo);
            if (result == MessageBox.MessageBoxResult.Yes) {
                string previousTheme = viewModel!.ThemeItems.TakeWhile(x => x != viewModel!.ThemeName).LastOrDefault()!;
                File.Delete(CustomTheme.Themes[viewModel!.ThemeName]);
                viewModel!.RefreshThemes();
                viewModel!.ThemeName = previousTheme;
            }
        }
    }
}
