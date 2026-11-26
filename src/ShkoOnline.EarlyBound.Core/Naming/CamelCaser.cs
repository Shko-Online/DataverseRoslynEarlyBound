// The word-splitting algorithm in this file is a port of CamelCaser from
// DLaB.Xrm.XrmToolBoxTools (the Early Bound Generator), Copyright (c) 2015 Daryl LaBar,
// MIT License. It is kept behaviourally identical on purpose: the names it produces are
// what makes generated code a drop-in replacement. See THIRD-PARTY-NOTICES.md.

using System.Text;
using System.Text.RegularExpressions;

namespace ShkoOnline.EarlyBound.Naming;

/// <summary>
/// Turns a lower-case identifier such as <c>new_duedate</c> into <c>New_DueDate</c> by
/// finding English words in it.
/// </summary>
/// <remarks>
/// Instances keep per-run caches and are not thread-safe; create one per model build.
/// The word list itself is loaded once and shared.
/// </remarks>
public sealed class CamelCaser
{
    private static readonly string[] DefaultPreferredEndings = { "Guid", "Id" };
    private static readonly Regex WordRegex = new("([A-Z][a-z]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, HashSet<string>> PreferredWords = new(StringComparer.Ordinal)
    {
        { "For", new HashSet<string>(StringComparer.Ordinal) { "Fort" } },
        { "In", new HashSet<string>(StringComparer.Ordinal) { "Tin", "Bin", "Sin" } },
        { "Team", new HashSet<string>(StringComparer.Ordinal) { "Steam" } },
        { "Sent", new HashSet<string>(StringComparer.Ordinal) { "Ent" } },
        { "Set", new HashSet<string>(StringComparer.Ordinal) { "Et" } },
        { "Side", new HashSet<string>(StringComparer.Ordinal) { "Ide" } },
    };

    private static readonly Lazy<WordList> SharedWords = new(WordList.LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly WordList _words;
    private readonly Dictionary<int, HashSet<string>> _customWords = new();
    private readonly List<string> _overrides;
    private readonly int _maxWordLength;
    private readonly Dictionary<string, string> _alreadyCasedBackward = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _alreadyCasedForward = new(StringComparer.Ordinal);

    /// <summary>Creates a caser over the built-in word list.</summary>
    /// <param name="customWords">Extra words to recognise, e.g. a publisher prefix.</param>
    /// <param name="tokenCapitalizationOverrides">Tokens that are always written exactly as given.</param>
    public CamelCaser(IEnumerable<string>? customWords = null, IEnumerable<string>? tokenCapitalizationOverrides = null)
        : this(SharedWords.Value, customWords, tokenCapitalizationOverrides)
    {
    }

    internal CamelCaser(WordList words, IEnumerable<string>? customWords, IEnumerable<string>? tokenCapitalizationOverrides)
    {
        _words = words;
        var max = words.MaxLength;
        if (customWords != null)
        {
            foreach (var raw in customWords)
            {
                var word = raw?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(word))
                {
                    continue;
                }

                if (!_customWords.TryGetValue(word!.Length, out var set))
                {
                    _customWords[word.Length] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(word);
                max = Math.Max(max, word.Length);
            }
        }

        _maxWordLength = max;
        _overrides = (tokenCapitalizationOverrides ?? Enumerable.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .OrderByDescending(t => t.Length)
            .ToList();
    }

    /// <summary>PascalCases <paramref name="value"/>, preferring to keep the given endings intact.</summary>
    public string Case(string value, params string[] preferredEndings)
    {
        value = value.ToLowerInvariant();
        if (preferredEndings == null || preferredEndings.Length == 0)
        {
            preferredEndings = DefaultPreferredEndings;
        }

        var backward = CaseWord(value, preferredEndings, parseForward: false);
        var forward = CaseWord(value, preferredEndings, parseForward: true);
        return ChooseBest(backward, forward);
    }

    private string CaseWord(string value, string[] preferredEndings, bool parseForward)
    {
        foreach (var ending in preferredEndings)
        {
            if (value.EndsWith(ending.ToLowerInvariant(), StringComparison.Ordinal))
            {
                var tmp = CaseInternal(value.Substring(0, value.Length - ending.Length), parseForward);
                value = CaseInternal(value, parseForward);
                return CountUpper(tmp) < CountUpper(value)
                    ? tmp + ending
                    : value;
            }
        }

        return CaseInternal(value, parseForward);
    }

    private static string ChooseBest(string option1, string option2)
    {
        var count1 = CountUpper(option1);
        var count2 = CountUpper(option2);

        if (count1 == count2)
        {
            return ParseForLongerWords(option1, option2);
        }

        return count1 < count2 ? option1 : option2;
    }

    private static string ParseForLongerWords(string option1, string option2)
    {
        var words1 = WordRegex.Matches(option1).Cast<Match>().Select(m => m.Value).ToList();
        var words2 = WordRegex.Matches(option2).Cast<Match>().Select(m => m.Value).ToList();

        // More words means fewer single letters left over.
        if (words1.Count > words2.Count)
        {
            return option1;
        }

        for (var i = 0; i < words1.Count; i++)
        {
            if (PreferredWords.TryGetValue(words1[i], out var lessPreferred2) && lessPreferred2.Contains(words2[i]))
            {
                return option1;
            }

            if (PreferredWords.TryGetValue(words2[i], out var lessPreferred1) && lessPreferred1.Contains(words1[i]))
            {
                return option2;
            }
        }

        var allLengths1 = words1.GroupBy(x => x.Length).OrderByDescending(x => x.Key).ToList();
        var allLengths2 = words2.GroupBy(x => x.Length).OrderByDescending(x => x.Key).ToList();

        for (var i = 0; i < allLengths1.Count && i < allLengths2.Count; i++)
        {
            if (allLengths1[i].Key > allLengths2[i].Key)
            {
                return option1;
            }

            if (allLengths1[i].Key < allLengths2[i].Key)
            {
                return option2;
            }

            if (allLengths1[i].Count() > allLengths2[i].Count())
            {
                return option1;
            }

            if (allLengths2[i].Count() > allLengths1[i].Count())
            {
                return option2;
            }
        }

        return option1;
    }

    private string CaseInternal(string value, bool parseForward)
    {
        foreach (var token in _overrides)
        {
            var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index == 0
                    ? string.Empty
                    : CaseInternal(value.Substring(0, index), parseForward);
                var end = index + token.Length == value.Length
                    ? string.Empty
                    : CaseInternal(value.Substring(index + token.Length), parseForward);
                return start + token + end;
            }
        }

        if (value.IndexOf('_') >= 0)
        {
            var parts = value.Split('_');
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = CasePart(parts[i], parseForward);
            }

            return string.Join("_", parts);
        }

        return CasePart(value, parseForward);
    }

    private string CasePart(string part, bool parseForward)
    {
        var cache = parseForward ? _alreadyCasedForward : _alreadyCasedBackward;
        if (cache.TryGetValue(part, out var cached))
        {
            return cached;
        }

        var result = CasePartForCache(part, parseForward);
        cache[part] = result;
        return result;
    }

    private string CasePartForCache(string part, bool parseForward)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return string.Empty;
        }

