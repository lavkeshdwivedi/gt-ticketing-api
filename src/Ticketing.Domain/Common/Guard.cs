namespace Ticketing.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleViolationException($"{field}.required", $"{field} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new BusinessRuleViolationException(
                $"{field}.too_long", $"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string field, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, field, maxLength);
}
