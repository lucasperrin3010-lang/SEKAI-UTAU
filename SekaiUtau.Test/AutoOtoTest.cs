using System;
using System.IO;
using System.Linq;
using SekaiUtau.Core;
using Xunit;

namespace SekaiUtau.Core {
    public class AutoOtoTest {
        const int SampleRate = 44100;

        /// <summary>A take made of an optional noisy consonant followed by a sung vowel.</summary>
        static short[] Take(int consonantMs, int vowelMs) {
            int consonant = SampleRate / 1000 * consonantMs;
            int vowel = SampleRate / 1000 * vowelMs;
            var samples = new short[consonant + vowel];
            var random = new Random(1234);
            for (int i = 0; i < consonant; i++) {
                samples[i] = (short)(random.Next(-12000, 12000));
            }
            for (int i = 0; i < vowel; i++) {
                double t = (double)i / SampleRate;
                // A voiced vowel: strong periodic wave with decreasing harmonics.
                double value = 12000 * Math.Sin(2 * Math.PI * 220 * t)
                    + 4000 * Math.Sin(4 * Math.PI * 220 * t);
                samples[consonant + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            }
            return samples;
        }

        [Fact]
        public void ConsonantLengthBecomesThePreutterance() {
            var samples = Take(consonantMs: 70, vowelMs: 400);

            var values = AutoOto.Estimate(samples, 0, samples.Length, SampleRate);

            Assert.InRange(values.Preutterance, 40, 110);
            Assert.True(values.Consonant >= values.Preutterance,
                $"consonant {values.Consonant} must not be shorter than preutterance {values.Preutterance}");
            Assert.InRange(values.Offset, 0, 40);
            Assert.True(values.Cutoff < 0, "the cutoff should trim the end of the file");
        }

        [Fact]
        public void PureVowelStartsWithoutPreutterance() {
            var samples = Take(consonantMs: 0, vowelMs: 500);

            var values = AutoOto.Estimate(samples, 0, samples.Length, SampleRate);

            Assert.InRange(values.Preutterance, 0, 40);
            Assert.InRange(values.Overlap, 0, 60);
        }

        /// <summary>A take recorded with the silence before and after it, as the recorder writes it.</summary>
        static short[] TakeWithSilence(int leadingMs, int consonantMs, int vowelMs, int trailingMs, int noise = 0) {
            var core = Take(consonantMs, vowelMs);
            int leading = SampleRate / 1000 * leadingMs;
            int trailing = SampleRate / 1000 * trailingMs;
            var samples = new short[leading + core.Length + trailing];
            var random = new Random(4321);
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = (short)random.Next(-noise, noise + 1);
            }
            Array.Copy(core, 0, samples, leading, core.Length);
            return samples;
        }

        [Fact]
        public void TheSilenceAroundTheTakeBecomesTheOffsetAndTheCutoff() {
            var samples = TakeWithSilence(leadingMs: 200, consonantMs: 60, vowelMs: 350, trailingMs: 150, noise: 40);

            var values = AutoOto.Estimate(samples, 0, samples.Length, SampleRate);

            // Playback starts right before the consonant, not 200 ms of silence earlier.
            Assert.InRange(values.Offset, 160, 220);
            Assert.InRange(values.Preutterance, 30, 110);
            // Cutoff is measured from the end of the file, so it trims the trailing silence.
            Assert.InRange(values.Cutoff, -180, -120);
        }

        /// <summary>A take whose consonant is a quiet breath (h, f) instead of a loud noise.</summary>
        static short[] BreathTake(int consonantMs, int consonantLevel, int vowelMs,
            int leadingMs = 0, int noise = 0) {
            int leading = SampleRate / 1000 * leadingMs;
            int consonant = SampleRate / 1000 * consonantMs;
            int vowel = SampleRate / 1000 * vowelMs;
            var samples = new short[leading + consonant + vowel];
            var random = new Random(1234);
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = (short)random.Next(-noise, noise + 1);
            }
            for (int i = 0; i < consonant; i++) {
                samples[leading + i] = (short)random.Next(-consonantLevel, consonantLevel + 1);
            }
            for (int i = 0; i < vowel; i++) {
                double t = (double)i / SampleRate;
                double value = 12000 * Math.Sin(2 * Math.PI * 220 * t)
                    + 4000 * Math.Sin(4 * Math.PI * 220 * t);
                samples[leading + consonant + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            }
            return samples;
        }

