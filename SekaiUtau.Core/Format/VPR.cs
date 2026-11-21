using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

using SekaiUtau.Core.Ustx;

namespace SekaiUtau.Core.Format;

/// <summary>
/// VOCALOID5 / VOCALOID6 project file (.vpr). Unlike the older VSQX, a VPR is a ZIP
/// archive whose <c>Project/sequence.json</c> holds the whole song: tempo and time
/// signature events on the master track, then the voice tracks with their parts and
/// notes (MIDI number + lyric). Time is counted in ticks with a fixed resolution of
/// 480 ticks per quarter note. Reading is tolerant about element names because the
/// JSON has drifted between VOCALOID5 and VOCALOID6.
/// </summary>
public static class VPR {
    /// <summary>Ticks per quarter note, fixed by the VOCALOID sequence format.</summary>
    const int Resolution = 480;

    const string SequenceEntry = "sequence.json";

    public static UProject Load(string path) {
        string json;
        try {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').EndsWith(SequenceEntry, StringComparison.OrdinalIgnoreCase));
            if (entry == null) {
                throw new FileFormatException("The VPR file has no Project/sequence.json.");
            }
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        } catch (InvalidDataException e) {
            throw new FileFormatException($"Error loading VPR file: {e.Message}", e);
        }

        JsonDocument doc;
        try {
            doc = JsonDocument.Parse(json);
        } catch (JsonException e) {
            throw new FileFormatException($"Error reading the VPR sequence: {e.Message}", e);
        }

        using (doc) {
            return LoadProject(doc.RootElement, path);
        }
    }

    static UProject LoadProject(JsonElement root, string path) {
        var project = new UProject {
            FilePath = path,
            name = Str(root, "title", "name") ?? Path.GetFileNameWithoutExtension(path),
        };
        Ustx.AddDefaultExpressions(project);
        project.RegisterExpression(new UExpressionDescriptor("opening", "ope", 0, 100, 100));
        // A new project starts with an empty default track; the VPR tracks replace it.
        project.tracks.Clear();
        project.parts.Clear();
        project.tempos.Clear();
        project.timeSignatures.Clear();

        var masterTrack = Prop(root, "masterTrack");
        int preMeasure = masterTrack.HasValue ? (int?)Int(Prop(masterTrack.Value, "preMeasure") ?? default, 0) ?? 0 : 0;

        LoadTempos(project, masterTrack);
        LoadTimeSignatures(project, masterTrack);
        if (project.tempos.Count == 0) {
            project.tempos.Add(new UTempo(0, 120));
        }
        if (project.timeSignatures.Count == 0) {
            project.timeSignatures.Add(new UTimeSignature(0, 4, 4));
        }

        int shift = -preMeasure * Resolution * 4 / Math.Max(1, project.timeSignatures[0].beatUnit)
                    * project.timeSignatures[0].beatPerBar;
        if (preMeasure <= 0) {
            shift = 0;
        }

        var tracks = FindArray(root, "tracks");
        if (tracks == null) {
            throw new FileFormatException("The VPR file contains no tracks.");
        }

        var singer = USinger.CreateMissing("");
        int trackNo = 0;
        foreach (var trackElement in tracks.Value.EnumerateArray()) {
            var parts = Prop(trackElement, "parts");
            if (parts == null || parts.Value.ValueKind != JsonValueKind.Array) {
                continue;
            }
            string trackName = Str(trackElement, "name", "trackName") ?? $"Track {trackNo + 1}";
            var track = new UTrack(project) {
                TrackNo = trackNo,
                TrackName = trackName,
                Singer = singer,
            };

            var projectParts = new List<UVoicePart>();
            foreach (var partElement in parts.Value.EnumerateArray()) {
                var part = LoadPart(project, partElement, shift);
                if (part == null) {
                    continue;
                }
                part.trackNo = trackNo;
                projectParts.Add(part);
            }
            if (projectParts.Count == 0) {
                continue;
            }
            project.tracks.Add(track);
            foreach (var part in projectParts) {
                project.parts.Add(part);
            }
            trackNo++;
        }

        if (project.tracks.Count == 0) {
            throw new FileFormatException("The VPR file contains no voice notes.");
        }

        project.AfterLoad();
        project.ValidateFull();
        return project;
    }

