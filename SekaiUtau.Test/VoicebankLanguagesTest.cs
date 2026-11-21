using System;
using System.IO;
using System.Linq;
using SekaiUtau.Core;
using Xunit;

namespace SekaiUtau.Core {
    public class VoicebankLanguagesTest {
        [Fact]
        public void EveryLanguageHasACompleteAndAnEssentialScript() {
            Assert.True(VoicebankLanguages.All.Count >= 15,
                $"only {VoicebankLanguages.All.Count} languages are offered");

            var ids = VoicebankLanguages.All.Select(language => language.Id).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Contains("ja", ids);
            Assert.Contains("fr", ids);
            Assert.Contains("zh", ids);

            foreach (var language in VoicebankLanguages.All) {
                Assert.False(string.IsNullOrWhiteSpace(language.Name), $"{language.Id} has no name");
                Assert.False(string.IsNullOrWhiteSpace(language.Phonemizer), $"{language.Id} has no phonemizer");
                Assert.True(language.Essentials.Count > 0, $"{language.Id} has no essential syllables");
                Assert.True(language.Full.Count > language.Essentials.Count,
                    $"{language.Id} full script ({language.Full.Count}) is not bigger than the essentials ({language.Essentials.Count})");

                var aliases = language.Full.Select(syllable => syllable.Alias).ToArray();
                Assert.Equal(aliases.Length, aliases.Distinct().Count());
                Assert.All(aliases, alias => {
                    Assert.False(string.IsNullOrWhiteSpace(alias));
                    // The alias is written in oto.ini next to the file name, separated by
                    // '=' and ',': those characters, spaces and path separators cannot be in it.
                    Assert.DoesNotContain(alias, c => c == '=' || c == ',' || c == ' ' || c == '/' || c == '\\');
                });

                var essential = language.Essentials.Select(syllable => syllable.Alias).ToHashSet();
                Assert.All(essential, alias => Assert.Contains(alias, aliases));
            }
        }

        [Fact]
        public void JapaneseKeepsItsKanaAliases() {
            var japanese = VoicebankLanguages.Japanese;

            Assert.Equal("ja", japanese.Id);
            Assert.Contains(japanese.Full, syllable => syllable.Alias == "ka" && syllable.Kana == "か");
            // The vowels and the final n are the small essential script.
            Assert.Equal(6, japanese.Essentials.Count);
        }

        [Fact]
        public void LanguagesUseTheSymbolsOfTheirPhonemizer() {
            var french = VoicebankLanguages.Find("fr")!;
            Assert.Contains(french.Full, syllable => syllable.Alias == "ba");
            Assert.Contains(french.Full, syllable => syllable.Alias == "R2");
            Assert.All(french.Full, syllable => Assert.Equal(string.Empty, syllable.Kana));

            var english = VoicebankLanguages.Find("en")!;
            Assert.Contains(english.Full, syllable => syllable.Alias == "dhih");
            Assert.Contains(english.Essentials, syllable => syllable.Alias == "bae");

            var korean = VoicebankLanguages.Find("ko")!;
            Assert.Contains(korean.Full, syllable => syllable.Alias == "geu");

            var mandarin = VoicebankLanguages.Find("zh")!;
            Assert.Contains(mandarin.Full, syllable => syllable.Alias == "zhi");
            Assert.Contains(mandarin.Full, syllable => syllable.Alias == "bian");
        }

        [Fact]
        public void ScriptHelperFallsBackToJapanese() {
            Assert.Same(new VoicebankLanguage[] { VoicebankLanguages.Japanese }.Single().Full,
                VoicebankLanguages.Script(null, full: true));
            Assert.Equal(VoicebankLanguages.Japanese.Essentials,
                VoicebankLanguages.Script(VoicebankLanguages.Japanese, full: false));
            Assert.Null(VoicebankLanguages.Find("xx"));
        }

        [Fact]
        public void ASpanishBankIsWrittenWithItsOwnAliases() {
            var dir = Path.Combine(Path.GetTempPath(), "sekaiu-lang-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try {
                var script = VoicebankLanguages.Script(VoicebankLanguages.Find("es"), full: false)
                    .Where(syllable => syllable.Alias is "a" or "ba")
                    .ToArray();
                var samples = new short[44100 / 2];
                foreach (var syllable in script) {
                    File.WriteAllBytes(Path.Combine(dir, VoicebankRecording.FileName(syllable)),
                        Array.Empty<byte>());
                }

                VoicebankRecording.WriteOto(dir, script, OtoMode.Manual);

                var lines = File.ReadAllLines(Path.Combine(dir, VoicebankCreator.OtoIni))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();
                // One line per sample: a language without kana gets no second alias.
                Assert.Equal(2, lines.Length);
                Assert.Equal("a.wav=a,0,0,0,0,0", lines[0]);
                Assert.Equal("ba.wav=ba,0,0,0,0,0", lines[1]);
            } finally {
                Directory.Delete(dir, true);
            }
        }
    }
}
