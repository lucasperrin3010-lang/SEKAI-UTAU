using System;
using System.Collections.Generic;
using System.IO;

namespace SekaiUtau.Core {
    /// <summary>How the oto.ini is filled when a recorded voicebank is saved.</summary>
    public enum OtoMode {
        /// <summary>The application estimates the cut points from the audio itself.</summary>
        Automatic,
        /// <summary>The oto.ini is left at zero so it can be tuned in vLabeler, SetParam or the oto editor.</summary>
        Manual,
    }

    /// <summary>oto.ini values for one sample, in milliseconds (offset, consonant, cutoff, preutterance, overlap).</summary>
    public sealed record OtoValues(int Offset, int Consonant, int Cutoff, int Preutterance, int Overlap) {
        public static OtoValues Zero { get; } = new(0, 0, 0, 0, 0);

        /// <summary>
        /// Neutral values for a take that cannot be measured. The takes are trimmed by the
        /// recorder, so playback starts at the beginning, with a plain fixed range and a short
        /// fade at the end. Zeroes are not a fallback: they would play the whole file and leave
        /// every note out of time.
        /// </summary>
        public static OtoValues Fallback { get; } = new(0, 60, -20, 0, 0);
    }

    /// <summary>What kind of syllable an oto alias describes, which changes how it can be measured.</summary>
    public enum OtoSyllable {
        /// <summary>A vowel on its own (a, i, n, あ, ん): no consonant, so no preutterance.</summary>
        Vowel,
        /// <summary>A consonant followed by a vowel (ka, shi, か).</summary>
        ConsonantVowel,
        /// <summary>Anything else ("- ka", "a ka", diphthongs): the measurement does not apply.</summary>
        Other,
    }

    /// <summary>One estimate plus how much it can be trusted, so callers can keep the old values.</summary>
    public sealed record OtoEstimate(OtoValues Values, OtoSyllable Syllable, double Confidence) {
        /// <summary>False when nothing periodic could be found; the values must not be written as they are.</summary>
        public bool Reliable => Confidence >= 0.3 && Values != OtoValues.Zero;
    }

    /// <summary>
    /// Estimates the oto.ini cut points of a recorded sample without any network: it looks at
    /// where the sound starts, where the periodic (vowel) part starts and where the sound ends.
    /// The consonant of a syllable is the unvoiced or noisy part before that vowel, which is
    /// exactly what preutterance and consonant need to know.
    ///
    /// The analysis works on a downsampled, DC-removed and high-passed copy of the take (rumble
    /// below the voice range would make every frame look periodic). The noise floor is taken from
    /// the quietest fifth of the frames, so a recording with a constant hiss or a loud room does
    /// not need a fixed threshold, and a frame only counts as a vowel once it repeats itself for
    /// several frames in a row. It is still an automatic estimation, not a trained model: the
    /// values are a good starting point that the oto editor refines.
    /// </summary>
    public static class AutoOto {
        const int AnalysisRate = 11025;
        const double WindowMs = 25;
        const double HopMs = 5;
        const double PeriodicityThreshold = 0.6;
        static readonly string VowelKana = "あいうえおぁぃぅぇぉアイウエオァィゥェォんン";
        // Vowels of the phonemic alphabets the voicebank languages use: romaji and Arpasing
        // (a i u e o n, aa iy uw), X-SAMPA (2 9 E O @ A U 0 y I Y 3 6). A single symbol from
        // this set is a vowel on its own; anything else may carry a consonant in front of it.
        const string VowelSymbols = "aiueonyAEOUIY26936@0";

        /// <summary>Reads a WAV file and estimates its oto values.</summary>
        public static OtoValues EstimateFile(string path) => EstimateFile(path, null).Values;

        /// <summary>
        /// Reads a WAV file and estimates its oto values, using the alias as a hint.
        /// <paramref name="trimmed"/> says the file already starts on the sound because the
        /// recorder cut the silence around the take, so its first sample is the syllable itself.
        /// </summary>
        public static OtoEstimate EstimateFile(string path, string? alias, bool trimmed = false) {
            try {
                var (samples, sampleRate) = ReadWav(path);
                return Estimate(samples, 0, samples.Length, sampleRate, alias, trimmed);
            } catch (Exception) {
                // Unreadable or unusual file: keep a neutral position instead of failing the export.
                return new OtoEstimate(new OtoValues(0, 40, -20, 0, 0), SyllableOf(alias), 0);
            }
        }

