using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace SekaiUtau.Voicebank;

/// <summary>
/// iOS entry point of the voicebank app: it hosts the same recording screen as
/// the Android edition, with the microphone recorded through AVFoundation.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App> {
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        VoicebankHost.Use(new IosPlatform());
        return base.CustomizeAppBuilder(builder);
    }
}
