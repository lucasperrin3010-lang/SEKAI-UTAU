using System;
using System.IO;
using SekaiUtau.Core.Recording;
using Xunit;

namespace SekaiUtau.Core {
    public class FfmpegLocatorTest {
        // The friend's error was "ffmpeg not found in PATH": dropping ffmpeg.exe next
        // to the application must be enough now.
        [Fact]
        public void FfmpegBesideTheApplicationIsFound() {
            string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            string exe = Path.Combine(AppContext.BaseDirectory, name);
            bool existed = File.Exists(exe);
            try {
                if (!existed) {
                    File.WriteAllText(exe, "");
                }
                Assert.Equal(exe, WindowRecorder.FindFfmpeg());
            } finally {
                if (!existed && File.Exists(exe)) {
                    File.Delete(exe);
                }
            }
        }

        [Fact]
        public void FfmpegInTheFfmpegSubfolderIsFound() {
            string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            string dir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            string exe = Path.Combine(dir, name);
            bool existed = File.Exists(exe);
            try {
                Directory.CreateDirectory(dir);
                if (!existed) {
                    File.WriteAllText(exe, "");
                }
                Assert.Equal(exe, WindowRecorder.FindFfmpeg());
            } finally {
                if (!existed && File.Exists(exe)) {
                    File.Delete(exe);
                }
            }
        }
    }
}
