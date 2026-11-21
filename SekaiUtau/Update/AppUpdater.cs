using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SekaiUtau.Core;
using SekaiUtau.Core.Util;
using Serilog;

namespace SekaiUtau.App.Update {
    public class UpdateAsset {
        public string Name = string.Empty;
        public string Url = string.Empty;
        public long Size;
    }

    public class UpdateCandidate {
        public Version Version = new Version(0, 0, 0, 0);
        public string Tag = string.Empty;
        public string ReleaseNotes = string.Empty;
        public UpdateAsset? Asset;
    }

    /// <summary>
    /// Checks the project's GitHub releases, compares them with the running
    /// version, and installs the matching package for this platform.
    /// </summary>
    public static class AppUpdater {
        public const string Repo = "lucasperrin3010-lang/SEKAI-UTAU";

        public static string AppDir => PathManager.Inst.RootPath ?? AppContext.BaseDirectory;

        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

        /// <summary>Name of the release asset that matches this machine.</summary>
        public static string PlatformAssetName() {
            if (OS.IsMacOS()) {
                return "SEKAI-UTAU-osx-arm64.zip";
            }
            if (OS.IsLinux()) {
                return "SEKAI-UTAU-linux-x64.tar.gz";
            }
            return "SEKAI-UTAU-win-x64.zip";
        }

        public static bool IsNewer(Version candidate) => Normalize(candidate) > Normalize(CurrentVersion);

        static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

        public static async Task<UpdateCandidate?> CheckAsync() {
            try {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "SekaiUtau");
                client.Timeout = TimeSpan.FromSeconds(30);
                string json = await client.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
                var candidate = new UpdateCandidate {
                    Tag = tag,
                    ReleaseNotes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty,
                };
                string cleaned = tag.TrimStart('v', 'V');
                int suffix = cleaned.IndexOf('-');
                if (suffix > 0) {
                    cleaned = cleaned.Substring(0, suffix);
                }
                Version? parsed = null;
                if (!Version.TryParse(cleaned, out parsed) || parsed == null) {
                    parsed = new Version(0, 0, 0, 0);
                }
                candidate.Version = parsed;
                string wanted = PlatformAssetName();
                if (root.TryGetProperty("assets", out var assets)) {
                    foreach (var asset in assets.EnumerateArray()) {
                        string name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                        if (name != wanted) {
                            continue;
                        }
                        candidate.Asset = new UpdateAsset {
                            Name = name,
                            Url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty,
                            Size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out long size) ? size : 0,
                        };
                        break;
                    }
                }
                Log.Information($"Latest release {tag} ({candidate.Version}), running {CurrentVersion}, asset {(candidate.Asset?.Name ?? "none")}");
                return candidate;
            } catch (Exception e) {
                Log.Error(e, "Update check failed.");
                return null;
            }
        }

        public static async Task<bool> DownloadAndInstallAsync(UpdateCandidate candidate, IProgress<int>? progress) {
            if (candidate.Asset == null || string.IsNullOrEmpty(candidate.Asset.Url)) {
                Log.Error("No downloadable asset for this platform.");
                return false;
            }
            try {
                string work = Path.Combine(Path.GetTempPath(), "sekaiutau-update");
                if (Directory.Exists(work)) {
                    Directory.Delete(work, true);
                }
                Directory.CreateDirectory(work);
                string archive = Path.Combine(work, candidate.Asset.Name);
                await DownloadAsync(candidate.Asset, archive, progress);
                string extracted = Path.Combine(work, "files");
                Directory.CreateDirectory(extracted);
                if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                    ZipFile.ExtractToDirectory(archive, extracted, true);
                } else {
                    await using var fileStream = File.OpenRead(archive);
                    await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
                    System.Formats.Tar.TarFile.ExtractToDirectory(gzip, extracted, true);
                }
                string source = ResolveSource(extracted);
                return LaunchInstaller(source, work);
            } catch (Exception e) {
                Log.Error(e, "Update installation failed.");
                return false;
            }
        }

        static async Task DownloadAsync(UpdateAsset asset, string path, IProgress<int>? progress) {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);
            using var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(path);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0) {
                await output.WriteAsync(buffer.AsMemory(0, read));
                written += read;
                if (total > 0) {
                    progress?.Report((int)(written * 100 / total));
                }
            }
            Log.Information($"Downloaded {written} bytes to {path}");
        }

        /// <summary>Archives wrap their content in a top folder (SEKAI-UTAU, SEKAI UTAU.app).</summary>
        static string ResolveSource(string extracted) {
            string[] directories = Directory.GetDirectories(extracted);
            string[] files = Directory.GetFiles(extracted);
            if (files.Length == 0 && directories.Length == 1) {
                return directories[0];
            }
            return extracted;
        }

        /// <summary>
        /// Starts a detached script that waits for this process to exit, copies
        /// the new files over the installation and restarts the application.
        /// </summary>
        static bool LaunchInstaller(string source, string work) {
            string appDir = AppDir;
            if (OS.IsMacOS() || OS.IsLinux()) {
                string script = Path.Combine(work, "apply-update.sh");
                string command = OS.IsMacOS()
                    ? $"rm -rf \"{appDir}\"; cp -R \"{source}\" \"{appDir}\"; open \"{appDir}\""
                    : $"cp -R \"{source}/.\" \"{appDir}/\"; chmod +x \"{appDir}/SekaiUtau\"; \"{appDir}/SekaiUtau\" >/dev/null 2>&1 &";
                File.WriteAllText(script, $"#!/bin/bash\nsleep 3\n{command}\n");
                var start = new ProcessStartInfo("bash") { UseShellExecute = false };
                start.ArgumentList.Add(script);
                Process.Start(start);
                return true;
            }
            string batch = Path.Combine(work, "apply-update.cmd");
            string exe = Path.Combine(appDir, "SekaiUtau.exe");
            File.WriteAllText(batch,
                "@echo off\r\n" +
                "timeout /t 3 /nobreak >nul\r\n" +
                $"xcopy /E /Y /I \"{source}\\*\" \"{appDir}\\\" >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + batch + "\"") {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            Log.Information($"Installer started from {source} to {appDir}");
            return true;
        }
    }
}
