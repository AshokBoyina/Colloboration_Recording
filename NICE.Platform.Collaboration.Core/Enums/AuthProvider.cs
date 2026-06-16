namespace NICE.Platform.Collaboration.Core.Enums;

/// <summary>
/// Identifies which external authentication validator to use.
/// Mapped from the <c>X-Access-Key</c> request header.
/// </summary>
public enum AuthProvider
{
    /// <summary>READI token — engine makes HTTP POST to the READI validate endpoint.</summary>
    READI,

    /// <summary>NICE token — engine makes HTTP POST to the NICE validate endpoint.</summary>
    NICE,

    /// <summary>Anonymous — engine decodes JWT internally, validates SurveyId / FirstName / LastName claims.</summary>
    ANON,

    /// <summary>
    /// Local signed JWT — engine verifies HMAC-SHA256 signature against a shared secret
    /// stored in <c>appsettings.json → AuthValidation:LocalJwt:Secret</c>.
    /// No external HTTP call is made. Suitable for staging / integration testing
    /// when READI / NICE identity providers are not yet available.
    /// Tokens are self-minted (jwt.io, Postman, or <c>POST /api/v1/demo/mint-token</c>).
    /// </summary>
    LOCAL_JWT
}
