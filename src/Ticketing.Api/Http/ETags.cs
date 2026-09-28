using Microsoft.Net.Http.Headers;

namespace Ticketing.Api.Http;

internal static class ETags
{
    public static string Format(string version) => $"\"{version}\"";

    /// <summary>
    /// Parses an If-Match value into a version. Returns null when absent or "*" (no precondition).
    /// A value that is not one of our strong ETags maps to a version that can never match.
    /// </summary>
    public static string? ParseIfMatch(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Trim() == "*")
        {
            return null;
        }

        return EntityTagHeaderValue.TryParse(header.Trim(), out var tag) && !tag.IsWeak
            ? tag.Tag.ToString().Trim('"')
            : "invalid";
    }

    public static bool IfNoneMatchHits(string? header, string version) =>
        !string.IsNullOrWhiteSpace(header)
        && EntityTagHeaderValue.TryParseList([header], out var tags)
        && tags.Any(t => t == EntityTagHeaderValue.Any || t.Tag.ToString().Trim('"') == version);
}