        /// <summary>Estimates the cut points of one take.</summary>
        public static OtoValues Estimate(short[] samples, int start, int length, int sampleRate) =>
            Estimate(samples, start, length, sampleRate, null).Values;

        /// <summary>
        /// Estimates the cut points of one take. <paramref name="alias"/> tells what kind of
        /// syllable the sample is supposed to be, which rules out impossible values (a vowel on
        /// its own never has a consonant to measure, a VC or VCV alias is left alone).
        /// <paramref name="trimmed"/> says the take was already cut around its sound, so it
        /// starts on the syllable and the start of the file is the offset.
        /// </summary>
        public static OtoEstimate Estimate(short[] samples, int start, int length, int sampleRate, string? alias, bool trimmed = false) {
            var syllable = SyllableOf(alias);
            if (syllable == OtoSyllable.Other
                || samples == null || length <= 0 || start < 0
                || start + length > samples.Length || sampleRate <= 0) {
                return new OtoEstimate(OtoValues.Zero, syllable, 0);
            }

            int decim = Math.Max(1, sampleRate / AnalysisRate);
            int rate = sampleRate / decim;
            int count = length / decim;
            if (count < rate / 1000 * 40) {
                return new OtoEstimate(OtoValues.Zero, syllable, 0);
            }

            // Downsample with a short box filter, then remove the DC offset and the rumble
            // below the voice range: both would otherwise bias the periodicity measurement.
            var signal = new float[count];
            double mean = 0;
            for (int i = 0; i < count; i++) {
                int sum = 0;
                for (int k = 0; k < decim; k++) {
                    sum += samples[start + i * decim + k];
                }
                signal[i] = (float)sum / decim;
                mean += signal[i];
            }
            mean /= count;
            double pole = Math.Exp(-2 * Math.PI * 60 / rate);
            double previousIn = 0, previousOut = 0;
            for (int i = 0; i < count; i++) {
                double x = signal[i] - mean;
                double y = pole * (previousOut + x - previousIn);
                previousIn = x;
                previousOut = y;
                signal[i] = (float)y;
            }

            int window = Math.Max(16, (int)(rate * WindowMs / 1000));
            int hop = Math.Max(1, (int)(rate * HopMs / 1000));
            int minLag = Math.Max(2, rate / 550);
            int maxLag = Math.Min(window - 2, rate / 60);
            if (maxLag <= minLag + 2) {
                return new OtoEstimate(OtoValues.Zero, syllable, 0);
            }
            int frames = (count - window - maxLag) / hop + 1;
            if (frames < 8) {
                return new OtoEstimate(OtoValues.Zero, syllable, 0);
            }

            // Loudness of every frame, in dB relative to full scale.
            var db = new double[frames];
            double peak = 0;
            for (int f = 0; f < frames; f++) {
                int offset = f * hop;
                double sum = 0;
                for (int i = 0; i < window; i++) {
                    double value = signal[offset + i];
                    sum += value * value;
                }
                double rms = Math.Sqrt(sum / window);
                db[f] = 20 * Math.Log10(Math.Max(rms, 1e-6) / 32768.0);
                if (rms > peak) {
                    peak = rms;
                }
            }
            if (peak < 120) {
                // Practically silent take: nothing to measure.
                return new OtoEstimate(OtoValues.Zero, syllable, 0);
            }

            // Adaptive gate for the sung level: 10 dB above the quiet part of the take, but never
            // so high that the vowel itself would be gated out (a take recorded without silence
            // is still usable).
            double noiseDb = Math.Max(Percentile(db, 0.2), -70);
            double peakDb = Max(db);
            double gateDb = Math.Max(noiseDb + 10, peakDb - 35);
            gateDb = Math.Min(gateDb, peakDb - 12);

            // Start of the take. A take the recorder already trimmed begins on its sound, so the
            // first sample is the start: looking for an onset there only lost the quiet
            // consonants, because the quietest frames of a trimmed take are the consonant itself.
            //
            // On an untrimmed take the onset is taken just above the room floor. It used to be
            // taken above the sung-level gate, which sits 12 dB under the peak, so the breath of
            // "ha", a fricative recorded close to the microphone and every voiced consonant were
            // hidden and the offset landed in the middle of the syllable.
            int first = 0;
            if (!trimmed) {
                bool hasQuietFloor = noiseDb <= peakDb - 8;
                double onsetDb = hasQuietFloor
                    ? Math.Max(noiseDb + 6, peakDb - 45)
                    : peakDb - 40;
                first = -1;
                for (int f = 0; f < frames; f++) {
                    if (db[f] < onsetDb) {
                        continue;
                    }
                    if (f + 1 >= frames || db[f + 1] >= onsetDb - 3) {
                        first = f;
                        break;
                    }
                }
                if (first < 0) {
                    return new OtoEstimate(OtoValues.Zero, syllable, 0);
                }
                // A soft attack can sit a little under the gate: walk back a couple of frames.
                for (int f = first - 1; f >= 0 && f >= first - 2; f--) {
                    if (db[f] < onsetDb - 6) {
                        break;
                    }
                    first = f;
                }
            }
            int last = frames - 1;
            while (last > first && db[last] < gateDb - 6) {
                last--;
            }

            // Periodicity per frame: a vowel or a nasal repeats itself (high normalized
            // autocorrelation), a plosive burst, a fricative or breath noise does not.
            // Energy of every possible window, so the normalized correlation below only has to
            // compute one dot product per lag instead of three inner loops.
            var energy = EnergyPrefix(signal);
            var periodic = new double[frames];
            for (int f = first; f <= last; f++) {
                if (db[f] < gateDb - 6) {
                    continue;
                }
                periodic[f] = Periodicity(signal, energy, f * hop, window, minLag, maxLag);
            }

            // Vowels are also much louder than the consonant in front of them, so a frame only
            // counts as the vowel once it is both periodic and close to the sung level. Voiced
            // consonants (m, n, r, b, d, g) repeat themselves as well, so this level margin is
            // what separates them from the vowel; 12 dB used to swallow them and gave "ma" no
            // preutterance at all.
            double vowelDb = PeriodicLevel(db, periodic, first, last, peakDb);
            double vowelGate = Math.Max(gateDb, vowelDb - 8);
            int vowelStart = -1;
            for (int f = first; f <= last - 2; f++) {
                if (periodic[f] >= PeriodicityThreshold && db[f] >= vowelGate
                    && periodic[f + 1] >= 0.5 && db[f + 1] >= vowelGate - 4
                    && periodic[f + 2] >= 0.5) {
                    vowelStart = f;
                    break;
                }
            }
            if (vowelStart < 0) {
                // Fallback: a clearly periodic frame, even if it is quieter than expected.
                for (int f = first; f <= last; f++) {
                    if (periodic[f] >= 0.75 && db[f] >= gateDb) {
                        vowelStart = f;
                        break;
                    }
                }
            }
            if (vowelStart < 0) {
                return new OtoEstimate(OtoValues.Zero, syllable, 0.1);
            }

            double frameMs = hop * 1000.0 / rate;
            int offsetMs = (int)Math.Round(Math.Max(0, first * frameMs - 10));
            double measured = vowelStart * frameMs - offsetMs;
            // The alias says there is no consonant (a vowel, a syllabic n), or the periodic part
            // starts right away: the take begins on the vowel, so there is nothing to preutter.
            // This is decided from the audio, because an alias alone cannot tell "aa" (a vowel
            // in Arpasing) from a syllable whose consonant is simply very short.
            bool vowelLike = syllable == OtoSyllable.Vowel || measured < 25;
            int preutterance = vowelLike ? 0 : (int)Math.Round(Math.Clamp(measured, 0, 400));
            // The fixed range ends once the vowel is settled, a couple of frames after its onset.
            int consonant = vowelLike
                ? (int)Math.Round(Math.Clamp((vowelStart + 3) * frameMs - offsetMs, 5, 60))
                : Math.Clamp(preutterance + 10, 5, 450);
            int overlap = vowelLike
                ? 0
                : (int)Math.Round(Math.Clamp(preutterance * 0.5, 5, 60));

            int totalMs = length * 1000 / sampleRate;
            int soundEndMs = Math.Min(totalMs, (int)Math.Round(last * frameMs + WindowMs));
            int tail = Math.Max(0, totalMs - soundEndMs);
            // Keep the end of the sound plus a short fade, and never cut inside the measured part.
            int room = Math.Max(10, totalMs - (offsetMs + consonant + 20));
            int cutoff = -Math.Clamp(tail + 10, 10, room);

            double voicedSum = 0;
            int voicedCount = 0;
            for (int f = vowelStart; f <= last; f++) {
                if (periodic[f] >= 0.5) {
                    voicedSum += periodic[f];
                    voicedCount++;
                }
            }
            double periodicity = voicedCount > 0 ? voicedSum / voicedCount : 0;
            double sungMs = Math.Max(0, (last - vowelStart) * frameMs);
            double confidence = 0.45 * Clamp01((vowelDb - noiseDb - 8) / 24)
                + 0.35 * Clamp01((periodicity - 0.5) / 0.45)
                + 0.20 * Clamp01(sungMs / 120);

            return new OtoEstimate(
                new OtoValues(offsetMs, consonant, cutoff, preutterance, overlap),
                syllable, Math.Round(confidence, 3));
        }

