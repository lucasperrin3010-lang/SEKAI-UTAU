using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.Core.Recording {
    /// <summary>
    /// Records only this app's window (not the desktop, not other applications) and muxes the
    /// app's own audio into the result. Requires ffmpeg on PATH.
    /// </summary>
    public class WindowRecorder : IDisposable {
        private Process? process;
        private string? videoPath;

        public bool IsRecording => process != null && !process.HasExited;

        public string? VideoPath => videoPath;

        /// <summary>Last message ffmpeg printed, useful when the capture cannot start.</summary>
        public string? LastError { get; private set; }

        /// <summary>Moment the capture started, used to align the audio track.</summary>
        public DateTime? StartedUtc { get; private set; }

        /// <summary>Moment ffmpeg encoded its first frame: the real start of the video timeline.</summary>
        public DateTime? FirstFrameUtc { get; private set; }

        public static string? FindFfmpeg() {
            string name = OS.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            foreach (string candidate in LocalCandidates(name)) {
                if (File.Exists(candidate)) {
                    Log.Information($"Using ffmpeg found at {candidate}");
                    return candidate;
                }
            }
            string? pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar)) {
                return null;
            }
            foreach (string dir in pathVar.Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(dir)) {
                    continue;
                }
                try {
                    string candidate = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                } catch (ArgumentException) {
                }
            }
            return null;
        }

        // Users rarely have ffmpeg on PATH, so look next to the application too: dropping
        // ffmpeg.exe beside SekaiUtau.exe is enough to make video recording work.
        static IEnumerable<string> LocalCandidates(string name) {
            var dirs = new List<string>();
            void Add(string? dir) {
                if (!string.IsNullOrWhiteSpace(dir) && !dirs.Contains(dir!)) {
                    dirs.Add(dir!);
                }
            }
            string baseDir = AppContext.BaseDirectory;
            Add(baseDir);
            Add(Path.Combine(baseDir, "ffmpeg"));
            Add(Path.Combine(baseDir, "ffmpeg", "bin"));
            Add(Path.Combine(baseDir, "Tools"));
            Add(Path.Combine(baseDir, "tools"));
            try {
                Add(PathManager.Inst.DataPath);
                Add(Path.Combine(PathManager.Inst.DataPath, "ffmpeg"));
                Add(Path.Combine(PathManager.Inst.DataPath, "Tools"));
            } catch (Exception) {
                // PathManager may not be initialised yet; the base directory still counts.
            }
            string? configured = Environment.GetEnvironmentVariable("SEKAI_FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(configured)) {
                if (Directory.Exists(configured)) {
                    Add(configured);
                    Add(Path.Combine(configured, "bin"));
                } else {
                    yield return configured!;
                }
            }
            foreach (string dir in dirs) {
                yield return Path.Combine(dir, name);
            }
        }

        /// <summary>
        /// Starts capturing exactly the screen rectangle of our window (in physical pixels).
        /// A window handle cannot be used here: the UI is composited by the GPU, so reading the
        /// window DC yields a black frame; the screen region shows the same window, never the desktop around it.
        /// </summary>
        public bool Start(int x, int y, int width, int height, string outputPath) {
            string? ffmpeg = FindFfmpeg();
            if (ffmpeg == null) {
                Log.Error("ffmpeg was not found in PATH; window recording is unavailable.");
                return false;
            }
            if (width <= 0 || height <= 0) {
                Log.Error($"Invalid capture size {width}x{height}.");
                return false;
            }
            try {
                string? dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }
                // Only the window's own rectangle is captured, so other applications are out of frame.
                // libx264 + yuv420p need even dimensions; the window height is often odd (e.g. 1009).
                FirstFrameUtc = null;
                string args = $"-y -hide_banner -loglevel error -progress pipe:1 -nostats -f gdigrab -framerate 30 " +
                              $"-offset_x {x} -offset_y {y} -video_size {width}x{height} -i desktop " +
                              "-vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" " +
                              $"-c:v libx264 -preset veryfast -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";
                var psi = new ProcessStartInfo(ffmpeg, args) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                process = Process.Start(psi);
                if (process == null) {
                    return false;
                }
                StartedUtc = DateTime.UtcNow;
                process.ErrorDataReceived += (_, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        LastError = e.Data;
                        Log.Warning($"ffmpeg: {e.Data}");
                    }
                };
                process.BeginErrorReadLine();
                process.OutputDataReceived += (_, e) => {
                    if (FirstFrameUtc != null || string.IsNullOrEmpty(e.Data)) {
                        return;
                    }
                    // ffmpeg prints "frame=1" once the first frame is encoded: that instant is the
                    // true start of the video timeline, so the audio can be aligned on it.
                    if (e.Data.StartsWith("frame=") &&
                        int.TryParse(e.Data.Substring(6).Trim(), out int frame) && frame > 0) {
                        FirstFrameUtc = DateTime.UtcNow;
                        Log.Information($"First captured frame at {FirstFrameUtc:HH:mm:ss.fff}");
                    }
                };
                process.BeginOutputReadLine();
                videoPath = outputPath;
                LastError = null;
                // Give ffmpeg a moment: if the handle cannot be captured it exits almost immediately.
                if (process.WaitForExit(1200)) {
                    if (process.ExitCode != 0) {
                        Log.Error($"ffmpeg exited immediately ({process.ExitCode}): {LastError}");
                        process.Dispose();
                        process = null;
                        videoPath = null;
                        try {
                            if (File.Exists(outputPath)) {
                                File.Delete(outputPath);
                            }
                        } catch (Exception) {
                        }
                        return false;
                    }
                }
                Log.Information($"Window recording started: {outputPath}");
                return true;
            } catch (Exception e) {
                Log.Error(e, "Failed to start window recording");
                process = null;
                videoPath = null;
                return false;
            }
        }

        /// <summary>Asks ffmpeg to quit, so the MP4 header is written properly.</summary>
        public void Stop() {
            Process? p = process;
            process = null;
            if (p == null) {
                return;
            }
            try {
                if (!p.HasExited) {
                    p.StandardInput.WriteLine("q");
                    p.StandardInput.Flush();
                    if (!p.WaitForExit(5000)) {
                        p.Kill(true);
                    }
                }
                Log.Information($"Window recording stopped: {videoPath}");
            } catch (Exception e) {
                Log.Warning(e, "Failed to stop window recording cleanly");
            } finally {
                p.Dispose();
            }
        }

        /// <summary>Adds the recorded app audio to the silent window video.</summary>
        public static string? MuxAudio(string videoPath, string? audioPath, string outputPath, double audioOffsetSeconds = 0) {
            string? ffmpeg = FindFfmpeg();
            if (ffmpeg == null) {
                return null;
            }
            bool hasAudio = !string.IsNullOrEmpty(audioPath) && File.Exists(audioPath);
            // -itsoffset delays the audio so it lines up with the frame where playback actually began.
            string offsetArg = audioOffsetSeconds > 0.02
                ? $"-itsoffset {audioOffsetSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} "
                : string.Empty;
            string args = hasAudio
                ? $"-y -hide_banner -loglevel error -i \"{videoPath}\" {offsetArg}-i \"{audioPath}\" -c:v copy -c:a aac -b:a 192k \"{outputPath}\""
                : $"-y -hide_banner -loglevel error -i \"{videoPath}\" -c copy \"{outputPath}\"";
            try {
                var psi = new ProcessStartInfo(ffmpeg, args) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                using Process? p = Process.Start(psi);
                if (p == null) {
                    return null;
                }
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                if (p.ExitCode != 0) {
                    Log.Warning($"ffmpeg mux failed: {err}");
                    return null;
                }
                return outputPath;
            } catch (Exception e) {
                Log.Error(e, "Failed to mux recording audio");
                return null;
            }
        }

        public void Dispose() {
            Stop();
        }
    }
}
