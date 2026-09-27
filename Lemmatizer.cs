using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScreenTranslator;

public static class Lemmatizer
{
    private static readonly Dictionary<string, string> Irregular = new()
    {
        ["told"] = "tell",
        ["said"] = "say",
        ["went"] = "go",
        ["gone"] = "go",
        ["got"] = "get",
        ["took"] = "take",
        ["made"] = "make",
        ["knew"] = "know",
        ["thought"] = "think",
        ["brought"] = "bring",
        ["bought"] = "buy",
        ["caught"] = "catch",
        ["taught"] = "teach",
        ["felt"] = "feel",
        ["kept"] = "keep",
        ["left"] = "leave",
        ["lost"] = "lose",
        ["sent"] = "send",
        ["spent"] = "spend",
        ["built"] = "build",
        ["found"] = "find",
        ["had"] = "have",
        ["did"] = "do",
        ["does"] = "do",
        ["done"] = "do",
        ["was"] = "be",
        ["were"] = "be",
        ["been"] = "be",
        ["is"] = "be",
        ["am"] = "be",
        ["are"] = "be",
        ["ate"] = "eat",
        ["eaten"] = "eat",
        ["drank"] = "drink",
        ["drunk"] = "drink",
        ["drove"] = "drive",
        ["driven"] = "drive",
        ["flew"] = "fly",
        ["flown"] = "fly",
        ["forgot"] = "forget",
        ["forgotten"] = "forget",
        ["gave"] = "give",
        ["given"] = "give",
        ["grew"] = "grow",
        ["grown"] = "grow",
        ["held"] = "hold",
        ["hid"] = "hide",
        ["hung"] = "hang",
        ["hurt"] = "hurt",
        ["laid"] = "lay",
        ["led"] = "lead",
        ["lent"] = "lend",
        ["lit"] = "light",
        ["meant"] = "mean",
        ["met"] = "meet",
        ["paid"] = "pay",
        ["put"] = "put",
        ["read"] = "read",
        ["rode"] = "ride",
        ["rang"] = "ring",
        ["rung"] = "ring",
        ["ran"] = "run",
        ["run"] = "run",
        ["saw"] = "see",
        ["seen"] = "see",
        ["sold"] = "sell",
        ["sought"] = "seek",
        ["shook"] = "shake",
        ["shone"] = "shine",
        ["shot"] = "shoot",
        ["sang"] = "sing",
        ["sung"] = "sing",
        ["sat"] = "sit",
        ["slept"] = "sleep",
        ["spoke"] = "speak",
        ["spoken"] = "speak",
        ["stood"] = "stand",
        ["stole"] = "steal",
        ["stolen"] = "steal",
        ["stuck"] = "stick",
        ["stung"] = "sting",
        ["strove"] = "strive",
        ["swore"] = "swear",
        ["swept"] = "sweep",
        ["swam"] = "swim",
        ["swung"] = "swing",
        ["threw"] = "throw",
        ["thrown"] = "throw",
        ["tore"] = "tear",
        ["torn"] = "tear",
        ["won"] = "win",
        ["wore"] = "wear",
        ["worn"] = "wear",
        ["wove"] = "weave",
        ["wept"] = "weep",
        ["woke"] = "wake",
        ["woken"] = "wake",
        ["wrote"] = "write",
        ["written"] = "write",
        ["bent"] = "bend",
        ["bet"] = "bet",
        ["bit"] = "bite",
        ["bitten"] = "bite",
        ["blew"] = "blow",
        ["blown"] = "blow",
        ["broke"] = "break",
        ["broken"] = "break",
        ["chose"] = "choose",
        ["chosen"] = "choose",
        ["came"] = "come",
        ["became"] = "become",
        ["began"] = "begin",
        ["begun"] = "begin",
        ["ate"] = "eat",
        ["fell"] = "fall",
        ["fallen"] = "fall",
        ["fought"] = "fight",
        ["froze"] = "freeze",
        ["held"] = "hold",
        ["better"] = "good",
        ["best"] = "good",
        ["worse"] = "bad",
        ["worst"] = "bad",
        ["more"] = "many",
        ["most"] = "many",
        ["less"] = "little",
        ["least"] = "little",
        ["men"] = "man",
        ["women"] = "woman",
        ["children"] = "child",
        ["feet"] = "foot",
        ["teeth"] = "tooth",
        ["mice"] = "mouse",
        ["people"] = "person",
        ["geese"] = "goose",
        ["him"] = "he",
        ["her"] = "she",
        ["them"] = "they",
        ["me"] = "I",
        ["us"] = "we",
    };

    private static HashSet<string>? _words;
    private static bool _wordsTried;

    private static HashSet<string>? Words
    {
        get
        {
            if (_wordsTried) return _words;
            _wordsTried = true;
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "words.txt");
                if (File.Exists(path))
                    _words = new HashSet<string>(
                        File.ReadAllLines(path).Select(l => l.Trim().ToLowerInvariant()),
                        StringComparer.Ordinal);
            }
            catch { _words = null; }
            return _words;
        }
    }

    public static string Lemmatize(string w)
    {
        w = w.ToLowerInvariant();
        if (Irregular.TryGetValue(w, out var irr)) return irr;
        if (w.Length <= 3) return w;

        var cands = new List<string>();

        if (w.EndsWith("ies")) cands.Add(w[..^3] + "y");
        if (w.EndsWith("ves")) cands.Add(w[..^3] + "fe");
        if (w.EndsWith("ied")) cands.Add(w[..^3] + "y");
        if (w.EndsWith("sses") || w.EndsWith("shes") || w.EndsWith("ches") ||
            w.EndsWith("xes") || w.EndsWith("zes")) cands.Add(w[..^2]);

        if (w.EndsWith("ing"))
        {
            var s = w[..^3];
            if (s.Length >= 2 && s[^1] == s[^2]) { cands.Add(s[..^1]); cands.Add(s[..^1] + "e"); }
            else { cands.Add(s); cands.Add(s + "e"); }
        }
        if (w.EndsWith("ed") && !w.EndsWith("eed"))
        {
            var s = w[..^2];
            if (s.Length >= 2 && s[^1] == s[^2]) { cands.Add(s[..^1]); cands.Add(s[..^1] + "e"); }
            else { cands.Add(s); cands.Add(s + "e"); }
        }
        if (w.EndsWith("s") && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is"))
            cands.Add(w[..^1]);

        cands.Add(w);

        var words = Words;
        if (words is not null)
        {
            foreach (var c in cands)
                if (words.Contains(c)) return c;
        }

        return cands[0].Length >= 3 ? cands[0] : w;
    }
}