        /// <summary>Guess the kind of syllable an alias describes.</summary>
        public static OtoSyllable SyllableOf(string? alias) {
            if (string.IsNullOrWhiteSpace(alias)) {
                return OtoSyllable.ConsonantVowel;
            }
            string text = alias.Trim();
            // VC ("- ka") and VCV ("a ka") banks need their own rules: leave them untouched.
            if (text.StartsWith("-", StringComparison.Ordinal) || text.Contains(' ')) {
                return OtoSyllable.Other;
            }
            char head = text[0];
            if (head >= '\u3041' && head <= '\u30FF') {
                return VowelKana.IndexOf(head) >= 0 ? OtoSyllable.Vowel : OtoSyllable.ConsonantVowel;
            }
            // Phonemic aliases: romaji (ka), Arpasing (aa, iy), X-SAMPA (2, @, bA, k2). Digits
            // and symbols belong to those alphabets, so an alias must never be rejected for
            // containing one: reading only ASCII letters used to turn half of a French or
            // English bank into zeroed oto lines. A single vowel symbol has nothing in front of
            // it; anything else may carry a consonant, and the audio then decides whether the
            // take really starts on a consonant.
            if (text.Length == 1) {
                return VowelSymbols.IndexOf(text[0]) >= 0 ? OtoSyllable.Vowel : OtoSyllable.ConsonantVowel;
            }
            return OtoSyllable.ConsonantVowel;
        }

