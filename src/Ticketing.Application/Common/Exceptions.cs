namespace Ticketing.Application.Common;

/// <summary>The addressed resource does not exist, or the caller is not allowed to know it exists (404).</summary>
public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} '{id}' was not found.")
{
    public string Resource { get; } = resource;
}

/// <summary>The client's If-Match precondition no longer holds (412).</summary>
public sealed class PreconditionFailedException()
    : Exception("The resource has changed since it was retrieved. Fetch the latest version and retry.");

/// <summary>A concurrent writer changed the aggregate between read and write (409).</summary>
public sealed class ConcurrencyConflictException(Exception? inner = null)
    : Exception("The resource was modified by another request. Retry the operation.", inner);

/// <summary>An idempotency key was reused with a different request body (422).</summary>
public sealed class IdempotencyKeyReusedException()
    : Exception("This Idempotency-Key was already used with a different request.");

/// <summary>Raised by persistence when a concurrent request committed the same idempotency key first.</summary>
public sealed class DuplicateIdempotencyKeyException(Exception? inner = null)
    : Exception("An order with this idempotency key already exists.", inner);
