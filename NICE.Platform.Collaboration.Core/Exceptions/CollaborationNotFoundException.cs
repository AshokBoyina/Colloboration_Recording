namespace NICE.Platform.Collaboration.Core.Exceptions;

public class CollaborationNotFoundException : Exception
{
    public CollaborationNotFoundException()
        : base("Collaboration not found.") { }

    public CollaborationNotFoundException(string message)
        : base(message) { }

    public CollaborationNotFoundException(Guid collaborationId)
        : base($"Collaboration {collaborationId} not found.") { }
}
