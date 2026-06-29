namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.DeleteRecordingsByCollaboration;

using MediatR;

/// <summary>
/// Deletes the saved recording media for a collaboration from the configured store
/// (local disk OR Azure Blob — resolved via IBlobStorageService) and soft-deletes the
/// recording rows. Recordings that are still in progress are skipped.
/// </summary>
/// <param name="CollaborationId">The collaboration whose recordings should be deleted.</param>
/// <param name="DeletedByUserId">Optional — the user performing the delete (audit).</param>
public record DeleteRecordingsByCollaborationCommand(
    Guid  CollaborationId,
    Guid? DeletedByUserId = null)
    : IRequest<DeleteRecordingsResult>;

/// <summary>Outcome of a delete-by-collaboration request.</summary>
public sealed record DeleteRecordingsResult(
    Guid CollaborationId,
    int  Deleted,
    int  SkippedInProgress,
    int  AlreadyDeleted,
    IReadOnlyList<string> Notes);