    /// <summary>
    /// Reads one part. Note positions are relative to the part start in VOCALOID5, but
    /// some exports store them already absolute, so the two cases are told apart by
    /// whether every note fits inside the part length.
    /// </summary>
    static UVoicePart? LoadPart(UProject project, JsonElement element, int shift) {
        var notesElement = Prop(element, "notes");
        if (notesElement == null || notesElement.Value.ValueKind != JsonValueKind.Array) {
            return null;
        }
        var notes = notesElement.Value.EnumerateArray()
            .Where(n => n.ValueKind == JsonValueKind.Object)
            .ToList();
        if (notes.Count == 0) {
            return null;
        }

        int partPosition = Math.Max(0, (Int(Prop(element, "pos") ?? default, 0) ?? 0) + shift);
        int rawDuration = Int(Prop(element, "duration", "dur", "length") ?? default, 0) ?? 0;

        int maxEnd = notes
            .Select(n => (Int(Prop(n, "pos", "startTick", "tick") ?? default, 0) ?? 0)
                         + (Int(Prop(n, "duration", "dur", "length") ?? default, 0) ?? 0))
            .DefaultIfEmpty(0)
            .Max();
        bool positionsAreAbsolute = rawDuration > 0 && maxEnd > rawDuration;

        var part = new UVoicePart {
            name = Str(element, "name") ?? string.Empty,
            position = partPosition,
        };

        foreach (var noteElement in notes) {
            int duration = Int(Prop(noteElement, "duration", "dur", "length") ?? default, 0) ?? 0;
            if (duration <= 0) {
                continue;
            }
            int rawPosition = Int(Prop(noteElement, "pos", "startTick", "tick") ?? default, 0) ?? 0;
            int position = positionsAreAbsolute ? Math.Max(0, rawPosition + shift) : rawPosition;
            int tone = Int(Prop(noteElement, "number", "pitch", "noteNumber") ?? default, 60) ?? 60;
            var note = project.CreateNote(tone, position, duration);
            string lyric = Str(noteElement, "lyric", "y") ?? string.Empty;
            note.lyric = string.IsNullOrEmpty(lyric) || lyric == "-" ? "+~" : lyric;
            if (string.IsNullOrEmpty(lyric)) {
                note.lyric = "a";
            }
            part.notes.Add(note);
            maxEnd = Math.Max(maxEnd, position + duration);
        }

        if (part.notes.Count == 0) {
            return null;
        }
        part.Duration = Math.Max(rawDuration, maxEnd);
        return part;
    }

    static void LoadTempos(UProject project, JsonElement? masterTrack) {
        if (masterTrack == null) {
            return;
        }
        var events = Prop(masterTrack.Value, "tempo") is { } tempo ? Prop(tempo, "events") : null;
        if (events == null || events.Value.ValueKind != JsonValueKind.Array) {
            return;
        }
        foreach (var element in events.Value.EnumerateArray()) {
            int position = Math.Max(0, Int(Prop(element, "pos", "tick", "t") ?? default, 0) ?? 0);
            double raw = Num(Prop(element, "value", "v", "bpm") ?? default, 0);
            // VOCALOID stores BPM multiplied by 100 (12000 = 120 BPM); small values are BPM already.
            double bpm = raw > 1000 ? raw / 100.0 : raw;
            if (bpm <= 0) {
                continue;
            }
            if (!project.tempos.Any(t => t.position == position)) {
                project.tempos.Add(new UTempo(position, bpm));
            }
        }
        project.tempos.Sort((a, b) => a.position.CompareTo(b.position));
        if (project.tempos.Count > 0 && project.tempos[0].position > 0) {
            project.tempos.Insert(0, new UTempo(0, project.tempos[0].bpm));
        }
    }