        if (part.Length == 1)
        {
            return Capitalize(part);
        }

        // Try the longest word first; fall back to shorter ones when what remains is not made of words.
        string? fallbackWord = null;
        string? fallbackRemaining = null;
        var fallbackIndex = int.MaxValue;
        var currentLength = part.Length > _maxWordLength ? _maxWordLength : part.Length;
        for (var length = currentLength; length >= 1; length--)
        {
            if (!HasWordsOfLength(length))
            {
                continue;
            }

            var word = parseForward
                ? part.Substring(0, length)
                : part.Substring(part.Length - length);
            if (!IsWord(word))
            {
                continue;
            }

            var remaining = CaseRemaining(part, word, parseForward);
            var index = FirstNonWordPartIndex(remaining);
            if (index >= 0)
            {
                if (fallbackWord == null || index < fallbackIndex)
                {
                    fallbackIndex = index;
                    fallbackRemaining = remaining;
                    fallbackWord = word;
                }

                continue;
            }

            return parseForward
                ? Capitalize(word) + remaining
                : remaining + Capitalize(word);
        }

        if (fallbackWord != null)
        {
            return parseForward
                ? Capitalize(fallbackWord) + fallbackRemaining
                : fallbackRemaining + Capitalize(fallbackWord);
        }

        if (parseForward)
        {
            return char.ToUpperInvariant(part[0]) + CasePart(part.Substring(1), parseForward);
        }

        var nonWordIndex = part.Length - 1;
        return CasePart(part.Substring(0, nonWordIndex), parseForward) + char.ToUpperInvariant(part[nonWordIndex]);
    }

    private bool HasWordsOfLength(int length) =>
        _words.HasLength(length) || _customWords.ContainsKey(length);

    private bool IsWord(string word) =>
        _words.Contains(word) || (_customWords.TryGetValue(word.Length, out var set) && set.Contains(word));

    private static int FirstNonWordPartIndex(string remaining)
    {
        for (var i = 0; i < remaining.Length - 1; i++)
        {
            if (char.IsUpper(remaining[i]) && char.IsUpper(remaining[i + 1]))
            {
                return i;
            }
        }

        return -1;
    }

    private string CaseRemaining(string whole, string word, bool parseForward)
    {
        if (whole.Length == word.Length)
        {
            return string.Empty;
        }

        var remaining = parseForward
            ? whole.Substring(word.Length)
            : whole.Substring(0, whole.Length - word.Length);
        return remaining.Length < 2
            ? Capitalize(remaining)
            : CasePart(remaining, parseForward);
    }

    private static string Capitalize(string word) =>
        word.Length > 1
            ? char.ToUpperInvariant(word[0]) + word.Substring(1)
            : word.ToUpperInvariant();

    private static int CountUpper(string value)
    {
        var count = 0;
        foreach (var c in value)
        {
            if (char.IsUpper(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The English word list, bucketed by word length.</summary>
    internal sealed class WordList
    {
        private readonly Dictionary<int, HashSet<string>> _byLength;

        public WordList(IEnumerable<string> words)
        {
            _byLength = new Dictionary<int, HashSet<string>>();
            foreach (var raw in words)
            {
                var word = raw.Trim().ToLowerInvariant();
                if (!_byLength.TryGetValue(word.Length, out var set))
                {
                    _byLength[word.Length] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(word);
            }

            MaxLength = _byLength.Count == 0 ? 0 : _byLength.Keys.Max();
        }

        public int MaxLength { get; }

        public bool HasLength(int length) => _byLength.ContainsKey(length);

        public bool Contains(string word) =>
            _byLength.TryGetValue(word.Length, out var set) && set.Contains(word);

        public static WordList LoadEmbedded()
        {
            const string resourceName = "ShkoOnline.EarlyBound.CamelCaseDictionary.txt";
            using var stream = typeof(CamelCaser).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return new WordList(ReadLines(reader));
        }

        private static IEnumerable<string> ReadLines(TextReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                yield return line;
            }
        }
    }
}
