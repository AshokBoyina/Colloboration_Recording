namespace NICE.Platform.Collaboration.Infrastructure.Storage;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStorageService"/>.
/// Active when <c>FeatureFlags:UseAzureBlob = true</c>.
///
/// Blob paths follow the same convention as <see cref="LocalDiskStorageService"/>
/// (e.g. <c>recordings/2026-06-16/{id}.mp4</c>) and become the blob name inside the
/// container configured at <c>Azure:Blob:Container</c>.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(IConfiguration config, ILogger<AzureBlobStorageService> logger)
    {
        _logger    = logger;
        _container = AzureBlobSupport.CreateContainerClient(config);
    }

    public async Task<string> UploadAsync(
        string blobPath, Stream content, string contentType, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = _container.GetBlobClient(blobPath);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);

        _logger.LogInformation("AzureBlob: uploaded {BlobPath}", blobPath);
        // Return the relative blob path (stored in CollaborationRecording.BlobUri),
        // consistent with the local-disk service. A SAS URL is produced on demand.
        return blobPath;
    }

    public Task<string> GenerateSasUrlAsync(string blobPath, TimeSpan expiry, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobPath);

        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException(
                "Azure:Blob:ConnectionString must use a shared account key to generate SAS URLs " +
                "(managed-identity connection strings cannot sign a service SAS).");

        var sas = blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(expiry));
        return Task.FromResult(sas.ToString());
    }

    public async Task DeleteAsync(string blobPath, CancellationToken ct)
    {
        await _container.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);
        _logger.LogInformation("AzureBlob: deleted {BlobPath}", blobPath);
    }
}

/// <summary>Shared Azure Blob container resolution from configuration.</summary>
internal static class AzureBlobSupport
{
    public static BlobContainerClient CreateContainerClient(IConfiguration config)
    {
        var conn = config["Azure:Blob:ConnectionString"];
        if (string.IsNullOrWhiteSpace(conn) ||
            conn.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "FeatureFlags:UseAzureBlob is true but Azure:Blob:ConnectionString is not configured " +
                "(appsettings.json → Azure:Blob).");

        var containerName = config["Azure:Blob:Container"];
        if (string.IsNullOrWhiteSpace(containerName))
            containerName = "collaboration";

        return new BlobServiceClient(conn).GetBlobContainerClient(containerName);
    }
}
