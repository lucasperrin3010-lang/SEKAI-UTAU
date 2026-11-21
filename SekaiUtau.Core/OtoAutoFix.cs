using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SekaiUtau.Core {
    /// <summary>What an automatic oto.ini correction changed.</summary>
    public sealed record OtoFixSummary(int Files, int Lines, int Fixed, int Skipped) {
        public static OtoFixSummary Empty { get; } = new(0, 0, 0, 0);
    }

    /// <summary>
    /// Rewrites the numbers of the existing oto.ini files of a voicebank from the audio itself.
    /// The aliases, the order of the lines and any extra column (prefix, suffix, set, colour) are
    /// kept as they are, a copy of every file is saved as <c>oto.ini.bak</c>, and a line is left
    /// untouched when the sample cannot be measured (missing WAV, VC or VCV alias, silence).
    /// </summary>
    public static class OtoAutoFix {
        const string OtoIni = "oto.ini";

        /// <summary>Corrects every oto.ini found in <paramref name="directory"/> and its subfolders.</summary>
        public static OtoFixSummary FixDirectory(string directory, Action<int, int>? progress = null) {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) {
                return OtoFixSummary.Empty;
            }
            List<string> files;
            try {
                files = new List<string>(Directory.EnumerateFiles(directory, OtoIni, SearchOption.AllDirectories));
            } catch (UnauthorizedAccessException) {
                return OtoFixSummary.Empty;
            } catch (IOException) {
                return OtoFixSummary.Empty;
            }
            int totalLines = 0, totalFixed = 0, totalSkipped = 0, done = 0;
            foreach (string file in files) {
                try {
                    var (lines, fixedCount, skipped) = FixFile(file);
                    totalLines += lines;
                    totalFixed += fixedCount;
                    totalSkipped += skipped;
                } catch (Exception) {
                    // An unreadable or read-only oto.ini must not stop the other sets.
                }
                progress?.Invoke(++done, files.Count);
            }
            return new OtoFixSummary(files.Count, totalLines, totalFixed, totalSkipped);
        }

        /// <summary>Corrects one oto.ini. Returns the number of lines, of corrected lines and of skipped ones.</summary>
        public static (int Lines, int Fixed, int Skipped) FixFile(string file) {
            EnsureEncodings();
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length == 0) {
                return (0, 0, 0);
            }
            var (encoding, bom) = DetectEncoding(bytes);
            string text = StripBom(encoding.GetString(bytes), bom);
            string directory = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            bool trailingNewline = text.EndsWith("\n", StringComparison.Ordinal);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            // Directory listings are case-sensitive on some systems, while oto.ini often is not.
            var present = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(directory)) {
                foreach (string path in Directory.EnumerateFiles(directory)) {
                    present.TryAdd(Path.GetFileName(path), path);
                }
            }

            int corrected = 0, skipped = 0;
            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal)
                    || trimmed.StartsWith(";", StringComparison.Ordinal)
                    || !TryFixLine(trimmed, present, out string fixedLine)) {
                    skipped++;
                    continue;
                }
                if (fixedLine != trimmed) {
                    lines[i] = fixedLine;
                    corrected++;
                }
            }
            if (corrected > 0) {
                File.Copy(file, file + ".bak", true);
                string joined = string.Join(newline, lines);
                if (!trailingNewline && joined.EndsWith(newline, StringComparison.Ordinal)) {
                    joined = joined.Substring(0, joined.Length - newline.Length);
                }
                byte[] encoded = encoding.GetBytes(joined);
                if (bom) {
                    encoded = Combine(Encoding.UTF8.GetPreamble(), encoded);
                }
                File.WriteAllBytes(file, encoded);
            }
            int total = 0;
            foreach (string line in lines) {
                if (!string.IsNullOrWhiteSpace(line)) {
                    total++;
                }
            }
            return (total, corrected, skipped);
        }

        static bool TryFixLine(string line, Dictionary<string, string> present, out string fixedLine) {
            fixedLine = line;
            int equals = line.IndexOf('=');
            if (equals <= 0) {
                return false;
            }
            string wav = line.Substring(0, equals).Trim();
            if (wav.Length == 0 || !wav.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            string[] parts = line.Substring(equals + 1).Split(',');
            if (parts.Length < 6) {
                return false;
            }
            string alias = parts[0].Trim();
            if (!present.TryGetValue(Path.GetFileName(wav), out string? path)) {
                return false;
            }
            var estimate = AutoOto.EstimateFile(path, alias);
            if (!estimate.Reliable) {
                return false;
            }
            var values = estimate.Values;
            parts[1] = values.Offset.ToString(CultureInfo.InvariantCulture);
            parts[2] = values.Consonant.ToString(CultureInfo.InvariantCulture);
            parts[3] = values.Cutoff.ToString(CultureInfo.InvariantCulture);
            parts[4] = values.Preutterance.ToString(CultureInfo.InvariantCulture);
            parts[5] = values.Overlap.ToString(CultureInfo.InvariantCulture);
            fixedLine = wav + "=" + string.Join(",", parts);
            return true;
        }

        static void EnsureEncodings() {
            try {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            } catch (Exception) {
                // Already registered, or no code pages available: UTF-8 and Latin-1 still work.
            }
        }

        /// <summary>
        /// oto.ini is usually Shift-JIS for Japanese banks and UTF-8 for recent ones, and it can
        /// declare its own charset. Reading it with the wrong encoding would corrupt the aliases
        /// when the file is written back, so the encoding is detected before anything is touched.
        /// </summary>
        static (Encoding Encoding, bool Bom) DetectEncoding(byte[] bytes) {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
                return (new UTF8Encoding(false, false), true);
            }
            int probe = Math.Min(bytes.Length, 512);
            string head = Encoding.Latin1.GetString(bytes, 0, probe);
            foreach (string raw in head.Replace("\r\n", "\n").Split('\n')) {
                string line = raw.Trim();
                if (!line.StartsWith("#Charset:", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                try {
                    return (Encoding.GetEncoding(line.Substring(9).Trim()), false);
                } catch (ArgumentException) {
                    break;
                }
            }
            try {
                new UTF8Encoding(false, true).GetString(bytes);
                return (new UTF8Encoding(false, false), false);
            } catch (DecoderFallbackException) {
            }
            try {
                return (Encoding.GetEncoding("shift_jis"), false);
            } catch (ArgumentException) {
                return (Encoding.Latin1, false);
            }
        }

        static string StripBom(string text, bool bom) =>
            bom && text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;

        static byte[] Combine(byte[] first, byte[] second) {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }
    }
}
