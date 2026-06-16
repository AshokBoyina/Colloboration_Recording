namespace NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Enums;
// Junction: a user can have different roles in different applications
public class ApplicationUser
{
    public Guid ApplicationId { get; private set; }
    public Guid UserId { get; private set; }
    public UserRole Role { get; private set; }
}
