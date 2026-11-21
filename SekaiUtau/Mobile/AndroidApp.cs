using System;
using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace SekaiUtau.App;

/// <summary>
/// Android Application class. Avalonia needs it to know which App type to build.
/// </summary>
[Application(Label = "SEKAI UTAU")]
public class AndroidApp : AvaloniaAndroidApplication<App> {
    public AndroidApp(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) {
    }
}
