namespace DdsRecolor.Core.Formats;

/// <summary>
/// Suggests near-miss colour names, so a typo is answered with a hint rather than
/// a bare rejection.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="NamedColors"/> so the table stays a pure lookup and
/// the fuzzy-matching cost is only paid on the error path.
/// </remarks>
public static class NameSuggester
{
    /// <summary>
    /// Names within a small edit distance of <paramref name="input"/>, closest
    /// first. Returns nothing when the input is not plausibly a name, so hex
    /// strings and function forms do not attract noise.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string input, int maxResults = 3)
    {
        // Too short to distinguish, or too long to be a keyword.
        if (string.IsNullOrWhiteSpace(input) || input.Length < 4 || input.Length > 24)
        {
            return [];
        }

        // Only alphabetic input can be a plausible misspelling of a keyword.
        foreach (char c in input)
        {
            if (!char.IsLetter(c)) return [];
        }

        // Allow up to a third of the length to differ, so one or two typos in a
        // typical keyword are caught without matching unrelated names.
        int allowed = Math.Max(1, input.Length / 3);

        return [.. NamedColors.AllNames
            .Select(name => (Name: name, Distance: Levenshtein(input, name)))
            .Where(x => x.Distance <= allowed)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(x => x.Name)];
    }

    /// <summary>Standard Levenshtein edit distance, case-insensitive.</summary>
    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1])
                    ? 0
                    : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
