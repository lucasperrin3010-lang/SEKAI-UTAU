using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Xunit;

using SekaiUtau.Core.Ustx;

namespace SekaiUtau.Core.Format {
    public class MxlTest {
        readonly string basePath;

        public MxlTest() {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            basePath = Path.Join(dir, "Files", "MusicXML");
        }

        static void WriteEntry(ZipArchive archive, string name, string contents) {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(contents);
        }

        [Fact]
        public void LoadsScoreFromFirstXml() {
            string mxl = Path.Combine(Path.GetTempPath(), $"seka-mxl-test-{Guid.NewGuid():N}.mxl");
            try {
                using (var archive = ZipFile.Open(mxl, ZipArchiveMode.Create)) {
                    WriteEntry(archive, "score.musicxml",
                        File.ReadAllText(Path.Join(basePath, "01a-Pitches-Pitches.musicxml")));
                }
                var project = Mxl.LoadProject(mxl);
                var part = Assert.IsType<UVoicePart>(project.parts.First());
                Assert.NotEmpty(part.notes);
                Assert.Equal(string.Empty, project.FilePath);
            } finally {
                if (File.Exists(mxl)) {
                    File.Delete(mxl);
                }
            }
        }

        [Fact]
        public void LoadsScoreNamedByContainer() {
            string mxl = Path.Combine(Path.GetTempPath(), $"seka-mxl-container-{Guid.NewGuid():N}.mxl");
            try {
                using (var archive = ZipFile.Open(mxl, ZipArchiveMode.Create)) {
                    WriteEntry(archive, "META-INF/container.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<container><rootfiles><rootfile full-path=\"scores/song.musicxml\" " +
                        "media-type=\"application/vnd.recordare.musicxml+xml\"/></rootfiles></container>");
                    WriteEntry(archive, "scores/song.musicxml",
                        File.ReadAllText(Path.Join(basePath, "61a-Lyrics.musicxml")));
                }
                var project = Mxl.LoadProject(mxl);
                var part = Assert.IsType<UVoicePart>(project.parts.First());
                Assert.NotEmpty(part.notes);
            } finally {
                if (File.Exists(mxl)) {
                    File.Delete(mxl);
                }
            }
        }
    }
}