        /// <summary>
        /// The recorder cuts the silence around each take, so a recorded WAV starts on the
        /// syllable. The estimator used to look for a loud onset in it anyway, and the quietest
        /// frames of such a file are the consonant itself: the offset then landed after the
        /// breath of "ha" and the consonant was gone from the sample.
        /// </summary>
        [Fact]
        public void ATrimmedTakeStartsOnItsQuietConsonant() {
            var samples = BreathTake(consonantMs: 70, consonantLevel: 900, vowelMs: 400);

            var estimate = AutoOto.Estimate(samples, 0, samples.Length, SampleRate, "ha", trimmed: true);

            Assert.True(estimate.Reliable, $"{estimate.Syllable} conf={estimate.Confidence}");
            Assert.Equal(0, estimate.Values.Offset);
            Assert.InRange(estimate.Values.Preutterance, 40, 100);
            Assert.True(estimate.Values.Consonant >= estimate.Values.Preutterance,
                $"consonant {estimate.Values.Consonant} is shorter than preutterance {estimate.Values.Preutterance}");
        }

        /// <summary>
        /// A bank that keeps the silence around its samples still has to start playback before a
        /// quiet consonant: the onset was taken 12 dB under the peak, which hid the breath of
        /// "ha" and left the offset on the vowel, so the note played a bare vowel.
        /// </summary>
        [Fact]
        public void AQuietConsonantIsNotSkippedWhenTheTakeKeepsItsSilence() {
            var samples = BreathTake(consonantMs: 70, consonantLevel: 900, vowelMs: 400,
                leadingMs: 120, noise: 60);

            var estimate = AutoOto.Estimate(samples, 0, samples.Length, SampleRate, "ha");

            Assert.InRange(estimate.Values.Offset, 0, 120);
            Assert.InRange(estimate.Values.Preutterance, 40, 110);
        }

        [Fact]
        public void ARoomWithAConstantHissIsNotMistakenForSound() {
            var samples = TakeWithSilence(leadingMs: 150, consonantMs: 60, vowelMs: 350, trailingMs: 120, noise: 500);

            var values = AutoOto.Estimate(samples, 0, samples.Length, SampleRate);

            Assert.InRange(values.Offset, 110, 180);
            Assert.InRange(values.Preutterance, 30, 120);
            Assert.True(values.Consonant > values.Preutterance,
                $"consonant {values.Consonant} must end after preutterance {values.Preutterance}");
            Assert.True(values.Consonant - values.Preutterance <= 40,
                $"consonant {values.Consonant} is too far from preutterance {values.Preutterance}");
        }

        [Fact]
        public void SilentTakeGivesZeroes() {
            Assert.Equal(OtoValues.Zero, AutoOto.Estimate(new short[SampleRate / 2], 0, SampleRate / 2, SampleRate));
            Assert.Equal(OtoValues.Zero, AutoOto.Estimate(Array.Empty<short>(), 0, 0, SampleRate));
        }

        [Fact]
        public void EstimateFileReadsTheRecordedWav() {
            var path = Path.Combine(Path.GetTempPath(), "sekaiu-auto-" + Path.GetRandomFileName() + ".wav");
            var samples = Take(consonantMs: 60, vowelMs: 350);
            try {
                using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(SampleRate, 16, 1))) {
                    var bytes = new byte[samples.Length * 2];
                    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                    writer.Write(bytes, 0, bytes.Length);
                }

                var (read, rate) = AutoOto.ReadWav(path);
                int peak = read.Length == 0 ? 0 : read.Max(s => Math.Abs((int)s));
                Assert.True(read.Length == samples.Length && rate == SampleRate && peak > 1000,
                    $"read={read.Length}/{samples.Length} rate={rate} peak={peak}");

                var values = AutoOto.EstimateFile(path);

