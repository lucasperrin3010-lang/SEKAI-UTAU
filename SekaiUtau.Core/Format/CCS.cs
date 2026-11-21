using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SekaiUtau.Core.Ustx;

namespace SekaiUtau.Core.Format;

/// <summary>
/// CeVIO project file (.ccs). It is plain XML: each song track is a
/// <c>Unit Category="SingerSong"</c> holding a <c>Song</c> with <c>Tempo</c>,
/// <c>Beat</c> and <c>Score</c> (notes). CeVIO counts 960 clocks per quarter note.
/// </summary>
public static class CCS {
    /// <summary>CeVIO clocks per quarter note, from the CeVIO user guide.</summary>
    const int CevioClocksPerQuarter = 960;

    public static UProject LoadProject(string path) {
        XDocument doc;
        try {
            doc = XDocument.Load(path);
        } catch (Exception e) {
            throw new FileFormatException($"Error loading CCS file: {e.Message}", e);
        }

        var project = new UProject();
        Ustx.AddDefaultExpressions(project);
        // A new project starts with an empty default track; the song's units replace it.
        project.tracks.Clear();
        project.parts.Clear();
        project.tempos.Clear();
        project.timeSignatures.Clear();

        var units = doc.Descendants()
            .Where(e => e.Name.LocalName == "Unit" && Str(e, "Category") == "SingerSong")
            .ToList();
        if (units.Count == 0) {
            // Older projects may omit Category; fall back to every unit that has a Score.
            units = doc.Descendants()
                .Where(e => e.Name.LocalName == "Unit" && e.Descendants().Any(d => d.Name.LocalName == "Score"))
                .ToList();
        }

        int clockToTick(int clock) =>
            (int)Math.Round(clock * (double)project.resolution / CevioClocksPerQuarter);

        bool globalLoaded = false;

        foreach (var unit in units) {
            var song = unit.Descendants().FirstOrDefault(e => e.Name.LocalName == "Song");
            if (song == null) {
                continue;
            }

            if (!globalLoaded) {
                LoadTempos(project, song, clockToTick);
                LoadTimeSignatures(project, song);
                globalLoaded = true;
            }

            var track = new UTrack(project) {
                TrackNo = project.tracks.Count,
                TrackName = TrackName(unit, project.tracks.Count),
                Singer = USinger.CreateMissing(""),
            };

            var part = new UVoicePart {
                name = track.TrackName,
                position = 0,
                trackNo = track.TrackNo,
            };

            int maxEnd = 0;
            var score = song.Descendants().FirstOrDefault(e => e.Name.LocalName == "Score");
            if (score != null) {
                foreach (var el in score.Descendants().Where(e => e.Name.LocalName == "Note")) {
                    int clock = Int(el, "Clock");
                    int duration = Int(el, "Duration");
                    if (duration <= 0) {
                        continue;
                    }
                    int position = Math.Max(0, clockToTick(clock));
                    int length = Math.Max(1, clockToTick(clock + duration) - position);
                    int tone = (Int(el, "PitchOctave", 4) + 1) * 12 + Int(el, "PitchStep");
                    var note = project.CreateNote(tone, position, length);
                    string lyric = Str(el, "Lyric");
                    note.lyric = string.IsNullOrEmpty(lyric) ? "a" : lyric;
                    part.notes.Add(note);
                    maxEnd = Math.Max(maxEnd, position + length);
                }
            }

            if (part.notes.Count == 0) {
                continue;
            }
            part.Duration = maxEnd;
            project.parts.Add(part);
            project.tracks.Add(track);
        }

        if (project.tracks.Count == 0) {
            throw new FileFormatException("The CCS file contains no song notes.");
        }
        if (project.tempos.Count == 0) {
            project.tempos.Add(new UTempo(0, 120));
        }
        if (project.timeSignatures.Count == 0) {
            project.timeSignatures.Add(new UTimeSignature(0, 4, 4));
        }
        project.ValidateFull();
        return project;
    }

    static string TrackName(XElement unit, int index) {
        string cast = Str(unit, "CastId");
        if (!string.IsNullOrWhiteSpace(cast)) {
            return cast;
        }
        return $"Song {index + 1}";
    }

    static void LoadTempos(UProject project, XElement song, Func<int, int> clockToTick) {
        foreach (var tempo in song.Descendants().Where(e => e.Name.LocalName == "Sound")) {
            double bpm = Double(tempo, "Tempo", 120);
            if (bpm <= 0) {
                continue;
            }
            int tick = Math.Max(0, clockToTick(Int(tempo, "Clock")));
            if (!project.tempos.Any(t => t.position == tick)) {
                project.tempos.Add(new UTempo(tick, bpm));
            }
        }
        project.tempos.Sort((a, b) => a.position.CompareTo(b.position));
        if (project.tempos.Count > 0 && project.tempos[0].position > 0) {
            project.tempos.Insert(0, new UTempo(0, project.tempos[0].bpm));
        }
    }

    static void LoadTimeSignatures(UProject project, XElement song) {
        // Time signatures are stored at their bar position, which has to be counted in clocks.
        var raw = song.Descendants()
            .Where(e => e.Name.LocalName == "Time")
            .Select(e => (clock: Int(e, "Clock"), beats: Int(e, "Beats", 4), beatType: Int(e, "BeatType", 4)))
            .Where(t => t.beats > 0 && t.beatType > 0)
            .OrderBy(t => t.clock)
            .ToList();
        if (raw.Count == 0) {
            project.timeSignatures.Add(new UTimeSignature(0, 4, 4));
            return;
        }

        int bar = 0;
        int previousClock = 0;
        int previousBarClocks = raw[0].beats * (4 * CevioClocksPerQuarter / raw[0].beatType);
        foreach (var (clock, beats, beatType) in raw) {
            if (clock > previousClock && previousBarClocks > 0) {
                bar += (clock - previousClock) / previousBarClocks;
            }
            previousClock = clock;
            previousBarClocks = beats * (4 * CevioClocksPerQuarter / beatType);
            if (!project.timeSignatures.Any(s => s.barPosition == bar)) {
                project.timeSignatures.Add(new UTimeSignature(bar, beats, beatType));
            }
        }
        project.timeSignatures.Sort((a, b) => a.barPosition.CompareTo(b.barPosition));
        if (project.timeSignatures.Count > 0 && project.timeSignatures[0].barPosition > 0) {
            project.timeSignatures.Insert(0, new UTimeSignature(0, project.timeSignatures[0].beatPerBar, project.timeSignatures[0].beatUnit));
        }
    }

    static string Str(XElement e, string attribute) =>
        (string?)e.Attribute(attribute) ?? string.Empty;

    static int Int(XElement e, string attribute, int fallback = 0) =>
        int.TryParse(Str(e, attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    static double Double(XElement e, string attribute, double fallback) =>
        double.TryParse(Str(e, attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
}
