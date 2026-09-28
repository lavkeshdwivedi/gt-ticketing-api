namespace Ticketing.Domain.Common;

/// <summary>
/// Base type for every rule the domain refuses to break. <see cref="Code"/> is a stable,
/// machine-readable identifier that API clients can switch on; the message is for humans.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

/// <summary>The request is well formed but breaks a business rule (maps to 422).</summary>
public sealed class BusinessRuleViolationException(string code, string message)
    : DomainException(code, message);

/// <summary>The request conflicts with the current state of the resource (maps to 409).</summary>
public sealed class DomainConflictException(string code, string message)
    : DomainException(code, message);
