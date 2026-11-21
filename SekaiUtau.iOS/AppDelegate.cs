using Avalonia;
using Avalonia.iOS;
using Foundation;
using SekaiUtau.Mobile;

namespace SekaiUtau.iOS;

/// <summary>
/// iOS entry point: it hosts the shared mobile UI. The singing engine of the
/// desktop app is not ported yet, so this edition reads projects only.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App> {
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        return base.CustomizeAppBuilder(builder);
    }
}
