using System;
using System.IO;
using AudioToolbox;
using AVFoundation;
using Foundation;

namespace SekaiUtau.Voicebank;

/// <summary>
/// iPhone microphone: AVAudioRecorder writes a temporary 16-bit mono 44.1 kHz WAV
/// per take and the samples are read back for the shared recording screen, so the
/// rest of the app works exactly like on Android.
/// </summary>
public sealed class MicRecorder : IMicRecorder {
    public const int SampleRate = 44100;

    AVAudioRecorder? recorder;
    string? takePath;

    public bool IsRecording { get; private set; }

    public void Start() {
        if (IsRecording) {
            return;
        }
        var session = AVAudioSession.SharedInstance();
        // A refused session shows up as a null recorder below, with Apple's own
        // error message, so the results of these two calls are not checked here.
        session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker);
        session.SetActive(true, default(AVAudioSessionSetActiveOptions));
        string dir = VoicebankHost.Platform.CacheDirectory;
        takePath = Path.Combine(dir, "prise.wav");
        if (File.Exists(takePath)) {
            File.Delete(takePath);
        }
        var settings = new AudioSettings {
            Format = AudioFormatType.LinearPCM,
            SampleRate = SampleRate,
            NumberChannels = 1,
            LinearPcmBitDepth = 16,
            LinearPcmBigEndian = false,
            LinearPcmFloat = false,
        };
        recorder = AVAudioRecorder.Create(NSUrl.FromFilename(takePath), settings, out var error);
        if (recorder is null) {
            throw new InvalidOperationException("Micro indisponible : " + Message(error)
                + " (autorise le micro pour SEKAI UTAU dans Réglages).");
        }
        recorder.PrepareToRecord();
        if (!recorder.Record()) {
            recorder.Dispose();
            recorder = null;
            throw new InvalidOperationException("L'enregistrement n'a pas démarré : vérifie l'autorisation du micro.");
        }
        IsRecording = true;
    }

    public short[] Stop() {
        if (!IsRecording) {
            return Array.Empty<short>();
        }
        IsRecording = false;
        recorder?.Stop();
        recorder?.Dispose();
        recorder = null;
        try {
            AVAudioSession.SharedInstance().SetActive(false, default(AVAudioSessionSetActiveOptions));
        } catch (Exception) {
            // keeping the session open only costs battery
        }
        if (takePath is null || !File.Exists(takePath)) {
            return Array.Empty<short>();
        }
        try {
            return ReadWav(takePath);
        } finally {
            try {
                File.Delete(takePath);
            } catch (Exception) {
                // a stale temp file is harmless
            }
        }
    }

    public void Dispose() {
        if (IsRecording) {
            Stop();
        }
    }

    static string Message(NSError? error) => error?.LocalizedDescription ?? "raison inconnue";

    /// <summary>Reads the PCM frames of a RIFF/WAVE file as mono 16-bit samples.</summary>
    static short[] ReadWav(string path) {
        byte[] bytes = File.ReadAllBytes(path);
        int format = 1;
        int channels = 1;
        int bits = 16;
        int dataStart = -1;
        int dataLength = 0;
        for (int pos = 12; pos + 8 <= bytes.Length;) {
            string id = new string(new[] { (char)bytes[pos], (char)bytes[pos + 1], (char)bytes[pos + 2], (char)bytes[pos + 3] });
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;
            if (size < 0) {
                break;
            }
            if (id == "fmt " && body + 16 <= bytes.Length) {
                format = BitConverter.ToInt16(bytes, body);
                channels = Math.Max(1, (int)BitConverter.ToInt16(bytes, body + 2));
                bits = BitConverter.ToInt16(bytes, body + 14);
            } else if (id == "data") {
                dataStart = body;
                dataLength = Math.Min(size, bytes.Length - body);
                break;
            }
            pos = body + size + (size % 2);
        }
        if (dataStart < 0 || dataLength <= 0 || channels <= 0) {
            return Array.Empty<short>();
        }
        int bytesPerSample = Math.Max(1, bits / 8);
        int frames = dataLength / (bytesPerSample * channels);
        if (frames <= 0) {
            return Array.Empty<short>();
        }
        var samples = new short[frames];
        for (int frame = 0; frame < frames; frame++) {
            int sum = 0;
            for (int channel = 0; channel < channels; channel++) {
                int at = dataStart + ((frame * channels) + channel) * bytesPerSample;
                if (at + bytesPerSample > bytes.Length) {
                    Array.Resize(ref samples, frame);
                    return samples;
                }
                int value;
                if (format == 3) {
                    value = (int)(BitConverter.ToSingle(bytes, at) * 32767f);
                } else if (bits == 16) {
                    value = BitConverter.ToInt16(bytes, at);
                } else {
                    value = (bytes[at] - 128) << 8;
                }
                sum += value;
            }
            samples[frame] = (short)Math.Clamp(sum / channels, short.MinValue, short.MaxValue);
        }
        return samples;
    }
}
