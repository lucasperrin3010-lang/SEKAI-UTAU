using System;
using System.IO;
using SekaiUtau.Core.Util;

namespace SekaiUtau.Core.Recording {
    /// <summary>
    /// Folder where the recorded video and its sound are written. The user can
    /// pick another folder in Preferences; an empty preference means the
    /// default Videos folder.
    /// </summary>
    public static class RecordingPaths {
        public static string DefaultDirectory {
            get {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (string.IsNullOrEmpty(baseDir)) {
                    baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                return Path.Combine(baseDir, "Sekai UTAU");
            }
        }

        /// <summary>Configured folder, created when missing.</summary>
        public static string OutputDirectory {
            get {
                string preference = Preferences.Default.RecordingOutputDirectory;
                string dir = string.IsNullOrWhiteSpace(preference) ? DefaultDirectory : preference;
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
