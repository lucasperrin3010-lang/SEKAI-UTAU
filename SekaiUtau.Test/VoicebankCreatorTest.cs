using System;
using System.IO;
using System.Linq;
using System.Text;
using SekaiUtau.Classic;
using Xunit;

namespace SekaiUtau.Core {
    public class VoicebankCreatorTest {
        [Fact]
        public void CreatedVoicebankIsFoundByTheSingerLoader() {
            // The voicebank loader decodes character.txt as shift_jis even when
            // character.yaml says utf-8, so the codepage provider must be registered.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var root = Path.Combine(Path.GetTempPath(), "sekaiu-voicebank-" + Path.GetRandomFileName());
            try {
                var dir = VoicebankCreator.Create("Voix de test", "Auteur de test", root: root);

                Assert.True(File.Exists(Path.Combine(dir, VoicebankCreator.CharacterTxt)));
                Assert.True(File.Exists(Path.Combine(dir, VoicebankCreator.CharacterYaml)));
                Assert.True(File.Exists(Path.Combine(dir, VoicebankCreator.OtoIni)));

                var banks = new VoicebankLoader(root).SearchAll().ToList();
                var bank = Assert.Single(banks);
                Assert.Equal("Voix de test", bank.Name);
                Assert.Equal("Auteur de test", bank.Author);
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void PhotoIsCopiedIntoTheVoicebank() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var root = Path.Combine(Path.GetTempPath(), "sekaiu-voicebank-" + Path.GetRandomFileName());
            try {
                var photo = Path.Combine(root, "portrait.png");
                Directory.CreateDirectory(root);
                File.WriteAllBytes(photo, SamplePng);
                var dir = VoicebankCreator.Create("Voix illustree", "Moi", photo, root: root);

                var copied = Path.Combine(dir, "character.png");
                Assert.True(File.Exists(copied));
                Assert.Equal(SamplePng, File.ReadAllBytes(copied));
                Assert.Contains("image=character.png", File.ReadAllText(Path.Combine(dir, VoicebankCreator.CharacterTxt)));

                var bank = Assert.Single(new VoicebankLoader(root).SearchAll());
                Assert.Equal("character.png", bank.Image);
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void IconAndPortraitAreBothCopiedAndDeclared() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var root = Path.Combine(Path.GetTempPath(), "sekaiu-voicebank-" + Path.GetRandomFileName());
            try {
                Directory.CreateDirectory(root);
                var icon = Path.Combine(root, "icone.png");
                var portrait = Path.Combine(root, "grande.png");
                File.WriteAllBytes(icon, SamplePng);
                File.WriteAllBytes(portrait, SamplePng);

                var dir = VoicebankCreator.Create("Deux images", "Moi", icon, portrait, root);

                Assert.True(File.Exists(Path.Combine(dir, "character.png")));
                Assert.True(File.Exists(Path.Combine(dir, "portrait.png")));
                var character = File.ReadAllText(Path.Combine(dir, VoicebankCreator.CharacterTxt));
                Assert.Contains("image=character.png", character);
                Assert.Contains("portrait=portrait.png", character);

                var bank = Assert.Single(new VoicebankLoader(root).SearchAll());
                Assert.Equal("character.png", bank.Image);
                Assert.Equal("portrait.png", bank.Portrait);
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, true);
                }
            }
        }

        // 1x1 transparent PNG.
        static readonly byte[] SamplePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        [Fact]
        public void CreatingTwiceInTheSameFolderFails() {
            var root = Path.Combine(Path.GetTempPath(), "sekaiu-voicebank-" + Path.GetRandomFileName());
            try {
                VoicebankCreator.Create("Doublon", string.Empty, root: root);
                Assert.Throws<IOException>(() => VoicebankCreator.Create("Doublon", string.Empty, root: root));
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
