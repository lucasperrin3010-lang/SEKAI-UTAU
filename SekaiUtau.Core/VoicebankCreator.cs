using System;
using System.IO;
using System.Text;
using SekaiUtau.Classic;
using SekaiUtau.Core.Util;

namespace SekaiUtau.Core {
    /// <summary>
    /// Creates an empty UTAU-style voicebank skeleton in the singers folder. The resulting
    /// folder is picked up by the singer list immediately, so the user only has to drop
    /// their recordings in and fill oto.ini.
    /// </summary>
    public static class VoicebankCreator {
        public const string CharacterTxt = "character.txt";
        public const string CharacterYaml = "character.yaml";
        public const string OtoIni = "oto.ini";
        public const string ReadmeTxt = "README.txt";
        public const string PhotoBaseName = "character";
        public const string PortraitBaseName = "portrait";

        public static string SingersRoot => PathManager.Inst.SingersInstallPath;

        /// <summary>
        /// Creates the voicebank and returns its folder. <paramref name="photoPath"/> is the
        /// optional small icon (the picture shown next to the track) and
        /// <paramref name="portraitPath"/> the optional large picture drawn behind the piano
        /// roll; <paramref name="root"/> is only passed by tests, the application always uses
        /// the singers folder.
        /// </summary>
        public static string Create(string name, string author, string? photoPath = null,
            string? portraitPath = null, string? root = null, string? language = null) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("A voicebank needs a name.", nameof(name));
            }
            root ??= SingersRoot;
            var dir = Path.Combine(root, FolderName(name));
            if (Directory.Exists(dir)) {
                throw new IOException($"Voicebank folder already exists: {dir}");
            }
            Directory.CreateDirectory(dir);

            var photoName = CopyImage(dir, photoPath, PhotoBaseName);
            var portraitName = CopyImage(dir, portraitPath, PortraitBaseName);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var character = new StringBuilder();
            character.Append($"name={name.Trim()}\n");
            character.Append($"author={(author ?? string.Empty).Trim()}\n");
            character.Append("version=1.0\n");
            if (photoName != null) {
                character.Append($"image={photoName}\n");
            }
            if (portraitName != null) {
                character.Append($"portrait={portraitName}\n");
            }
            // The language the recordings were made in (ja, en, fr, ...), so the bank can be
            // recognised later and the matching phonemizer picked without guessing.
            if (!string.IsNullOrWhiteSpace(language)) {
                character.Append($"language={language.Trim()}\n");
            }
            File.WriteAllText(Path.Combine(dir, CharacterTxt), character.ToString(), utf8);

            // character.yaml declares the text encoding, so accented names and lyrics are
            // read as UTF-8 instead of the shift_jis default of old UTAU banks.
            var config = new VoicebankConfig {
                Name = name.Trim(),
                Author = (author ?? string.Empty).Trim(),
                TextFileEncoding = "utf-8",
                Image = photoName,
                Portrait = portraitName,
            };
            using (var stream = File.Create(Path.Combine(dir, CharacterYaml))) {
                config.Save(stream);
            }
            // Empty oto file: the loader then exposes the WAV files by their file name,
            // which is enough to render before any oto tuning.
            File.WriteAllText(Path.Combine(dir, OtoIni), string.Empty, utf8);
            File.WriteAllText(Path.Combine(dir, ReadmeTxt), Readme(name.Trim()), utf8);
            return dir;
        }

        /// <summary>
        /// Copies the chosen picture into the voicebank as &lt;baseName&gt;.&lt;ext&gt; and
        /// returns its file name, or null when no picture was given.
        /// </summary>
        static string? CopyImage(string dir, string? photoPath, string baseName) {
            if (string.IsNullOrWhiteSpace(photoPath) || !File.Exists(photoPath)) {
                return null;
            }
            var extension = Path.GetExtension(photoPath);
            if (string.IsNullOrEmpty(extension)) {
                extension = ".png";
            }
            var photoName = baseName + extension.ToLowerInvariant();
            File.Copy(photoPath, Path.Combine(dir, photoName), overwrite: true);
            return photoName;
        }

        static string FolderName(string name) {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = string.Join("_", name.Trim().Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            cleaned = cleaned.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(cleaned) ? "Voicebank" : cleaned;
        }

        static string Readme(string name) => $"""
            Voicebank {name}
            ================

            Dossier cree par SEKAI UTAU.

            1. Enregistrez vos echantillons au format WAV (mono, 44100 Hz recommande)
               et deposez-les dans ce dossier.
            2. Nommez chaque fichier comme la syllabe qu'il chante (a.wav, ka.wav, ...).
               Sans oto.ini rempli, chaque fichier est utilise tel quel sous son nom.
            3. Pour regler les points de coupe, ouvrez la fenetre Voix de SEKAI UTAU :
               le bouton "Modifier l'oto" permet d'aligner offset, preutterance, overlap.
            4. Les images de la voix se changent dans la fenetre Voix, menu du chanteur :
               "Definir une icone" (petite image des pistes, character.png) et
               "Definir le portrait" (grande image derriere le piano roll, portrait.png).

            Le nom et l'auteur de la voix se modifient dans character.txt.
            """;
    }
}
