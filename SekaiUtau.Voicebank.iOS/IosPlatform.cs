using System;
using System.IO;
using AVFoundation;

namespace SekaiUtau.Voicebank;

/// <summary>
/// iOS side of the shared recording screen: the ZIP lands in the app Documents
/// folder, which the Files app shows thanks to UIFileSharingEnabled.
/// </summary>
public sealed class IosPlatform : IVoicebankPlatform {
    public IosPlatform() {
        // Ask once at launch so the first take is not refused by a prompt that
        // arrives while AVAudioRecorder is already starting.
        try {
            AVAudioSession.SharedInstance().RequestRecordPermission(granted => { });
        } catch (Exception) {
            // MicRecorder.Start asks again if the permission is still missing.
        }
    }

    public int SampleRate => MicRecorder.SampleRate;

    public string CacheDirectory {
        get {
            string dir = Path.Combine(Path.GetTempPath(), "sekai-voicebank");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public IMicRecorder CreateRecorder() => new MicRecorder();

    public string Save(string zipPath) {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string fileName = Path.GetFileName(zipPath);
        string target = Path.Combine(documents, fileName);
        File.Copy(zipPath, target, true);
        return "Fichiers > Sur mon iPhone > SEKAI UTAU > " + fileName;
    }
}
