namespace NICE.Platform.Collaboration.UI.Services;

using NICE.Platform.Collaboration.UI.Models;

public interface IDemoApiService
{
    Task<bool>                  PingAsync(CancellationToken ct = default);
    Task<DemoStatusResult>      GetStatusAsync(CancellationToken ct = default);
    Task<(bool Ok, string Msg)> SeedAppsAsync(CancellationToken ct = default);
    Task<List<DemoUser>>        GetUsersAsync(Guid appId, CancellationToken ct = default);
    Task<DemoUser?>             CreateUserAsync(DemoCreateUserDto dto, CancellationToken ct = default);
    Task<bool>                  RemoveUserAsync(Guid userId, Guid appId, CancellationToken ct = default);
    Task<List<ActiveChannelInfo>>        GetActiveInternalChannelsAsync(Guid appId, CancellationToken ct = default);
    Task<List<CollaborationMessageDto>>  GetCollaborationMessagesAsync(Guid collaborationId, CancellationToken ct = default);
    /// <summary>Returns users currently connected to the hub for one application (non-External only).</summary>
    Task<List<OnlineUserInfo>>           GetOnlineUsersAsync(Guid appId, CancellationToken ct = default);
    /// <summary>Returns ALL non-External users connected across every application — for the global internal chat directory.</summary>
    Task<List<OnlineUserInfo>>           GetAllInternalOnlineUsersAsync(CancellationToken ct = default);
    /// <summary>Returns all non-ended collaborations for the application, with customer name resolved from participants.</summary>
    Task<List<ActiveCollaborationDto>>   GetActiveCollaborationsAsync(Guid appId, CancellationToken ct = default);
    /// <summary>
    /// Marks stuck collaborations (EndedAt == null) as Abandoned.
    /// <paramref name="olderThanHours"/> = 0 (default) cleans ALL stuck sessions;
    /// pass a positive value to restrict to sessions older than that many hours.
    /// </summary>
    Task<(int Cleaned, string Message)>  CleanupStaleSessionsAsync(int olderThanHours = 0, CancellationToken ct = default);
}
