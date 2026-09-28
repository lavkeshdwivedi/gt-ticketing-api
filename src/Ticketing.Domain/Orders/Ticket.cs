using System.Security.Cryptography;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Orders;

/// <summary>An individually addressable admission, so it can later be scanned, transferred or refunded.</summary>
public sealed class Ticket
{
    // Crockford-style alphabet: no 0/O or 1/I/L confusion when read aloud at a gate.
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int CodeLength = 10;

    private Ticket()
    {
        Code = null!;
    }

    private Ticket(Guid orderId)
    {
        Id = SequentialGuid.NewGuid();
        OrderId = orderId;
        Code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public string Code { get; private set; }

    internal static Ticket Issue(Guid orderId) => new(orderId);
}