                Assert.InRange(values.Preutterance, 30, 100);
            } finally {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// The phone app writes its own WAV files, with a 16-byte fmt chunk. The reader used to
        /// stop right after that chunk, so the automatic mode silently fell back to defaults.
        /// </summary>
        [Fact]
        public void EstimateFileHandlesTheWavLayoutOfThePhoneApp() {
            var path = Path.Combine(Path.GetTempPath(), "sekaiu-mobile-" + Path.GetRandomFileName() + ".wav");
            var samples = Take(consonantMs: 60, vowelMs: 350);
            try {
                using (var stream = File.Create(path)) {
                    using var writer = new BinaryWriter(stream);
                    writer.Write(new[] { 'R', 'I', 'F', 'F' });
                    writer.Write(36 + samples.Length * 2);
                    writer.Write(new[] { 'W', 'A', 'V', 'E' });
                    writer.Write(new[] { 'f', 'm', 't', ' ' });
                    writer.Write(16);
                    writer.Write((short)1);
                    writer.Write((short)1);
                    writer.Write(SampleRate);
                    writer.Write(SampleRate * 2);
                    writer.Write((short)2);
                    writer.Write((short)16);
                    writer.Write(new[] { 'd', 'a', 't', 'a' });
                    writer.Write(samples.Length * 2);
                    foreach (var sample in samples) {
                        writer.Write(sample);
                    }
                }

                var values = AutoOto.EstimateFile(path);

                Assert.InRange(values.Preutterance, 30, 100);
            } finally {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// The aliases of the 15 languages are not all short romaji: Arpasing writes "aa" or
        /// "iy", X-SAMPA writes "2", "@", "bA" or "b2". Reading only ASCII letters rejected
        /// them, so a French or English bank came out with zeroed oto lines.
        /// </summary>
        [Fact]
        public void AliasesWithDigitsOrSymbolsAreMeasuredToo() {
            var samples = Take(consonantMs: 60, vowelMs: 350);

            foreach (string alias in new[] { "2", "@", "aa", "b2", "k2", "bA", "6" }) {
                var estimate = AutoOto.Estimate(samples, 0, samples.Length, SampleRate, alias);

                Assert.True(estimate.Reliable,
                    $"{alias}: {estimate.Syllable} conf={estimate.Confidence} values={estimate.Values}");
                Assert.True(estimate.Values.Consonant > 0, $"{alias}: consonant={estimate.Values.Consonant}");
                Assert.True(estimate.Values.Cutoff < 0, $"{alias}: cutoff={estimate.Values.Cutoff}");
            }
        }

        [Fact]
        public void AShortTakeAliasStillGetsAPreutteranceFromTheAudio() {
            var samples = Take(consonantMs: 60, vowelMs: 350);

            // "b2" is a consonant-vowel syllable even though its vowel is not a romaji letter.
            var estimate = AutoOto.Estimate(samples, 0, samples.Length, SampleRate, "b2");

            Assert.InRange(estimate.Values.Preutterance, 30, 90);
            Assert.True(estimate.Values.Consonant >= estimate.Values.Preutterance);
        }

        [Fact]
        public void TwoSymbolVowelAliasesHaveNoPreutterance() {
            var samples = Take(consonantMs: 0, vowelMs: 400);

            foreach (string alias in new[] { "aa", "iy", "@" }) {
                var estimate = AutoOto.Estimate(samples, 0, samples.Length, SampleRate, alias);

                Assert.Equal(0, estimate.Values.Preutterance);
                Assert.Equal(0, estimate.Values.Overlap);
                Assert.InRange(estimate.Values.Consonant, 5, 60);
            }
        }

        /// <summary>
        /// A silent or unreadable take must not be written as "0,0,0,0,0": zeroed lines keep the
        /// whole file and put the notes out of time, which is what made banks sound broken.
        /// </summary>
        [Fact]
        public void UnmeasurableTakesGetNeutralNumbersInsteadOfZeroes() {
            var dir = Path.Combine(Path.GetTempPath(), "sekaiu-fallback-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try {
                var syllable = new RecordingSyllable("a", "あ");
                using (var writer = new NAudio.Wave.WaveFileWriter(
                    Path.Combine(dir, VoicebankRecording.FileName(syllable)),
                    new NAudio.Wave.WaveFormat(SampleRate, 16, 1))) {
                    var silence = new byte[SampleRate];
                    writer.Write(silence, 0, silence.Length);
                }

                VoicebankRecording.WriteOto(dir, new[] { syllable }, OtoMode.Automatic);
                var lines = File.ReadAllLines(Path.Combine(dir, VoicebankCreator.OtoIni))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                Assert.Equal("a.wav=a,0,60,-20,0,0", lines[0]);
                Assert.Equal("a.wav=あ,0,60,-20,0,0", lines[1]);
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Fact]
        public void ManualModeWritesZeroedOto() {
            var dir = Path.Combine(Path.GetTempPath(), "sekaiu-manual-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try {
                var recorded = VoicebankRecording.Full.Where(s => s.Alias == "ka").ToArray();
                VoicebankRecording.WriteOto(dir, recorded, OtoMode.Manual);
                var lines = File.ReadAllLines(Path.Combine(dir, VoicebankCreator.OtoIni))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                Assert.Equal(2, lines.Length);
                Assert.Equal("ka.wav=ka,0,0,0,0,0", lines[0]);
                Assert.Equal("ka.wav=か,0,0,0,0,0", lines[1]);
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}
