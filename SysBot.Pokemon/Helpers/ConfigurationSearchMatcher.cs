using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon.Helpers;

internal static class ConfigurationSearchMatcher
{
    public static int Score(string query, string candidate)
    {
        var queryTokens = Tokenize(query);
        var candidateTokens = Tokenize(candidate);
        if (queryTokens.Length == 0 || candidateTokens.Length == 0)
            return 0;

        var total = 0;
        foreach (var queryToken in queryTokens)
        {
            var best = candidateTokens.Max(candidateToken => ScoreToken(queryToken, candidateToken));
            if (best == 0)
                return 0;
            total += best;
        }

        var normalizedQuery = string.Join(' ', queryTokens);
        var normalizedCandidate = string.Join(' ', candidateTokens);
        if (normalizedCandidate == normalizedQuery)
            total += 120;
        else if (normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal))
            total += 60;

        return total;
    }

    private static int ScoreToken(string query, string candidate)
    {
        if (query == candidate)
            return 120;
        if (candidate.StartsWith(query, StringComparison.Ordinal))
            return 105;
        if (query.Length >= 3 && candidate.Contains(query, StringComparison.Ordinal))
            return 92;
        if (query.Length <= 2)
            return 0;

        var maximumDistance = query.Length switch
        {
            <= 4 => 1,
            <= 8 => 2,
            _ => 3,
        };
        var distance = GetEditDistance(query, candidate, maximumDistance);
        return distance <= maximumDistance
            ? 84 - (distance * 8)
            : 0;
    }

    private static string[] Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Add(char.ToLowerInvariant(character));
                continue;
            }

            AddCurrentToken(tokens, current);
        }

        AddCurrentToken(tokens, current);
        return [.. tokens];
    }

    private static void AddCurrentToken(List<string> tokens, List<char> current)
    {
        if (current.Count == 0)
            return;

        tokens.Add(new string([.. current]));
        current.Clear();
    }

    private static int GetEditDistance(string left, string right, int maximumDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maximumDistance)
            return maximumDistance + 1;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximumDistance)
                return maximumDistance + 1;

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
