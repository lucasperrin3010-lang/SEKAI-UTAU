using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using SekaiUtau.Core.Ustx;
using Serilog;

namespace SekaiUtau.Core.Format;

/// <summary>
/// Compressed MusicXML (.mxl): a zip archive that holds the score plus a
/// META-INF/container.xml pointing at it.
/// </summary>
public static class Mxl {
    public static UProject LoadProject(string mxlPath) {
        using var archive = ZipFile.OpenRead(mxlPath);
        var entry = FindScoreEntry(archive)
            ?? throw new FileFormatException("No MusicXML score found inside the archive.");
        string temp = Path.Combine(Path.GetTempPath(), $"seka-mxl-{Guid.NewGuid():N}.musicxml");
        try {
            entry.ExtractToFile(temp, true);
            var project = MusicXML.LoadProject(temp);
            // The MusicXML import is not the project file itself.
            project.FilePath = string.Empty;
            return project;
        } finally {
            try {
                File.Delete(temp);
            } catch (Exception e) {
                Log.Warning(e, $"Failed to delete temporary file {temp}");
            }
        }
    }

    static ZipArchiveEntry? FindScoreEntry(ZipArchive archive) {
        // container.xml names the root file, so use it when present.
        var container = archive.GetEntry("META-INF/container.xml");
        if (container != null) {
            try {
                using var stream = container.Open();
                var doc = XDocument.Load(stream);
                string? fullPath = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "rootfile")
                    ?.Attribute("full-path")?.Value;
                if (!string.IsNullOrEmpty(fullPath)) {
                    var found = archive.GetEntry(fullPath.Replace('\\', '/'));
                    if (found != null) {
                        return found;
                    }
                }
            } catch (Exception e) {
                Log.Warning(e, "Failed to read the MXL container, falling back to the first score");
            }
        }
        return archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }
}
