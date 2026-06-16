namespace NICE.Platform.Collaboration.Core.Exceptions;
public class UnauthorizedCollaborationAccessException : Exception
{
    public UnauthorizedCollaborationAccessException() : base("Access to this collaboration is not permitted.") { }
    public UnauthorizedCollaborationAccessException(string message) : base(message) { }
}
