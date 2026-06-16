namespace NICE.Platform.Collaboration.Core.Exceptions;

/// <summary>
/// Thrown when the <c>X-Api-Key</c> header is missing, unrecognised,
/// or belongs to a disabled application registration.
/// </summary>
public sealed class InvalidApiKeyException(string message)
    : Exception(message);
