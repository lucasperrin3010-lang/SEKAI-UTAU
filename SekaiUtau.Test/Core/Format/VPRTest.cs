using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

using SekaiUtau.Core.Ustx;

namespace SekaiUtau.Core.Format {
    public class VPRTest {
        // VOCALOID5 / VOCALOID6 projects are ZIP archives with Project/sequence.json.
        // Note positions inside a part are relative to the part start, tempo values are BPM x 100.
        const string Sequence = """
{
  "title": "Song",
  "masterTrack": {
    "tempo": { "events": [ { "pos": 0, "value": 12000 }, { "pos": 1920, "value": 9000 } ] },
    "timeSig": { "events": [ { "bar": 0, "numer": 4, "denom": 4 } ] }
  },
  "tracks": [
    {
      "type": 2,
      "name": "Miku",
      "parts": [
        {
          "name": "part 1",
          "pos": 1920,
          "duration": 3840,
          "notes": [
            { "pos": 0, "duration": 480, "number": 60, "lyric": "ら", "phoneme": "4 a", "velocity": 64 },
            { "pos": 480, "duration": 960, "number": 64, "lyric": "-", "phoneme": "a", "velocity": 64 }
          ]
        }
      ]
    }
  ]
}
""";

        static string WriteVpr(string json) {
            string path = Path.Combine(Path.GetTempPath(), $"sekai-vpr-{Guid.NewGuid():N}.vpr");
            using var stream = File.Create(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("Project/sequence.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(json);
            return path;
        }

        [Fact]
        public void LoadsNotesTempoAndTrackName() {
            string path = WriteVpr(Sequence);
            try {
                var project = VPR.Load(path);

                Assert.Single(project.tracks);
                Assert.Equal("Miku", project.tracks[0].TrackName);
                Assert.Equal(2, project.tempos.Count);
                Assert.Equal(120, project.tempos[0].bpm);
                Assert.Equal(90, project.tempos[1].bpm);
                Assert.Equal(1920, project.tempos[1].position);
                Assert.Equal(4, project.timeSignatures[0].beatPerBar);

                var part = Assert.IsType<UVoicePart>(project.parts.Single());
                Assert.Equal(1920, part.position);
                var notes = part.notes.OrderBy(note => note.position).ToList();
                Assert.Equal(2, notes.Count);
                // Part-relative note positions stay relative to the part, which carries the 1920 start.
                Assert.Equal(0, notes[0].position);
                Assert.Equal(480, notes[0].duration);
                Assert.Equal(60, notes[0].tone);
                Assert.Equal("ら", notes[0].lyric);
                Assert.Equal(480, notes[1].position);
                Assert.Equal("+~", notes[1].lyric);
                Assert.Equal(3840, part.Duration);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void DetectedAsProjectFormat() {
            string path = WriteVpr(Sequence);
            try {
                Assert.Equal(ProjectFormats.Vpr, Formats.DetectProjectFormat(path));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void RejectsArchiveWithoutSequence() {
            string path = Path.Combine(Path.GetTempPath(), $"sekai-vpr-{Guid.NewGuid():N}.vpr");
            try {
                using (var stream = File.Create(path)) {
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                    zip.CreateEntry("Project/readme.txt");
                }
                Assert.Throws<FileFormatException>(() => VPR.Load(path));
            } finally {
                File.Delete(path);
            }
        }
    }
}
