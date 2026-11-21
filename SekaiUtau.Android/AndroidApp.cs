using System;
using Android.App;
using Android.Runtime;
using Avalonia.Android;
using SekaiUtau.Mobile;

namespace SekaiUtau.Android;

/// <summary>
/// Android <see cref="Application"/> that hosts the Avalonia mobile application.
/// Avalonia needs this subclass so it knows which Avalonia App type to build.
/// </summary>
[Application(Label = "SEKAI UTAU")]
public class AndroidApp : AvaloniaAndroidApplication<App> {
    public AndroidApp(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) {
    }
}
