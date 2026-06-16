namespace NICE.Platform.Collaboration.Core.Responses;
public class SessionResponse
{
    public Guid SessionId { get; set; }
    public string Token { get; set; } = default!;
    public string Role { get; set; } = default!;
    public string UserType { get; set; } = default!;
    public Guid ApplicationId { get; set; }
}
