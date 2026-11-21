using System;
using System.IO;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace SekaiUtau.Voicebank;

/// <summary>
/// Android side of the shared recording screen: the voicebank is built in the
/// app cache and the finished ZIP is published in Downloads/SEKAI UTAU.
/// </summary>
public sealed class AndroidPlatform : IVoicebankPlatform {
    public int SampleRate => MicRecorder.SampleRate;

    public string CacheDirectory => Android.App.Application.Context.CacheDir!.AbsolutePath;

    public IMicRecorder CreateRecorder() => new MicRecorder();

    /// <summary>Copies the ZIP into the phone Downloads folder and returns where it landed.</summary>
    public string Save(string zipPath) {
        var context = Android.App.Application.Context;
        string fileName = Path.GetFileName(zipPath);
        try {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) {
                var values = new ContentValues();
                values.Put("_display_name", fileName);
                values.Put("mime_type", "application/zip");
                values.Put("relative_path", Android.OS.Environment.DirectoryDownloads + "/SEKAI UTAU");
                var resolver = context.ContentResolver!;
                var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri!, values);
                if (uri != null) {
                    using (var output = resolver.OpenOutputStream(uri)) {
                        using var input = File.OpenRead(zipPath);
                        input.CopyTo(output!);
                    }
                    return "Téléchargements/SEKAI UTAU/" + fileName;
                }
            }
        } catch (Exception) {
            // fall through to the app folder
        }
        try {
            var downloads = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads);
            var target = Path.Combine(downloads!.AbsolutePath, "SEKAI UTAU");
            Directory.CreateDirectory(target);
            var destination = Path.Combine(target, fileName);
            File.Copy(zipPath, destination, true);
            return destination;
        } catch (Exception) {
            var fallback = context.GetExternalFilesDir(null)!.AbsolutePath;
            var destination = Path.Combine(fallback, fileName);
            File.Copy(zipPath, destination, true);
            return destination;
        }
    }
}
