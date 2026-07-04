using System.Text;
using System.Text.RegularExpressions;

namespace LocalDictation.Evals;

/// <summary>
/// Computes Word Error Rate (WER) between a reference and hypothesis transcript using
/// word-level Levenshtein edit distance: WER = (substitutions + deletions + insertions) / reference words.
/// </summary>
/// <remarks>
/// Text is normalised (lower-cased, punctuation stripped, whitespace collapsed) before scoring
/// so cosmetic differences don't inflate the score. Used by the ASR accuracy eval.
/// </remarks>
public static class WerCalculator
{
    /// <summary>Returns WER in 0..1 (0 = perfect). Values above 1 are possible with many insertions.</summary>
    public static double Compute(string reference, string hypothesis)
    {
        var refWords = Normalize(reference);
        var hypWords = Normalize(hypothesis);
        if (refWords.Length == 0) return hypWords.Length == 0 ? 0 : 1;

        int[,] d = new int[refWords.Length + 1, hypWords.Length + 1];
        for (int i = 0; i <= refWords.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= hypWords.Length; j++) d[0, j] = j;

        for (int i = 1; i <= refWords.Length; i++)
            for (int j = 1; j <= hypWords.Length; j++)
            {
                int cost = refWords[i - 1] == hypWords[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }

        return (double)d[refWords.Length, hypWords.Length] / refWords.Length;
    }

    /// <summary>Lower-cases, removes punctuation and splits into words.</summary>
    private static string[] Normalize(string text)
    {
        var cleaned = Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}\s']", " ");
        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
