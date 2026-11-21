using SekaiUtau.Core.DiffSinger;
using SekaiUtau.Core.Util;
using ReactiveUI.SourceGenerators;

namespace SekaiUtau.App.ViewModels {
    public partial class DsScriptExportViewModel : ViewModelBase {
        [Reactive] public partial bool ExportPitch { get; set; } = true;
        [Reactive] public partial bool ExportVariance { get; set; } = false;
        public bool TensorCacheEnabled => Preferences.Default.DiffSingerTensorCache;

        public DsScriptExportOptions BuildOptions() {
            return new DsScriptExportOptions {
                exportPitch = ExportPitch,
                exportVariance = TensorCacheEnabled && ExportVariance,
            };
        }
    }
}
