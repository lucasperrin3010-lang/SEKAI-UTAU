using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.Core.Vocaloid {
    /// <summary>
    /// Finds VOCALOID voicebanks dropped into the singers folders.
    /// Their voice data is encrypted by the vendor (.ddb, .ddi, .vvd, .vvb), so it cannot be read
    /// or rendered here. This scanner exists so the app says so instead of silently ignoring the
    /// folder the user just installed.
    /// </summary>
    public static class VocaloidBankScanner {
        static readonly string[] DataExtensions = { ".ddb", ".ddi", ".vvd", ".vvb" };

        public static List<string> FindBankFolders() {
            var roots = new List<string> { PathManager.Inst.SingersPath };
            var additional = PathManager.Inst.AdditionalSingersPath;
            if (!string.IsNullOrWhiteSpace(additional)) {
                roots.Add(additional);
            }
            var result = new List<string>();
            foreach (var root in roots.Distinct()) {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
                    continue;
                }
                try {
                    foreach (var dir in Directory.EnumerateDirectories(root)) {
                        if (!result.Contains(dir) && IsVocaloidBank(dir)) {
                            result.Add(dir);
                        }
                    }
                } catch (Exception e) {
                    Log.Warning(e, $"Failed to scan {root} for VOCALOID voicebanks");
                }
            }
            return result;
        }

        static bool IsVocaloidBank(string dir) {
            try {
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Any(file => DataExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));
            } catch (Exception) {
                return false;
            }
        }
    }
}
