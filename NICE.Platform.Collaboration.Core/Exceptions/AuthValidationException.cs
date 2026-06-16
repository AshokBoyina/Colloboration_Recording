namespace NICE.Platform.Collaboration.Core.Exceptions;

/// <summary>
/// Thrown when a READI, NICE, or Anonymous token validation call fails.
/// </summary>
public sealed class AuthValidationException(string message)
    : Exception(message);
