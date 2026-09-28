namespace Ticketing.Application.Common;

/// <summary>The authenticated principal on whose behalf a use case runs.</summary>
public sealed record Caller(string UserId, bool IsAdmin);
