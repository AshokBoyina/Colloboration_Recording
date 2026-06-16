namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartStandAloneRecording;

using MediatR;
using NICE.Platform.Collaboration.Core.Responses;

/// <summary>
/// Creates a StandAlone Collaboration + a Recording row for an agent who
/// initiates a screen-capture session without an external customer present.
/// Issued by RecordingHub when the agent calls StartRecording().
/// </summary>
/// <param name="AgentUserId">The agent's internal user id.</param>
/// <param name="ApplicationId">The owning application's id.</param>
/// <param name="CollaborationId">
/// Optional caller-supplied collaboration / session id (e.g. the host app's ticket
/// or interaction GUID). When provided, the recording is attached to that
/// collaboration (created if it does not yet exist) so recordings correlate to the
/// host's own records. When null, a new collaboration id is generated server-side.
/// </param>
public record StartStandAloneRecordingCommand(
    Guid   AgentUserId,
    Guid   ApplicationId,
    Guid?  CollaborationId = null)
    : IRequest<StartStandAloneRecordingResponse>;

public class StartStandAloneRecordingResponse
{
    public Guid CollaborationId { get; init; }
    public Guid RecordingId     { get; init; }
    public string BlobPath      { get; init; } = string.Empty;
}
