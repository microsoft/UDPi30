using System.Text;

namespace Udpi30.Cli;

/// <summary>
/// Minimal unified-diff generator (LCS-based) — a lightweight stand-in for
/// Python's <c>difflib.unified_diff</c> used by the <c>diff</c> command.
/// </summary>
internal static class DiffUtil
{
    public static IEnumerable<string> UnifiedDiff(string fromText, string toText, string fromFile, string toFile)
    {
        var a = fromText.Replace("\r\n", "\n").Split('\n');
        var b = toText.Replace("\r\n", "\n").Split('\n');

        var lcs = LongestCommonSubsequence(a, b);

        yield return $"--- {fromFile}";
        yield return $"+++ {toFile}";

        int i = 0, j = 0;
        foreach (var (ai, bj) in lcs.Append((a.Length, b.Length)))
        {
            while (i < ai)
            {
                yield return "-" + a[i];
                i++;
            }
            while (j < bj)
            {
                yield return "+" + b[j];
                j++;
            }
            if (ai < a.Length && bj < b.Length)
            {
                yield return " " + a[ai];
                i++;
                j++;
            }
        }
    }

    private static List<(int, int)> LongestCommonSubsequence(string[] a, string[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var result = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add((x, y));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }
        return result;
    }
}
