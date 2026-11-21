using SekaiUtau.Core.Ustx;
using Serilog;

namespace SekaiUtau.Classic {
    public interface IResampler {
        string FilePath { get; }
        bool NoWrapperScript { get; }
        float[] DoResampler(ResamplerItem args, ILogger logger);
        string DoResamplerReturnsFile(ResamplerItem args, ILogger logger);
        void CheckPermissions();
        ResamplerManifest Manifest {  get; }
        bool SupportsFlag(string abbr);
    }
}
