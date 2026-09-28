namespace Ticketing.Domain.Common;

/// <summary>
/// Amount plus ISO 4217 currency. Always decimal, never floating point, and arithmetic across
/// currencies is refused rather than silently producing a meaningless number.
/// </summary>
public sealed record Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Of(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new BusinessRuleViolationException("money.negative", "Amount cannot be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new BusinessRuleViolationException(
                "money.precision", "Amount cannot have more than two decimal places.");
        }

        return new Money(amount, NormalizeCurrency(currency));
    }

    public static Money Zero(string currency) => Of(0m, currency);

    public Money Multiply(int quantity) => new(Amount * quantity, Currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Currency != Currency)
        {
            throw new BusinessRuleViolationException(
                "money.currency_mismatch", $"Cannot add {other.Currency} to {Currency}.");
        }

        return new Money(Amount + other.Amount, Currency);
    }

    public static string NormalizeCurrency(string? currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant();
        if (normalized is not { Length: 3 } || !normalized.All(char.IsAsciiLetterUpper))
        {
            throw new BusinessRuleViolationException(
                "money.invalid_currency", "Currency must be a three-letter ISO 4217 code.");
        }

        return normalized;
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
