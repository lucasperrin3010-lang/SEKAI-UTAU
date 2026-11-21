using System;
using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace SekaiUtau.Voicebank;

/// <summary>
/// Android Application class: it tells Avalonia which App type to build.
/// </summary>
[Application(Label = "SEKAI UTAU Voicebank", Icon = "@mipmap/ic_launcher")]
public class AndroidApp : AvaloniaAndroidApplication<App> {
    public AndroidApp(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) {
        // The shared recording screen asks for the mic and the export folder.
        VoicebankHost.Use(new AndroidPlatform());
    }
}