        /// <summary>Running sum of squares of the signal, used to normalize every correlation.</summary>
        static double[] EnergyPrefix(float[] x) {
            var prefix = new double[x.Length + 1];
            for (int i = 0; i < x.Length; i++) {
                prefix[i + 1] = prefix[i] + (double)x[i] * x[i];
            }
            return prefix;
        }

        /// <summary>
        /// Highest normalized autocorrelation of one window over the lags of the voice range.
        /// The two window energies come from the precomputed sums, which leaves a single dot
        /// product per lag - the same value as before, without two thirds of the work.
        /// </summary>
        static double Periodicity(float[] x, double[] energy, int offset, int window, int minLag, int maxLag) {
            double a2 = energy[offset + window] - energy[offset];
            if (a2 <= 0) {
                return 0;
            }
            int limit = Math.Min(maxLag, x.Length - offset - window);
            double best = 0;
            // Spans keep the bounds checks out of the inner loop, which lets it run on the
            // wide registers instead of one element at a time.
            var head = new ReadOnlySpan<float>(x, offset, window);
            for (int lag = minLag; lag <= limit; lag++) {
                double b2 = energy[offset + window + lag] - energy[offset + lag];
                if (b2 <= 0) {
                    continue;
                }
                var tail = new ReadOnlySpan<float>(x, offset + lag, window);
                float num = 0;
                for (int i = 0; i < head.Length; i++) {
                    num += head[i] * tail[i];
                }
                double r = num / Math.Sqrt(a2 * b2);
                if (r > best) {
                    best = r;
                    // Already almost perfectly periodic: no later lag can be meaningfully better.
                    if (best >= 0.98) {
                        break;
                    }
                }
            }
            return best;
        }

