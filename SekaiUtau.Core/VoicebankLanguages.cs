using System;
using System.Collections.Generic;
using System.Linq;

namespace SekaiUtau.Core {
    /// <summary>
    /// One recordable language: the vowels and consonants the voicebank has to contain, and
    /// the phonemizer of the application that will read those aliases. The syllable lists are
    /// built from them: every vowel on its own, then every consonant followed by every vowel
    /// (a CV voicebank).
    /// </summary>
    public sealed record VoicebankLanguage(
        string Id,
        string Name,
        string Phonemizer,
        IReadOnlyList<RecordingSyllable> Essentials,
        IReadOnlyList<RecordingSyllable> Full);

    /// <summary>
    /// The languages a SEKAI UTAU voicebank can be recorded in. Each one is a CV script written
    /// with the symbols its phonemizer expects, so a bank recorded with one of these lists is
    /// understood by that phonemizer right away (the oto editor then refines the cut points).
    ///
    /// These are CV banks: consonant + vowel, plus the vowels on their own. VC and VCV banks
    /// (needed by the CVVC phonemizers for the transitions between notes) are a much bigger
    /// recording and are not generated here.
    /// </summary>
    public static class VoicebankLanguages {
        public static VoicebankLanguage Japanese { get; } = new(
            "ja", "Japanese", "Japanese CV (romaji + kana)",
            VoicebankRecording.Basic,
            VoicebankRecording.Full);

        public static IReadOnlyList<VoicebankLanguage> All { get; } = Build();

        public static VoicebankLanguage Default => Japanese;

        public static VoicebankLanguage? Find(string? id) {
            if (string.IsNullOrWhiteSpace(id)) {
                return null;
            }
            return All.FirstOrDefault(language => string.Equals(language.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<RecordingSyllable> Script(VoicebankLanguage? language, bool full) {
            language ??= Default;
            return full ? language.Full : language.Essentials;
        }

        /// <summary>Directory name safe for the voicebank folder: "Français" is fine, "a/b" is not.</summary>
        public static string FileName(RecordingSyllable syllable) => VoicebankRecording.FileName(syllable);

        static IReadOnlyList<VoicebankLanguage> Build() => new List<VoicebankLanguage> {
            Japanese,

            // Arpasing symbols: the English phonemizers read these aliases.
            Cv("en", "English", "Arpasing",
                "aa ae ah ao aw ay eh er ey ih iy ow oy uh uw",
                "aa ae ah eh iy ow uh uw",
                "b ch d dh f g hh jh k l m n ng p r s sh t th v w y z zh",
                "b d k m n p s t"),

            // French VCCV symbols (X-SAMPA), the usual convention of French UTAU banks.
            Cv("fr", "French", "French VCCV (X-SAMPA)",
                "a e i o u y 2 9 E O @ A U 0",
                "a e i o u y 2 9 E O @ A",
                "b d f g Z k l m n p R s S t v w j z J",
                "b d f g k l m n"),

            Cv("es", "Spanish", "Spanish Syllable-Based",
                "a e i o u",
                "b ch d f g k l ll m n nh p r rr s t w y z",
                "b d k l m n p s t"),

            Cv("it", "Italian", "Italian Syllable-Based",
                "a e i o u 3 0",
                "a e i o u",
                "b d dz dZ f g gn j k l m M n N p r rr s S t ts tS v w y z",
                "b d f g k l m n p r"),

            Cv("de", "German", "German VCCV (X-SAMPA)",
                "a 6 e E 2 9 i I y Y u U o O @ aI aU OY",
                "a e i I o u U E O @ aI aU",
                "b d f g h j k l m n N p r s S t ts tS v x z Z pf dZ",
                "b d f g k l m n"),

            Cv("pt", "Portuguese (Brazil)", "Brazilian Portuguese CVC",
                "a e i o u an en in on un",
                "a e i o u",
                "b ch d dj f g h k l lh m n nh p r rr s sh t v w y z",
                "b ch d f g k l m n p"),

            Cv("ru", "Russian", "Russian CVC",
                "a e i o u y",
                "b b' v v' g g' d d' z z' k k' l l' m m' n n' p p' r r' s s' t t' f f' h h' sh sh' zh j ts ch",
                "b v g d z k l m n s"),

            Cv("ko", "Korean", "Korean CV",
                "a e i o u eu eo",
                "g n d r l m b s j ch k t p h",
                "g n d m b s j k"),

            // Mandarin pinyin: initials and finals concatenate into the syllabary.
            Cv("zh", "Chinese (Mandarin)", "Chinese CVVC (pinyin)",
                "a o e i u v er ai ei ao ou an en ang eng ong ia ie iao iou ian in iang ing ua uo uai ui uan un uang",
                "a o e i u ai ei ao ou an en ang",
                "b p m f d t n l g k h j q x zh ch sh r z c s",
                "b p m d n l"),

            // Jyutping: same idea for Cantonese.
            Cv("yue", "Cantonese", "Cantonese CVVC (jyutping)",
                "a aa e i o u oe yu ai aai au aau ei iu oi ou ui am an ang ap at ak im in ing ip it ik ong ung",
                "a aa e i o u ai au ei ou ui an ang",
                "b p m f d t n l g k ng h gw kw w z c s j",
                "b d g k l m n s"),

            Cv("vi", "Vietnamese", "Vietnamese CVVC",
                "a A @ i e E o O u U",
                "a A @ i e E o O u U",
                "b ch d g h k kh l m n ng nh ph r s t th tr v x",
                "b ch d h k l m n"),

            Cv("tr", "Turkish", "Turkish CVVC",
                "a e i o u oe ue",
                "b c ch d f g h j k l m n p r s sh t v y z",
                "b d g h k l m n p s t y"),

            Cv("pl", "Polish", "Polish CVC",
                "a A e E i o u y",
                "a e i o u y",
                "b c ch cz d dz f g h j k l m n p r rz s sz t w z",
                "b c d g k l m n"),

            Cv("fil", "Filipino", "Filipino VCV & CVVC",
                "a e i o u ay ey oy aw",
                "b k d g h l m n ng p r s t w y",
                "b d g k l m n p s t"),
        };

        /// <summary>
        /// A CV language: the vowels, then every consonant followed by every vowel. The
        /// essentials list is the same with a shorter set of consonants, for a quick bank.
        /// </summary>
        static VoicebankLanguage Cv(string id, string name, string phonemizer,
            string vowels, string vowelsEssentials, string consonants, string consonantsEssentials) =>
            new(id, name, phonemizer,
                BuildScript(vowels, consonantsEssentials),
                BuildScript(vowels, consonants));

        static VoicebankLanguage Cv(string id, string name, string phonemizer,
            string vowels, string consonants, string consonantsEssentials) =>
            Cv(id, name, phonemizer, vowels, vowels, consonants, consonantsEssentials);

        static IReadOnlyList<RecordingSyllable> BuildScript(string vowels, string consonants) {
            var list = new List<RecordingSyllable>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string vowel in Split(vowels)) {
                if (seen.Add(vowel)) {
                    list.Add(new RecordingSyllable(vowel, string.Empty));
                }
            }
            foreach (string consonant in Split(consonants)) {
                foreach (string vowel in Split(vowels)) {
                    string alias = consonant + vowel;
                    if (seen.Add(alias)) {
                        list.Add(new RecordingSyllable(alias, string.Empty));
                    }
                }
            }
            return list;
        }

        static string[] Split(string symbols) =>
            symbols.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
