namespace NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Enums;
public class UserProfile
{
    public Guid Id { get; private set; }
    public string ExternalId { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public UserType UserType { get; private set; }
    public bool IsActive { get; private set; }
}
