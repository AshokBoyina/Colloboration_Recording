namespace NICE.Platform.Collaboration.Core.Requests;
public class StartCollaborationRequest
{
    public Guid  UserId            { get; set; }
    /// <summary>Optional. Null = any available agent can accept.</summary>
    public Guid? PreferredAgentId  { get; set; }
    public Guid  ApplicationId     { get; set; }
}
