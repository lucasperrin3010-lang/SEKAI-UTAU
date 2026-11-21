using System;
using NAudio.Wave;

namespace SekaiUtau.Core.SignalChain {
    /// <summary>
    /// Taps the exact samples this app sends to the audio device and writes them to a WAV file.
    /// Nothing from the microphone or from other applications can end up here: the only input
    /// is the app's own mix.
    /// </summary>
    class RecordingSampleProvider : ISampleProvider {
        private ISampleProvider source;
        private readonly WaveFileWriter writer;
        private readonly object gate = new object();
        private bool closed;

        public string Path { get; }

        public RecordingSampleProvider(ISampleProvider source, string path) {
            this.source = source;
            Path = path;
            writer = new WaveFileWriter(path, source.WaveFormat);
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        /// <summary>
        /// Points the recorder at a new mix (a later playback session) while keeping the same file,
        /// so one recording can span several play/stop cycles.
        /// </summary>
        public void SetSource(ISampleProvider newSource) {
            lock (gate) {
                source = newSource;
            }
        }

        /// <summary>Last time real samples were written, used to pad pauses with silence.</summary>
        public DateTime LastReadUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>Appends silence, so the file timeline follows the wall clock while playback is stopped.</summary>
        public void WriteSilence(double seconds) {
            if (seconds <= 0.005) {
                return;
            }
            lock (gate) {
                if (closed) {
                    return;
                }
                try {
                    int frames = (int)Math.Round(seconds * writer.WaveFormat.SampleRate);
                    int samples = frames * writer.WaveFormat.Channels;
                    if (samples > 0) {
                        writer.WriteSamples(new float[samples], 0, samples);
                    }
                } catch (Exception) {
                }
            }
        }

        public int Read(float[] buffer, int offset, int count) {
            int read = source.Read(buffer, offset, count);
            LastReadUtc = DateTime.UtcNow;
            if (read > 0) {
                lock (gate) {
                    if (!closed) {
                        try {
                            writer.WriteSamples(buffer, offset, read);
                            LastReadUtc = DateTime.UtcNow;
                        } catch (Exception) {
                            // A full disk must never break playback.
                        }
                    }
                }
            }
            return read;
        }

        public void Close() {
            lock (gate) {
                if (closed) {
                    return;
                }
                closed = true;
                try {
                    writer.Dispose();
                } catch (Exception) {
                }
            }
        }
    }
}
