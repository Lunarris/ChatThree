using System.Reflection;
using WeCantSpell.Hunspell;

namespace ChatThree.Util;

public class SpellChecker
{
    private const string DictionaryResource = "ChatThree.Dictionaries.en_US.dic";
    private const string AffixResource = "ChatThree.Dictionaries.en_US.aff";
    private const int CacheLimit = 5_000;

    // Chat words and game terms the dictionary doesn't know about
    private static readonly HashSet<string> GameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "gg", "ty", "tyvm", "tysm", "thx", "np", "omw", "brb", "afk", "lol", "lmao", "rofl", "pls", "plz", "idk", "imo",
        "tbh", "btw", "ofc", "nvm", "ngl", "irl", "ok", "lfg", "lfm", "dps", "aoe", "ilvl", "glam", "glams", "mog", "mogs",
        "msq", "fc", "ls", "cwls", "pf", "df", "pld", "drk", "gnb", "whm", "sch", "ast", "sge", "mnk", "drg", "nin", "rpr",
        "vpr", "brd", "mch", "dnc", "blm", "smn", "rdm", "pct", "blu", "eorzea", "hydaelyn", "limsa", "gridania", "uldah",
        "ishgard", "kugane", "crystarium", "sharlayan", "tuliyollal", "moogle", "moogles", "chocobo", "chocobos", "gil",
        "aetheryte", "aetherytes", "savage", "unreal", "mentor", "sprout", "sprouts", "tank", "tanks", "healer", "healers",
        "hmu", "hbu", "ttyl", "gnight",
    };

    private readonly Dictionary<string, bool> Cache = [];
    private readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
    private WordList? Dictionary;
    private Task? Loading;

    public bool IsReady => Dictionary != null;

    public void EnsureLoaded()
    {
        Loading ??= Task.Run(Load);
    }

    private void Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var dictionary = assembly.GetManifestResourceStream(DictionaryResource)!;
            using var affix = assembly.GetManifestResourceStream(AffixResource)!;
            Dictionary = WordList.CreateFromStreams(dictionary, affix);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load the spell check dictionary");
        }
    }

    // Finds the part of a whitespace separated token that should be checked, without surrounding punctuation
    public static bool TryGetWord(string token, out int start, out int length)
    {
        start = 0;
        var end = token.Length;
        while (start < end && !char.IsLetter(token[start]))
            start++;
        while (end > start && !char.IsLetter(token[end - 1]))
            end--;

        length = end - start;
        if (length < 2)
            return false;

        var hasLower = false;
        for (var i = start; i < end; i++)
        {
            var c = token[i];
            if (char.IsLower(c))
                hasLower = true;
            else if (!char.IsLetter(c) && c is not '\'' and not '’' and not '-')
                return false;
        }

        // All caps words are usually abbreviations like DPS or LFM
        return hasLower;
    }

    public bool IsMisspelled(string word)
    {
        var dictionary = Dictionary;
        if (dictionary == null)
            return false;

        if (!Cache.TryGetValue(word, out var misspelled))
        {
            // Every half-typed word ends up in here, so start over once it gets big
            if (Cache.Count >= CacheLimit)
                Cache.Clear();

            misspelled = !GameWords.Contains(word) && !dictionary.Check(word);
            Cache[word] = misspelled;
        }

        return misspelled && !IsName(word) && !Plugin.Config.SpellCheckWords.Contains(word.ToLowerInvariant());
    }

    // Player names change as people join the party or send tells, so they're checked live instead of cached
    public void SetNames(IEnumerable<string> names)
    {
        Names.Clear();
        foreach (var name in names)
            foreach (var part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                Names.Add(part);
    }

    private bool IsName(string word)
    {
        if (Names.Contains(word))
            return true;

        return word.Length > 2 && word[^2] is '\'' or '’' && word[^1] is 's' or 'S' && Names.Contains(word[..^2]);
    }

    public Task<List<string>> SuggestAsync(string word)
    {
        var dictionary = Dictionary;
        if (dictionary == null)
            return Task.FromResult(new List<string>());

        return Task.Run(() => dictionary.Suggest(word).Take(8).ToList());
    }
}
