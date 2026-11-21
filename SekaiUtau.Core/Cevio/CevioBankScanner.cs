using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.Core.Cevio {
    /// <summary>
    /// Finds CeVIO AI (and VoiSona) song libraries dropped into the singers folders.
    /// Their voice data is licensed and encrypted by Techno-Speech, and the engine has no
    /// external voicebank format, so it cannot be read or rendered here. This scanner exists
    /// so the app says so - and explains the WAV / MIDI route - instead of silently ignoring
    /// the folder the user just installed.
    /// </summary>
    public static class CevioBankScanner {
        static readonly string[] DataExtensions = { ".cvbank", ".cvvoice", ".cvsong", ".tlk", ".cvbankdata" };
        static readonly string[] SongVoiceFolderNames = { "song voice", "ソングボイス", "songvoice" };

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
                        if (!result.Contains(dir) && IsCevioBank(dir)) {
                            result.Add(dir);
                        }
                    }
                } catch (Exception e) {
                    Log.Warning(e, $"Failed to scan {root} for CeVIO voicebanks");
                }
            }
            return result;
        }

        static bool IsCevioBank(string dir) {
            try {
                if (Path.GetFileName(dir).IndexOf("cevio", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)) {
                    var name = Path.GetFileName(sub).ToLowerInvariant();
                    if (SongVoiceFolderNames.Contains(name)) {
                        return true;
                    }
                }
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Any(file =>
                        DataExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) ||
                        file.IndexOf("cevio", StringComparison.OrdinalIgnoreCase) >= 0);
            } catch (Exception) {
                return false;
            }
        }
    }
}