    /// <summary>
    /// Time signatures are stored at a bar number; bars are turned into ticks by walking
    /// the list with the bar length of the previous entry.
    /// </summary>
    static void LoadTimeSignatures(UProject project, JsonElement? masterTrack) {
        if (masterTrack == null) {
            return;
        }
        var events = Prop(masterTrack.Value, "timeSig", "timeSignature") is { } meter ? Prop(meter, "events") : null;
        if (events == null || events.Value.ValueKind != JsonValueKind.Array) {
            return;
        }
        var raw = events.Value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => (
                bar: Math.Max(0, Int(Prop(e, "bar", "pos", "tick") ?? default, 0) ?? 0),
                beats: Math.Max(1, Int(Prop(e, "numer", "numerator", "nu") ?? default, 4) ?? 4),
                beatUnit: Math.Max(1, Int(Prop(e, "denom", "denominator", "de") ?? default, 4) ?? 4)))
            .OrderBy(t => t.bar)
            .ToList();
        if (raw.Count == 0) {
            project.timeSignatures.Add(new UTimeSignature(0, 4, 4));
            return;
        }

        int currentBar = raw[0].bar;
        int currentTick = 0;
        int barLength = raw[0].beats * (Resolution * 4 / raw[0].beatUnit);
        foreach (var (bar, beats, beatUnit) in raw) {
            if (bar > currentBar && barLength > 0) {
                currentTick += (bar - currentBar) * barLength;
            }
            currentBar = bar;
            barLength = beats * (Resolution * 4 / beatUnit);
            int barPosition = barLength > 0 ? currentTick / Math.Max(1, barLength) : 0;
            if (!project.timeSignatures.Any(s => s.barPosition == barPosition)) {
                project.timeSignatures.Add(new UTimeSignature(barPosition, beats, beatUnit));
            }
        }
        project.timeSignatures.Sort((a, b) => a.barPosition.CompareTo(b.barPosition));
        if (project.timeSignatures.Count > 0 && project.timeSignatures[0].barPosition > 0) {
            project.timeSignatures.Insert(0,
                new UTimeSignature(0, project.timeSignatures[0].beatPerBar, project.timeSignatures[0].beatUnit));
        }
    }

    static JsonElement? Prop(JsonElement element, params string[] names) {
        if (element.ValueKind != JsonValueKind.Object) {
            return null;
        }
        foreach (var property in element.EnumerateObject()) {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))) {
                return property.Value;
            }
        }
        return null;
    }

    /// <summary>Finds an array named <paramref name="name"/> at the root, or nested in a wrapper object.</summary>
    static JsonElement? FindArray(JsonElement root, string name) {
        foreach (var property in root.EnumerateObject()) {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Array) {
                return property.Value;
            }
        }
        foreach (var property in root.EnumerateObject()) {
            if (property.Value.ValueKind == JsonValueKind.Object) {
                var nested = FindArray(property.Value, name);
                if (nested.HasValue) {
                    return nested;
                }
            }
        }
        return null;
    }

    static string? Str(JsonElement element, params string[] names) {
        var value = Prop(element, names);
        if (value == null) {
            return null;
        }
        return value.Value.ValueKind switch {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    static int? Int(JsonElement element, int fallback = 0) => ElementToInt(element, fallback);

    static int? ElementToInt(JsonElement element, int fallback) {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number)) {
            return (int)Math.Round(number);
        }
        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) {
            return (int)Math.Round(parsed);
        }
        return fallback;
    }

    static double Num(JsonElement element, double fallback) {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number)) {
            return number;
        }
        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) {
            return parsed;
        }
        return fallback;
    }
}
