using System;
using System.IO;

namespace SekaiUtau.Voicebank;

/// <summary>
/// Microphone used by the recording screen. Android records with AudioRecord,
/// iOS with AVAudioRecorder, and both hand back plain 16-bit PCM samples.
/// </summary>
public interface IMicRecorder : IDisposable {
    bool IsRecording { get; }
    void Start();
    short[] Stop();
}

/// <summary>
/// Everything the shared screen cannot do by itself: where the temporary
/// voicebank is built and where the finished ZIP is handed to the user.
/// </summary>
public interface IVoicebankPlatform {
    int SampleRate { get; }
    string CacheDirectory { get; }
    IMicRecorder CreateRecorder();
    string Save(string zipPath);
}

/// <summary>
/// Keeps the platform side of the app. Each entry point (AndroidApp, iOS
/// AppDelegate) calls <see cref="Use"/> before Avalonia builds the view.
/// </summary>
public static class VoicebankHost {
    static IVoicebankPlatform platform = new FallbackPlatform();

    public static IVoicebankPlatform Platform => platform;

    public static void Use(IVoicebankPlatform instance) {
        platform = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    /// <summary>Used only when no platform registered itself (tests, designer).</summary>
    sealed class FallbackPlatform : IVoicebankPlatform {
        public int SampleRate => 44100;

        public string CacheDirectory {
            get {
                string dir = Path.Combine(Path.GetTempPath(), "sekai-voicebank");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public IMicRecorder CreateRecorder() => new SilentRecorder();

        public string Save(string zipPath) {
            string target = Path.Combine(CacheDirectory, Path.GetFileName(zipPath));
            File.Copy(zipPath, target, true);
            return target;
        }

        sealed class SilentRecorder : IMicRecorder {
            public bool IsRecording { get; private set; }

            public void Start() {
                throw new InvalidOperationException("Aucun micro sur cette plateforme.");
            }

            public short[] Stop() => Array.Empty<short>();

            public void Dispose() { }
        }
    }
}
