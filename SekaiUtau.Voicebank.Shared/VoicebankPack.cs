using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SekaiUtau.Core;

namespace SekaiUtau.Voicebank;

/// <summary>
/// Turns the recorded takes into a real voicebank folder (WAV files, oto.ini with
/// romaji and kana aliases, character.txt/character.yaml) and packs it into a ZIP
/// that drops straight into the Singers folder of SEKAI UTAU on PC.
/// </summary>
public static class VoicebankPack {
    public sealed record Take(short[] Samples, int Start, int Length);

    public static string BuildZip(string name, string author,
        IReadOnlyList<RecordingSyllable> script, IReadOnlyDictionary<string, Take> takes,
        OtoMode otoMode, string? language = null) {
        var cache = VoicebankHost.Platform.CacheDirectory;
        var work = Path.Combine(cache, "voicebank");
        if (Directory.Exists(work)) {
            Directory.Delete(work, true);
        }
        Directory.CreateDirectory(work);

        string dir = VoicebankCreator.Create(name, author, null, null, work, language);
        var recorded = new List<RecordingSyllable>();
        foreach (var syllable in script) {
            if (!takes.TryGetValue(syllable.Alias, out var take) || take.Length <= 0) {
                continue;
            }
            // Same loudness for every syllable, like the desktop recorder does: a bank recorded
            // at an uneven distance from the phone then sings at one level.
            var kept = VoicebankRecording.LevelTake(take.Samples, take.Start, take.Length);
            WavFile.Write(Path.Combine(dir, VoicebankRecording.FileName(syllable)),
                kept, 0, kept.Length, VoicebankHost.Platform.SampleRate);
            recorded.Add(syllable);
        }
        // The takes were cut around their sound above, so each WAV starts on its syllable.
        VoicebankRecording.WriteOto(dir, recorded, otoMode, trimmedTakes: true);

        string zipPath = Path.Combine(cache, SafeFileName(name) + ".zip");
        if (File.Exists(zipPath)) {
            File.Delete(zipPath);
        }
        // includeBaseDirectory: the ZIP holds "Name/character.txt, Name/oto.ini, ..."
        // so extracting it into Singers is enough.
        ZipFile.CreateFromDirectory(dir, zipPath, CompressionLevel.Optimal, true);
        return zipPath;
    }

    /// <summary>Hands the ZIP to the platform so the user can find it.</summary>
    public static string Save(string zipPath) => VoicebankHost.Platform.Save(zipPath);

    static string SafeFileName(string name) {
        var builder = new StringBuilder();
        foreach (char c in name.Trim()) {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }
        string result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "voicebank" : result;
    }
}
