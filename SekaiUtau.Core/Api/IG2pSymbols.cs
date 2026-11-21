using static SekaiUtau.Api.Phonemizer;
using static SekaiUtau.Api.Phonemizer.Note;

namespace SekaiUtau.Api {
    public interface IG2pSymbols {
         string[] GetSymbols(Note note);
    }

}
