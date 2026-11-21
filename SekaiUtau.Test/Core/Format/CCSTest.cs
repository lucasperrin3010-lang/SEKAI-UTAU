using System;
using System.IO;
using System.Linq;
using Xunit;

using SekaiUtau.Core.Ustx;

namespace SekaiUtau.Core.Format {
    public class CCSTest {
        // Layout taken from a documented CeVIO AI project: 960 clocks per quarter note,
        // a whole note on A4 starting at clock 3840, then a quarter note on C5.
        const string Ccs = """
<?xml version="1.0" encoding="utf-8"?>
<Scenario Code="7251BC4B6168E7B2992FA620BD3E1E77">
  <Sequence Id="">
    <Scene Id="">
      <Units>
        <Unit Version="" Id="" Category="SingerSong" Group="c02c094d-2f83-4e7c-a522-5f316b2c0199" StartTime="00:00:00" Duration="" CastId="Sato Sasara" Language="">
          <Song Version="" CommonKeys="true">
            <Tempo><Sound Clock="0" Tempo="120" /></Tempo>
            <Beat><Time Clock="0" Beats="4" BeatType="4" /></Beat>
            <Score>
              <Note Clock="3840" PitchStep="9" PitchOctave="4" Duration="3840" Lyric="ラ" />
              <Note Clock="7680" PitchStep="0" PitchOctave="5" Duration="960" Lyric="la" />
            </Score>
          </Song>
        </Unit>
      </Units>
    </Scene>
  </Sequence>
</Scenario>
""";

        [Fact]
        public void LoadsNotesTempoAndMeter() {
            string path = Path.Combine(Path.GetTempPath(), $"seka-ccs-{Guid.NewGuid():N}.ccs");
            try {
                File.WriteAllText(path, Ccs);
                var project = CCS.LoadProject(path);

                Assert.Single(project.tracks);
                Assert.Equal("Sato Sasara", project.tracks[0].TrackName);
                Assert.Equal(120, project.tempos[0].bpm);
                Assert.Equal(4, project.timeSignatures[0].beatPerBar);
                Assert.Equal(4, project.timeSignatures[0].beatUnit);

                var part = Assert.IsType<UVoicePart>(project.parts.Single());
                var notes = part.notes.ToList();
                Assert.Equal(2, notes.Count);

                // Clock 3840 at 960 clocks per quarter = 4 quarters = 1920 ticks at 480 PPQ.
                var first = notes[0];
                Assert.Equal(1920, first.position);
                Assert.Equal(1920, first.duration);
                Assert.Equal(69, first.tone); // A4
                Assert.Equal("ラ", first.lyric);

                var second = notes[1];
                Assert.Equal(3840, second.position);
                Assert.Equal(480, second.duration);
                Assert.Equal(72, second.tone); // C5
                Assert.Equal("la", second.lyric);

                Assert.Equal(4320, part.Duration);
            } finally {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
        }

        [Theory]
        [InlineData(".ccs")]
        [InlineData(".ccst")]
        public void DetectedAsProjectFormat(string extension) {
            // CeVIO AI saves whole songs as .ccs and single tracks as .ccst; both are the same XML.
            string path = Path.Combine(Path.GetTempPath(), $"seka-ccs-{Guid.NewGuid():N}{extension}");
            try {
                File.WriteAllText(path, Ccs);
                Assert.Equal(ProjectFormats.Ccs, Formats.DetectProjectFormat(path));
                var project = CCS.LoadProject(path);
                Assert.Single(project.tracks);
            } finally {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
        }
    }
}
