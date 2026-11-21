using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace SekaiUtau.App;

/// <summary>
/// Android entry point of the mobile edition. It hosts the very same
/// <see cref="App"/>, so the interface is the one from the PC build.
/// </summary>
[Activity(
    Label = "SEKAI UTAU",
    Theme = "@style/SekaiTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity {
}
