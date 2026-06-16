namespace NICE.Platform.Collaboration.Core.Requests;
public class OnboardUserRequest
{
    public string ExternalId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Role { get; set; } = default!;   // User | Agent | Supervisor
}
