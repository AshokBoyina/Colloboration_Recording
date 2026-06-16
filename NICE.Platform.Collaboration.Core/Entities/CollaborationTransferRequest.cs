namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Records an agent hand-off or transfer request within a collaboration.
/// </summary>
public class CollaborationTransferRequest
{
    public Guid Id { get; set; }

    public Guid CollaborationId { get; set; }

    /// <summary>Agent initiating the transfer.</summary>
    public Guid FromUserId { get; set; }

    /// <summary>
    /// Target agent for a directed transfer.
    /// Null if transferred to a queue (any available agent).
    /// </summary>
    public Guid? ToUserId { get; set; }

    /// <summary>
    /// Target queue / skill group name, if routing to a pool rather than a specific agent.
    /// </summary>
    public string? ToQueue { get; set; }

    /// <summary>Optional note left by the transferring agent.</summary>
    public string? TransferNote { get; set; }

    /// <summary>
    /// Current state: Pending | Accepted | Declined | Cancelled | TimedOut.
    /// </summary>
    public string Status { get; set; } = "Pending";

    public DateTime RequestedAt { get; set; }

    /// <summary>UTC time the target agent accepted or declined.</summary>
    public DateTime? RespondedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Collaboration     CollaborationEntity { get; set; } = null!;
    public CollaborationUser FromUser            { get; set; } = null!;
    public CollaborationUser? ToUser             { get; set; }
}
