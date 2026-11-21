using System;
using System.IO;
using System.Linq;
using System.Text;
using NAudio.Wave;
using Xunit;

namespace SekaiUtau.Core {
    public class OtoAutoFixTest {
        const int SampleRate = 44100;

        static short[] Take(int consonantMs, int vowelMs) {
            int consonant = SampleRate / 1000 * consonantMs;
            int vowel = SampleRate / 1000 * vowelMs;
            var samples = new short[consonant + vowel + SampleRate / 1000 * 20];
            var random = new Random(1234);
            int start = SampleRate / 1000 * 10;
            for (int i = 0; i < consonant; i++) {
                samples[start + i] = (short)(random.Next(-12000, 12000));
            }
            for (int i = 0; i < vowel; i++) {
                double t = (double)i / SampleRate;
                double value = 12000 * Math.Sin(2 * Math.PI * 220 * t)
                    + 4000 * Math.Sin(4 * Math.PI * 220 * t);
                samples[start + consonant + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            }
            return samples;
        }

        static void WriteWav(string path, short[] samples) {
            using var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 1));
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            writer.Write(bytes, 0, bytes.Length);
        }

        static string NewDirectory() {
            string dir = Path.Combine(Path.GetTempPath(), "sekaiu-fix-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        static double[] Numbers(string line) {
            var parts = line.Split('=')[1].Split(',');
            return parts.Skip(1).Take(4).Select(p => double.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        [Fact]
        public void CorrectsTheNumbersAndKeepsAliasesAndExtraColumns() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string dir = NewDirectory();
            try {
                WriteWav(Path.Combine(dir, "ka.wav"), Take(consonantMs: 60, vowelMs: 350));
                WriteWav(Path.Combine(dir, "a.wav"), Take(consonantMs: 0, vowelMs: 400));
                var oto = Path.Combine(dir, "oto.ini");
                var original = string.Join("\n", new[] {
                    "ka.wav=ka,0,0,0,0,0",
                    "ka.wav=か,0,0,0,0,0",
                    "ka.wav=- ka,0,0,0,0,0",
                    "a.wav=a,0,0,0,0,0,prefix,suffix",
                    "missing.wav=ka,0,0,0,0,0",
                    "",
                });
                File.WriteAllText(oto, original, new UTF8Encoding(false));

                var summary = OtoAutoFix.FixDirectory(dir);

                var lines = File.ReadAllLines(oto);
                Assert.Equal(1, summary.Files);
                Assert.Equal(3, summary.Fixed);

                // Same alias, corrected numbers.
                Assert.StartsWith("ka.wav=ka,", lines[0]);
                Assert.InRange(Numbers(lines[0])[3], 30, 110);
                Assert.StartsWith("ka.wav=か,", lines[1]);
                Assert.InRange(Numbers(lines[1])[3], 30, 110);

                // A VC alias is not measurable: it is left exactly as it was.
                Assert.Equal("ka.wav=- ka,0,0,0,0,0", lines[2]);

                // A vowel has no preutterance, the extra columns survive.
                Assert.StartsWith("a.wav=a,", lines[3]);
                Assert.Equal(0, Numbers(lines[3])[3]);
                Assert.EndsWith(",prefix,suffix", lines[3]);

                Assert.Equal("missing.wav=ka,0,0,0,0,0", lines[4]);

                // The previous file is kept, and running again changes nothing.
                Assert.Equal(original, File.ReadAllText(oto + ".bak"));
                var again = OtoAutoFix.FixDirectory(dir);
                Assert.Equal(0, again.Fixed);
                Assert.Equal(lines, File.ReadAllLines(oto));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void KeepsTheShiftJisAliasesOfJapaneseBanks() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var shiftJis = Encoding.GetEncoding("shift_jis");
            string dir = NewDirectory();
            try {
                WriteWav(Path.Combine(dir, "ka.wav"), Take(consonantMs: 60, vowelMs: 350));
                var oto = Path.Combine(dir, "oto.ini");
                File.WriteAllText(oto, "ka.wav=か,0,0,0,0,0\n", shiftJis);

                OtoAutoFix.FixDirectory(dir);

                var bytes = File.ReadAllBytes(oto);
                string text = shiftJis.GetString(bytes);
                Assert.Contains("か", text);
                Assert.DoesNotContain("\uFFFD", text);
                Assert.InRange(Numbers(text.Trim())[3], 30, 110);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void SilentSamplesAreLeftAlone() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string dir = NewDirectory();
            try {
                WriteWav(Path.Combine(dir, "a.wav"), new short[SampleRate]);
                var oto = Path.Combine(dir, "oto.ini");
                File.WriteAllText(oto, "a.wav=a,12,34,-56,78,90\n", new UTF8Encoding(false));

                var summary = OtoAutoFix.FixDirectory(dir);

                Assert.Equal(0, summary.Fixed);
                Assert.Equal("a.wav=a,12,34,-56,78,90", File.ReadAllText(oto).Trim());
                Assert.False(File.Exists(oto + ".bak"));
            } finally {
                Directory.Delete(dir, true);
            }
        }
    }
}
