using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using SekaiUtau.Mobile;

namespace SekaiUtau.Android;

/// <summary>
/// Android entry point. It hosts the Avalonia UI of the mobile edition; the singing
/// engine of the desktop app still relies on Windows processes, so this edition is
/// limited to browsing projects for now.
/// </summary>
[Activity(
    Label = "SEKAI UTAU",
    Theme = "@style/SekaiTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity {
}
