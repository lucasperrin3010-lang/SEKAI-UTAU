using System.Collections.Generic;
using SekaiUtau.Classic;
using SekaiUtau.Classic.Hifisampler;
using SekaiUtau.Core.Analysis;
using SekaiUtau.Core.Render;
using SekaiUtau.Core.Ustx;

namespace SekaiUtau.Core {
    /// <summary>The registry packages SekaiUtau's engines need, and which the app may install itself.</summary>
    public static class PackageRequirements {
        public static readonly IReadOnlySet<string> Installable = new HashSet<string> {
            Hnsep.PackageId,
            HifiVocoder.PackageId,
            "game",
            "rmvpe",
        };

        /// <summary>The packages a track's renderer (and resampler) needs. hifisampler needs hnsep only for its curves.</summary>
        public static string[] For(URenderSettings settings) => settings.renderer switch {
            Renderers.WORLDLINE_R11 => [Hnsep.PackageId],
            Renderers.WORLDLINE_R2 => [HifiVocoder.PackageId],
            Renderers.CLASSIC when (settings.Resampler?.ToString() ?? settings.resampler) == HifisamplerResampler.name
                => [HifiVocoder.PackageId, Hnsep.PackageId],
            _ => [],
        };
    }
}
