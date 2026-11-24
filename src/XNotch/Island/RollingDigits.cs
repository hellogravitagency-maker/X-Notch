using System;
using System.Collections.Generic;

namespace XNotch.Island;

/// <summary>Pure diff for rolling numbers: which character columns must animate.</summary>
public static class RollingDigits
{
    /// <summary>
    /// Indices (into <paramref name="next"/>) whose character differs from
    /// <paramref name="previous"/>. Strings are right-aligned so "9:59" → "10:00"
    /// compares units with units; columns that did not exist before count as changed.
    /// </summary>
    public static IReadOnlyList<int> ChangedIndices(string? previous, string next)
    {
        previous ??= "";
        var changed = new List<int>();
        int offset = next.Length - previous.Length;
        for (int i = 0; i < next.Length; i++)
        {
            int j = i - offset;
            if (j < 0 || j >= previous.Length || previous[j] != next[i]) changed.Add(i);
        }
        return changed;
    }

    /// <summary>m:ss, or h:mm:ss past an hour (tabular, no leading zero on the first unit).</summary>
    public static string FormatClock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