        /// <summary>Loudness of the periodic frames, which is where the sung vowel lives.</summary>
        static double PeriodicLevel(double[] db, double[] periodic, int first, int last, double fallback) {
            var levels = new List<double>();
            for (int f = first; f <= last; f++) {
                if (periodic[f] >= 0.5) {
                    levels.Add(db[f]);
                }
            }
            return levels.Count == 0 ? fallback : Percentile(levels.ToArray(), 0.75);
        }

        static double Percentile(double[] values, double fraction) {
            if (values.Length == 0) {
                return 0;
            }
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int index = (int)Math.Round((sorted.Length - 1) * fraction);
            return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }

        static double Max(double[] values) {
            double max = values.Length > 0 ? values[0] : 0;
            foreach (double value in values) {
                if (value > max) {
                    max = value;
                }
            }
            return max;
        }

        static double Clamp01(double value) => Math.Clamp(value, 0, 1);

        /// <summary>Minimal RIFF reader: 8/16/24/32-bit PCM, mono or stereo (first channel kept).</summary>
        public static (short[] Samples, int SampleRate) ReadWav(string path) {
            using var stream = File.OpenRead(path);
            var header = new byte[12];
            stream.ReadExactly(header);
            if (System.Text.Encoding.ASCII.GetString(header, 0, 4) != "RIFF"
                || System.Text.Encoding.ASCII.GetString(header, 8, 4) != "WAVE") {
                throw new InvalidDataException("Not a WAVE file");
            }
            int channels = 1;
            int sampleRate = 44100;
            int bits = 16;
            var chunk = new byte[8];
            while (stream.Position + 8 <= stream.Length) {
                stream.ReadExactly(chunk);
                string id = System.Text.Encoding.ASCII.GetString(chunk, 0, 4);
                int size = BitConverter.ToInt32(chunk, 4);
                if (size < 0) {
                    break;
                }
                long next = stream.Position + size + (size % 2);
                if (id == "fmt ") {
                    var fmt = new byte[Math.Max(0, Math.Min(16, size))];
                    stream.ReadExactly(fmt);
                    if (fmt.Length >= 16) {
                        channels = Math.Max(1, (int)BitConverter.ToInt16(fmt, 2));
                        sampleRate = BitConverter.ToInt32(fmt, 4);
                        bits = BitConverter.ToInt16(fmt, 14);
                    }
                } else if (id == "data") {
                    int bytesPerSample = Math.Max(1, bits / 8);
                    int frameBytes = bytesPerSample * channels;
                    int count = size / Math.Max(1, frameBytes);
                    var samples = new short[count];
                    var frame = new byte[Math.Max(1, frameBytes)];
                    for (int i = 0; i < count; i++) {
                        stream.ReadExactly(frame, 0, frameBytes);
                        long value = bytesPerSample switch {
                            1 => (frame[0] - 128) << 8,
                            2 => BitConverter.ToInt16(frame, 0),
                            3 => (BitConverter.ToInt32(new byte[] { 0, frame[0], frame[1], frame[2] }, 0)) >> 8,
                            _ => BitConverter.ToInt32(frame, 0) >> 16,
                        };
                        samples[i] = (short)Math.Clamp(value, (long)short.MinValue, short.MaxValue);
                    }
                    return (samples, sampleRate);
                }
                if (next < stream.Position) {
                    // A chunk whose size does not match what was read: stop instead of looping.
                    break;
                }
                if (next > stream.Position) {
                    stream.Position = next;
                }
            }
            throw new InvalidDataException("No data chunk");
        }
    }
}
