using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;

[assembly: UsesPermission(Manifest.Permission.RecordAudio)]

namespace SekaiUtau.Voicebank;

/// <summary>
/// Android entry point of the voicebank app. It asks for the microphone when the
/// app starts, since recording is the whole point of this edition.
/// </summary>
[Activity(
    Label = "SEKAI UTAU Voicebank",
    Theme = "@style/SekaiTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity {
    const int MicrophoneRequest = 1001;

    protected override void OnCreate(Bundle? savedInstanceState) {
        // Avalonia keeps the single view alive across activities; without detaching it
        // first, a relaunch tries to add it under the new root and crashes.
        DetachMainView();
        base.OnCreate(savedInstanceState);
        if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted) {
            RequestPermissions(new[] { Manifest.Permission.RecordAudio }, MicrophoneRequest);
        }
    }

    static void DetachMainView() {
        if (Avalonia.Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime single) {
            return;
        }
        if (single.MainView is not Control view) {
            return;
        }
        switch (view.Parent) {
            case ContentPresenter presenter:
                presenter.Content = null;
                break;
            case ContentControl content:
                content.Content = null;
                break;
            case Panel panel:
                panel.Children.Remove(view);
                break;
        }
    }
}
