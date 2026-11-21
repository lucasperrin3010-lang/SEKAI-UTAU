using System;
using System.IO;
using System.Linq;
using System.Text;
using SekaiUtau.Core;
using Xunit;

namespace SekaiUtau.Core {
    public class VoicebankRecordingTest {
        [Fact]
        public void FullScriptCoversEverySyllableOnce() {
            var full = VoicebankRecording.Full;

            Assert.Equal(71, full.Count);
            Assert.Equal(71, full.Select(s => s.Alias).Distinct().Count());
            Assert.All(full, s => Assert.False(string.IsNullOrWhiteSpace(s.Kana)));
            foreach (string alias in new[] { "a", "i", "u", "e", "o", "n", "ka", "shi", "chi", "tsu", "fu", "ji", "yo", "wo" }) {
                Assert.Contains(full, s => s.Alias == alias);
            }
        }

        [Fact]
        public void BasicScriptIsTheVowelsAndN() {
            Assert.Equal(new[] { "a", "i", "u", "e", "o", "n" },
                VoicebankRecording.Basic.Select(s => s.Alias).ToArray());
        }

        [Fact]
        public void OtoLineUsesTheSampleNameAsAlias() {
            Assert.Equal("ka.wav=ka,20,70,-20,20,10", VoicebankRecording.OtoLine("ka.wav", "ka"));
        }

        [Fact]
        public void WriteOtoAliasesEverySampleInRomajiAndKana() {
            var dir = Path.Combine(Path.GetTempPath(), "sekaiu-oto-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try {
                var recorded = VoicebankRecording.Full.Where(s => s.Alias == "a" || s.Alias == "ka").ToArray();
                VoicebankRecording.WriteOto(dir, recorded);
                var lines = File.ReadAllLines(Path.Combine(dir, VoicebankCreator.OtoIni))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                Assert.Equal(4, lines.Length);
                Assert.StartsWith("a.wav=a,", lines[0]);
                Assert.StartsWith("a.wav=あ,", lines[1]);
                Assert.StartsWith("ka.wav=ka,", lines[2]);
                Assert.StartsWith("ka.wav=か,", lines[3]);
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Fact]
        public void RecordedBankSingsRomajiAndKana() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var root = Path.Combine(Path.GetTempPath(), "sekaiu-rec-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try {
                var dir = VoicebankCreator.Create("Voix enregistree", "Test", root: root);
                var recorded = VoicebankRecording.Full.Where(s => s.Alias == "a" || s.Alias == "ka").ToArray();
                foreach (var syllable in recorded) {
                    WriteTinyWav(Path.Combine(dir, VoicebankRecording.FileName(syllable)));
                }
                VoicebankRecording.WriteOto(dir, recorded);

                var bank = new SekaiUtau.Classic.VoicebankLoader(root).SearchAll().Single();
                var singer = new SekaiUtau.Classic.ClassicSinger(bank);
                singer.Reload();

                Assert.True(singer.TryGetOto("ka", out _), "ka should be singable");
                Assert.True(singer.TryGetOto("か", out _), "the kana alias should be singable too");
                Assert.True(singer.TryGetOto("a", out _));
                Assert.True(singer.TryGetOto("あ", out _));
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, true);
                }
            }
        }

        static void WriteTinyWav(string path) {
            using var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(44100, 16, 1));
            writer.Write(new byte[44100 / 10 * 2], 0, 44100 / 10 * 2);
        }

        [Fact]
        public void TrimSilenceKeepsTheAudiblePart() {
            var samples = new short[44100];
            for (int i = 20000; i < 30000; i++) {
                samples[i] = 8000;
            }

            var (start, length) = VoicebankRecording.TrimSilence(samples, 44100);

            // 20 ms of margin on each side of the audible block.
            Assert.True(start <= 20000 && start >= 20000 - 44100 / 50);
            Assert.True(start + length >= 30000 && start + length <= 30000 + 44100 / 50);
        }

        /// <summary>A steady sung note, at the given peak level.</summary>
        static short[] Note(int amplitude, int length = 22050) {
            var samples = new short[length];
            for (int i = 0; i < length; i++) {
                samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * 220 * i / 44100.0));
            }
            return samples;
        }

        static double Rms(short[] samples) {
            double sum = 0;
            foreach (short sample in samples) {
                sum += (double)sample * sample;
            }
            return samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length);
        }

        static int Peak(short[] samples) {
            int peak = 0;
            foreach (short sample in samples) {
                peak = Math.Max(peak, Math.Abs((int)sample));
            }
            return peak;
        }

        [Fact]
        public void LevelTakeBringsAQuietAndALoudTakeToTheSameLevel() {
            // Both are inside the +-12 dB gain range, so both reach the target loudness.
            // (A take far quieter than that is deliberately not amplified into its own noise.)
            var quiet = Note(2500);
            var loud = Note(12000);

            var levelledQuiet = VoicebankRecording.LevelTake(quiet, 0, quiet.Length);
            var levelledLoud = VoicebankRecording.LevelTake(loud, 0, loud.Length);

            double rq = Rms(levelledQuiet);
            double rl = Rms(levelledLoud);
            Assert.InRange(rq, rl * 0.85, rl * 1.15);
            // The quiet take is really lifted, not left as it was.
            Assert.True(rq > Rms(quiet) * 1.5, $"quiet rms {rq:0} vs {Rms(quiet):0}");
        }

        [Fact]
        public void LevelTakeNeverClipsTheTake() {
            var loud = Note(30000);

            var levelled = VoicebankRecording.LevelTake(loud, 0, loud.Length);

            Assert.True(Peak(levelled) <= 32000, $"peak {Peak(levelled)}");
        }

        [Fact]
        public void LevelTakeLeavesSilenceAlone() {
            var silence = new short[4410];

            var levelled = VoicebankRecording.LevelTake(silence, 0, silence.Length);

            Assert.Equal(silence.Length, levelled.Length);
            Assert.All(levelled, s => Assert.Equal(0, s));
        }

        [Fact]
        public void IsClippedOnlyFlagsSaturatedTakes() {
            Assert.False(VoicebankRecording.IsClipped(Note(8000), 0, 22050));
            Assert.True(VoicebankRecording.IsClipped(Note(32767), 0, 22050));
            Assert.False(VoicebankRecording.IsClipped(Array.Empty<short>(), 0, 0));
        }

        [Fact]
        public void TrimSilenceReportsNothingWhenSilent() {
            Assert.Equal((0, 0), VoicebankRecording.TrimSilence(new short[4410], 44100));
            Assert.Equal((0, 0), VoicebankRecording.TrimSilence(Array.Empty<short>(), 44100));
        }
    }
}
