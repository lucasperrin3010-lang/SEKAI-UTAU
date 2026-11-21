using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SekaiUtau.Core {
    /// <summary>One syllable of the recording script: the sample name (also the main oto alias) and what to read.</summary>
    public sealed record RecordingSyllable(string Alias, string Kana);

    /// <summary>
    /// Recording script for a CV voicebank (one sample per syllable): the list of syllables,
    /// the WAV file name of each sample, the oto.ini written once recording ends and a small
    /// helper to cut the silence around a take.
    /// </summary>
    public static class VoicebankRecording {
        /// <summary>Quick test voice: the five vowels and the final n.</summary>
        public static IReadOnlyList<RecordingSyllable> Basic { get; } = new List<RecordingSyllable> {
            new("a", "あ"), new("i", "い"), new("u", "う"), new("e", "え"), new("o", "お"), new("n", "ん"),
        };

        /// <summary>Complete CV script: every syllable an UTAU-style bank needs.</summary>
        public static IReadOnlyList<RecordingSyllable> Full { get; } = BuildFull();

        // The oto values are a starting point: the takes are trimmed, so the vowel starts
        // almost immediately; the built-in oto editor refines them afterwards.
        public const int OffsetMs = 20;
        public const int ConsonantMs = 70;
        public const int CutoffMs = -20;
        public const int PreutteranceMs = 20;
        public const int OverlapMs = 10;

        public static string FileName(RecordingSyllable syllable) => syllable.Alias + ".wav";

        public static string OtoLine(string fileName, string alias) =>
            OtoLine(fileName, alias, new OtoValues(OffsetMs, ConsonantMs, CutoffMs, PreutteranceMs, OverlapMs));

        public static string OtoLine(string fileName, string alias, OtoValues values) =>
            $"{fileName}={alias},{values.Offset},{values.Consonant},{values.Cutoff},{values.Preutterance},{values.Overlap}";

        /// <summary>
        /// Writes oto.ini for the recorded samples, in the script order. Each sample is
        /// aliased with its romaji (ka, shi, ...) and with its kana (か, し, ...), so the
        /// voice sings whether the lyrics are romanised or Japanese.
        /// </summary>
        public static void WriteOto(string directory, IEnumerable<RecordingSyllable> recorded) {
            WriteOto(directory, recorded, OtoMode.Automatic);
        }

        /// <summary>
        /// Writes oto.ini for the recorded samples. In <see cref="OtoMode.Automatic"/> the cut
        /// points of every take are estimated from the audio of the WAV file stored in
        /// <paramref name="directory"/>; in <see cref="OtoMode.Manual"/> they are written at zero
        /// so the bank can be tuned in vLabeler, SetParam or the built-in oto editor. Either way
        /// each sample gets a romaji and a kana alias. <paramref name="trimmedTakes"/> says the
        /// recorder cut the silence around each WAV, so the file starts on the syllable.
        /// </summary>
        public static void WriteOto(string directory, IEnumerable<RecordingSyllable> recorded, OtoMode mode,
            bool trimmedTakes = false) {
            var lines = new List<string>();
            foreach (var syllable in recorded) {
                string file = FileName(syllable);
                var values = OtoValues.Zero;
                if (mode == OtoMode.Automatic) {
                    var estimate = AutoOto.EstimateFile(Path.Combine(directory, file), syllable.Alias, trimmedTakes);
                    // A take that cannot be measured still gets usable numbers: zeroes would
                    // leave the whole file in the bank and put every note out of time.
                    values = estimate.Reliable ? estimate.Values : OtoValues.Fallback;
                }
                lines.Add(OtoLine(file, syllable.Alias, values));
                if (!string.IsNullOrEmpty(syllable.Kana) && syllable.Kana != syllable.Alias) {
                    lines.Add(OtoLine(file, syllable.Kana, values));
                }
            }
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(directory, VoicebankCreator.OtoIni),
                string.Join("\n", lines) + "\n", utf8);
        }

        /// <summary>
        /// Finds the audible part of a 16-bit mono take so the silence before and after the
        /// vowel is removed. Returns a start index and a length; empty when nothing is audible.
        /// <paramref name="threshold"/> defaults to 0, meaning "follow the take": the gate is
        /// then set from the quiet floor of the recording instead of a fixed level.
        /// </summary>
        public static (int Start, int Length) TrimSilence(short[] samples, int sampleRate,
            int threshold = 0, int marginMs = 20) {
            if (samples == null || samples.Length == 0) {
                return (0, 0);
            }
            // A fixed gate was wrong in both directions: a quiet microphone (phone, headset)
            // cut the soft consonants h, s, f off the front of the take, and a noisy room kept
            // seconds of hiss. Following the take's own noise floor keeps the whole syllable.
            if (threshold <= 0) {
                threshold = NoiseGate(samples, sampleRate);
            }
            int first = -1;
            int last = -1;
            for (int i = 0; i < samples.Length; i++) {
                if (Math.Abs((int)samples[i]) > threshold) {
                    if (first < 0) {
                        first = i;
                    }
                    last = i;
                }
            }
            if (first < 0) {
                return (0, 0);
            }
            int margin = Math.Max(0, sampleRate / 1000 * marginMs);
            int start = Math.Max(0, first - margin);
            int end = Math.Min(samples.Length, last + margin);
            return (start, end - start);
        }

        /// <summary>
        /// Loudness every take is written at, in dBFS RMS. A bank recorded in several sittings,
        /// or with the microphone at different distances, then sings at one level instead of
        /// jumping from loud to quiet note to note.
        /// </summary>
        public const double TargetLoudnessDb = -18;

        /// <summary>Below this peak a take is loud but clean; above it the converter already clipped.</summary>
        public const int ClipThreshold = 32000;

        /// <summary>
        /// Copies the audible part of a take and scales it to <see cref="TargetLoudnessDb"/>.
        /// The gain is limited to -12/+12 dB and the peak always stays under the 16-bit
        /// ceiling, so a loud take is never distorted to reach the target and a take that is
        /// only room noise is left alone instead of being amplified into hiss.
        /// </summary>
        public static short[] LevelTake(short[] samples, int start, int length) {
            if (samples == null || length <= 0) {
                return Array.Empty<short>();
            }
            var copy = new short[length];
            Array.Copy(samples, start, copy, 0, length);
            double sum = 0;
            int peak = 0;
            foreach (short sample in copy) {
                sum += (double)sample * sample;
                int value = Math.Abs((int)sample);
                if (value > peak) {
                    peak = value;
                }
            }
            double rms = Math.Sqrt(sum / copy.Length);
            if (rms < 60 || peak == 0) {
                // Silence, or nothing but noise: scaling it would only make the hiss louder.
                return copy;
            }
            double target = 32768.0 * Math.Pow(10, TargetLoudnessDb / 20);
            double gain = Math.Clamp(target / rms, 0.25, 4.0);
            double headroom = 32000.0 / peak;
            if (gain > headroom) {
                gain = headroom;
            }
            if (Math.Abs(gain - 1) < 0.02) {
                return copy;
            }
            for (int i = 0; i < copy.Length; i++) {
                copy[i] = (short)Math.Clamp(Math.Round(copy[i] * gain), short.MinValue, short.MaxValue);
            }
            return copy;
        }

        /// <summary>True when the take was recorded so loud that the converter already distorted it.</summary>
        public static bool IsClipped(short[] samples, int start, int length) {
            if (samples == null || length <= 0) {
                return false;
            }
            int end = Math.Min(samples.Length, start + length);
            for (int i = Math.Max(0, start); i < end; i++) {
                if (Math.Abs((int)samples[i]) >= ClipThreshold) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Silence gate of one take: three times the level of its quietest fifth, kept between
        /// 200 and 3000 so a take with no silence and a very noisy one both stay usable.
        /// </summary>
        static int NoiseGate(short[] samples, int sampleRate) {
            int frame = Math.Max(1, sampleRate / 100); // 10 ms
            int frames = samples.Length / frame;
            if (frames < 4) {
                return 400;
            }
            var levels = new double[frames];
            for (int f = 0; f < frames; f++) {
                double sum = 0;
                int offset = f * frame;
                for (int i = 0; i < frame; i++) {
                    double value = samples[offset + i];
                    sum += value * value;
                }
                levels[f] = Math.Sqrt(sum / frame);
            }
            Array.Sort(levels);
            return (int)Math.Clamp(levels[frames / 5] * 3, 200, 3000);
        }

        static IReadOnlyList<RecordingSyllable> BuildFull() {
            var list = new List<RecordingSyllable>();
            foreach (string vowel in new[] { "a", "i", "u", "e", "o" }) {
                list.Add(new RecordingSyllable(vowel, KanaOf("", vowel)));
            }
            foreach (string consonant in new[] { "k", "g", "s", "z", "t", "d", "n", "h", "b", "p", "m" }) {
                foreach (string vowel in new[] { "a", "i", "u", "e", "o" }) {
                    list.Add(new RecordingSyllable(AliasOf(consonant, vowel), KanaOf(consonant, vowel)));
                }
            }
            foreach (string vowel in new[] { "a", "u", "o" }) {
                list.Add(new RecordingSyllable("y" + vowel, KanaOf("y", vowel)));
            }
            foreach (string vowel in new[] { "a", "i", "u", "e", "o" }) {
                list.Add(new RecordingSyllable("r" + vowel, KanaOf("r", vowel)));
            }
            list.Add(new RecordingSyllable("wa", KanaOf("w", "a")));
            list.Add(new RecordingSyllable("wo", KanaOf("w", "o")));
            list.Add(new RecordingSyllable("n", "ん"));
            return list;
        }

        /// <summary>Hepburn romaji, which is what singers type in lyrics.</summary>
        static string AliasOf(string consonant, string vowel) => (consonant, vowel) switch {
            ("s", "i") => "shi",
            ("z", "i") => "ji",
            ("t", "i") => "chi",
            ("t", "u") => "tsu",
            ("h", "u") => "fu",
            _ => consonant + vowel,
        };

        static string KanaOf(string consonant, string vowel) => (consonant, vowel) switch {
            ("", "a") => "あ", ("", "i") => "い", ("", "u") => "う", ("", "e") => "え", ("", "o") => "お",
            ("k", "a") => "か", ("k", "i") => "き", ("k", "u") => "く", ("k", "e") => "け", ("k", "o") => "こ",
            ("g", "a") => "が", ("g", "i") => "ぎ", ("g", "u") => "ぐ", ("g", "e") => "げ", ("g", "o") => "ご",
            ("s", "a") => "さ", ("s", "i") => "し", ("s", "u") => "す", ("s", "e") => "せ", ("s", "o") => "そ",
            ("z", "a") => "ざ", ("z", "i") => "じ", ("z", "u") => "ず", ("z", "e") => "ぜ", ("z", "o") => "ぞ",
            ("t", "a") => "た", ("t", "i") => "ち", ("t", "u") => "つ", ("t", "e") => "て", ("t", "o") => "と",
            ("d", "a") => "だ", ("d", "i") => "ぢ", ("d", "u") => "づ", ("d", "e") => "で", ("d", "o") => "ど",
            ("n", "a") => "な", ("n", "i") => "に", ("n", "u") => "ぬ", ("n", "e") => "ね", ("n", "o") => "の",
            ("h", "a") => "は", ("h", "i") => "ひ", ("h", "u") => "ふ", ("h", "e") => "へ", ("h", "o") => "ほ",
            ("b", "a") => "ば", ("b", "i") => "び", ("b", "u") => "ぶ", ("b", "e") => "べ", ("b", "o") => "ぼ",
            ("p", "a") => "ぱ", ("p", "i") => "ぴ", ("p", "u") => "ぷ", ("p", "e") => "ぺ", ("p", "o") => "ぽ",
            ("m", "a") => "ま", ("m", "i") => "み", ("m", "u") => "む", ("m", "e") => "め", ("m", "o") => "も",
            ("y", "a") => "や", ("y", "u") => "ゆ", ("y", "o") => "よ",
            ("r", "a") => "ら", ("r", "i") => "り", ("r", "u") => "る", ("r", "e") => "れ", ("r", "o") => "ろ",
            ("w", "a") => "わ", ("w", "o") => "を",
            _ => "",
        };
    }
}
