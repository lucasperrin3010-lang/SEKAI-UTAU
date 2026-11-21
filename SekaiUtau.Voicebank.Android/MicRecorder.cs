using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.Media;

namespace SekaiUtau.Voicebank;

/// <summary>
/// Records the phone microphone as 16-bit mono PCM, which is what the voicebank
/// WAV files need. One recorder is reused for every syllable.
/// </summary>
public sealed class MicRecorder : IMicRecorder {
    public const int SampleRate = 44100;

    readonly List<short> samples = new();
    AudioRecord? record;
    CancellationTokenSource? cancel;
    Task? worker;

    public bool IsRecording { get; private set; }

    /// <summary>Starts a take. Throws when the microphone is unavailable or refused.</summary>
    public void Start() {
        if (IsRecording) {
            return;
        }
        samples.Clear();
        int bufferBytes = SampleRate * 2 * 4;
        record = new AudioRecord(AudioSource.Mic, SampleRate, ChannelIn.Mono, Encoding.Pcm16bit, bufferBytes);
        if (record.State != State.Initialized) {
            record.Release();
            record.Dispose();
            record = null;
            throw new InvalidOperationException("Micro indisponible : vérifie l'autorisation d'enregistrement.");
        }
        record.StartRecording();
        IsRecording = true;
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        worker = Task.Run(() => Loop(token));
    }

    void Loop(CancellationToken token) {
        var buffer = new short[SampleRate / 20];
        while (!token.IsCancellationRequested) {
            int read;
            try {
                read = record!.Read(buffer, 0, buffer.Length);
            } catch (Exception) {
                break;
            }
            if (read > 0) {
                lock (samples) {
                    for (int i = 0; i < read; i++) {
                        samples.Add(buffer[i]);
                    }
                }
            }
        }
    }

    /// <summary>Stops the take and returns every captured sample.</summary>
    public short[] Stop() {
        if (!IsRecording) {
            return Array.Empty<short>();
        }
        IsRecording = false;
        cancel?.Cancel();
        try {
            worker?.Wait(1500);
        } catch (Exception) {
            // the capture loop never throws out of itself
        }
        try {
            record?.Stop();
        } catch (Exception) {
            // stopping an already stopped record is harmless
        }
        record?.Release();
        record?.Dispose();
        record = null;
        worker = null;
        cancel?.Dispose();
        cancel = null;
        lock (samples) {
            return samples.ToArray();
        }
    }

    public void Dispose() {
        Stop();
    }
}
