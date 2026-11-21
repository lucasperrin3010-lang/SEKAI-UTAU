using System.Threading.Tasks;

namespace SekaiUtau.Classic {
    public interface IPlugin {
        string Encoding { get; }
        Task Run(string tempFile);
    }
}
