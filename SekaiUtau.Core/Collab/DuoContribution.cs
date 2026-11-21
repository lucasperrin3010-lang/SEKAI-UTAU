using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SekaiUtau.Core.Format;
using SekaiUtau.Core.Ustx;
using UstxFormat = SekaiUtau.Core.Format.Ustx;
using Serilog;

namespace SekaiUtau.Core.Collab {
    /// <summary>
    /// Duo workflow: each person signs in with a local account, tracks remember their author,
    /// and a contribution is a normal ustx file holding only that person's tracks. The partner
    /// imports it with the usual "import tracks" merge, so two people can build one song by
    /// exchanging a single file (USB stick, cloud folder, chat...).
    /// </summary>
    public static class DuoContribution {
        public static bool IsMine(UTrack track, string author) =>
            !string.IsNullOrEmpty(author) &&
            string.Equals(track.Author, author, StringComparison.OrdinalIgnoreCase);

        public static int CountMine(UProject project, string author) =>
            project.tracks.Count(t => IsMine(t, author));

        /// <summary>Puts every track of the project under the current account.</summary>
        public static int ClaimAll(UProject project) {
            var me = Accounts.CurrentName;
            if (string.IsNullOrEmpty(me)) {
                throw new InvalidOperationException("duo.error.noaccount");
            }
            foreach (var track in project.tracks) {
                track.Author = me;
            }
            project.Saved = false;
            Log.Information("Duo: {Count} tracks assigned to {Name}", project.tracks.Count, me);
            return project.tracks.Count;
        }

        /// <summary>
        /// Writes a copy of the project that only contains the current account's tracks.
        /// Returns how many tracks were exported.
        /// </summary>
        public static int Export(string path) => ExportFor(DocManager.Inst.Project, path, Accounts.CurrentName);

        /// <summary>Same as <see cref="Export"/> for an explicit project and author.</summary>
        public static int ExportFor(UProject project, string path, string me) {
            if (string.IsNullOrEmpty(me)) {
                throw new InvalidOperationException("duo.error.noaccount");
            }
            if (CountMine(project, me) == 0) {
                throw new InvalidOperationException("duo.error.notracks");
            }
            Directory.CreateDirectory(PathManager.Inst.CachePath);
            var cache = Path.Combine(PathManager.Inst.CachePath, $"duo-{Guid.NewGuid():N}.ustx");
            try {
                // Round-trip through the project format: the serializer copies notes, curves,
                // singers and settings for us, so the filtered copy stays complete.
                UstxFormat.Save(cache, project);
                var copy = UstxFormat.Load(cache) ?? throw new InvalidOperationException("duo.error.export");
                var mine = copy.tracks.Where(t => IsMine(t, me)).ToList();
                var trackNumbers = new Dictionary<int, int>();
                var tracks = new List<UTrack>();
                foreach (var track in mine) {
                    trackNumbers[track.TrackNo] = tracks.Count;
                    track.TrackNo = tracks.Count;
                    tracks.Add(track);
                }
                copy.tracks = tracks;
                copy.parts = copy.parts.Where(p => trackNumbers.ContainsKey(p.trackNo)).ToList();
                foreach (var part in copy.parts) {
                    part.trackNo = trackNumbers[part.trackNo];
                }
                copy.name = project.name;
                copy.comment = $"{me}";
                copy.FilePath = path;
                copy.Saved = true;
                UstxFormat.Save(path, copy);
                Log.Information("Duo: exported {Count} tracks of {Name} to {Path}", tracks.Count, me, path);
                return tracks.Count;
            } finally {
                try {
                    File.Delete(cache);
                } catch {
                    // The cache file is disposable; a locked file is not an error.
                }
            }
        }
    }
}
