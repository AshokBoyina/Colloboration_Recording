namespace NICE.Platform.Collaboration.Application.Interfaces.Services;
public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobPath, Stream content, string contentType, CancellationToken ct);
    Task<string> GenerateSasUrlAsync(string blobPath, TimeSpan expiry, CancellationToken ct);
    Task DeleteAsync(string blobPath, CancellationToken ct);
}
