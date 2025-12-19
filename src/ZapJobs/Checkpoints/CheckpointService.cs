using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;
using ZapJobs.Core.Checkpoints;

namespace ZapJobs.Checkpoints;

/// <summary>
/// Implementation of ICheckpointStore that handles compression, serialization,
/// and delegates to IJobStorage for persistence.
/// </summary>
public class CheckpointService : ICheckpointStore
{
    private readonly IJobStorage _storage;
    private readonly CheckpointOptions _options;
    private readonly ILogger<CheckpointService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CheckpointService(
        IJobStorage storage,
        IOptions<CheckpointOptions> options,
        ILogger<CheckpointService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CheckpointResult> SaveAsync<T>(
        Guid runId,
        string key,
        T data,
        CheckpointSaveOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        try
        {
            // Serialize to JSON
            var json = JsonSerializer.Serialize(data, JsonOptions);
            var originalSize = Encoding.UTF8.GetByteCount(json);

            // Check size limit
            if (originalSize > _options.MaxDataSizeBytes)
            {
                var error = $"Checkpoint data size ({originalSize:N0} bytes) exceeds maximum ({_options.MaxDataSizeBytes:N0} bytes)";
                _logger.LogWarning("Checkpoint save rejected for run {RunId}, key {Key}: {Error}", runId, key, error);
                return CheckpointResult.Fail(error);
            }

            // Determine if we should compress
            var shouldCompress = options?.Compress ?? (_options.EnableCompression && originalSize >= _options.CompressionThresholdBytes);
            var finalData = json;
            var isCompressed = false;

            if (shouldCompress)
            {
                var compressed = Compress(json);
                // Only use compression if it actually reduces size
                if (compressed.Length < originalSize)
                {
                    finalData = compressed;
                    isCompressed = true;
                    _logger.LogDebug(
                        "Checkpoint compressed for run {RunId}, key {Key}: {Original:N0} -> {Compressed:N0} bytes ({Ratio:P0})",
                        runId, key, originalSize, compressed.Length, (double)compressed.Length / originalSize);
                }
            }

            var finalSize = isCompressed ? Encoding.UTF8.GetByteCount(finalData) : originalSize;

            // Calculate TTL/expiration
            var ttl = options?.Ttl ?? _options.DefaultTtl;
            var expiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : (DateTimeOffset?)null;

            // Get next sequence number
            var sequenceNumber = await _storage.GetNextCheckpointSequenceAsync(runId, ct);

            // Build checkpoint entity
            var checkpoint = new Checkpoint
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                Key = key,
                DataJson = finalData,
                Version = options?.Version ?? 1,
                SequenceNumber = sequenceNumber,
                DataSizeBytes = finalSize,
                IsCompressed = isCompressed,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
                MetadataJson = options?.Metadata != null
                    ? JsonSerializer.Serialize(options.Metadata, JsonOptions)
                    : null
            };

            // Save to storage
            await _storage.SaveCheckpointAsync(checkpoint, ct);

            _logger.LogInformation(
                "Checkpoint saved for run {RunId}, key {Key}, sequence {Seq}, size {Size:N0} bytes{Compressed}",
                runId, key, sequenceNumber, finalSize, isCompressed ? " (compressed)" : "");

            // Enforce max checkpoints limit
            await EnforceLimitAsync(runId, _options.MaxCheckpointsPerRun, ct);

            return CheckpointResult.Ok(checkpoint.Id, sequenceNumber, isCompressed, finalSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save checkpoint for run {RunId}, key {Key}", runId, key);
            return CheckpointResult.Fail($"Failed to save checkpoint: {ex.Message}");
        }
    }

    public async Task<T?> GetAsync<T>(Guid runId, string key, CancellationToken ct = default) where T : class
    {
        var checkpoint = await GetCheckpointAsync(runId, key, ct);
        if (checkpoint == null)
        {
            return null;
        }

        return DeserializeCheckpoint<T>(checkpoint);
    }

    public async Task<Checkpoint?> GetCheckpointAsync(Guid runId, string key, CancellationToken ct = default)
    {
        return await _storage.GetLatestCheckpointAsync(runId, key, ct);
    }

    public async Task<IReadOnlyList<Checkpoint>> GetAllAsync(Guid runId, CancellationToken ct = default)
    {
        return await _storage.GetCheckpointsAsync(runId, ct);
    }

    public async Task<IReadOnlyList<Checkpoint>> GetHistoryAsync(
        Guid runId,
        string key,
        int limit = 10,
        CancellationToken ct = default)
    {
        return await _storage.GetCheckpointHistoryAsync(runId, key, limit, ct);
    }

    public async Task<bool> ExistsAsync(Guid runId, string key, CancellationToken ct = default)
    {
        var checkpoint = await _storage.GetLatestCheckpointAsync(runId, key, ct);
        return checkpoint != null;
    }

    public async Task<bool> DeleteAsync(Guid checkpointId, CancellationToken ct = default)
    {
        return await _storage.DeleteCheckpointAsync(checkpointId, ct);
    }

    public async Task<int> DeleteAllAsync(Guid runId, CancellationToken ct = default)
    {
        return await _storage.DeleteCheckpointsForRunAsync(runId, ct);
    }

    public async Task<int> DeleteByKeyAsync(Guid runId, string key, CancellationToken ct = default)
    {
        return await _storage.DeleteCheckpointsByKeyAsync(runId, key, ct);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var deleted = await _storage.DeleteExpiredCheckpointsAsync(ct);
        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired checkpoints", deleted);
        }
        return deleted;
    }

    public async Task<int> CleanupCompletedJobsAsync(TimeSpan completedJobAge, CancellationToken ct = default)
    {
        var deleted = await _storage.DeleteCheckpointsForCompletedJobsAsync(completedJobAge, ct);
        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} checkpoints for completed jobs older than {Age}", deleted, completedJobAge);
        }
        return deleted;
    }

    public async Task<int> EnforceLimitAsync(Guid runId, int maxCheckpoints, CancellationToken ct = default)
    {
        var checkpoints = await _storage.GetCheckpointsAsync(runId, ct);
        if (checkpoints.Count <= maxCheckpoints)
        {
            return 0;
        }

        // Delete oldest checkpoints (lowest sequence numbers)
        var toDelete = checkpoints
            .OrderBy(c => c.SequenceNumber)
            .Take(checkpoints.Count - maxCheckpoints)
            .ToList();

        var deleted = 0;
        foreach (var checkpoint in toDelete)
        {
            if (await _storage.DeleteCheckpointAsync(checkpoint.Id, ct))
            {
                deleted++;
            }
        }

        if (deleted > 0)
        {
            _logger.LogDebug("Deleted {Count} old checkpoints for run {RunId} to enforce limit of {Max}",
                deleted, runId, maxCheckpoints);
        }

        return deleted;
    }

    private T? DeserializeCheckpoint<T>(Checkpoint checkpoint) where T : class
    {
        try
        {
            var json = checkpoint.IsCompressed
                ? Decompress(checkpoint.DataJson)
                : checkpoint.DataJson;

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize checkpoint {CheckpointId} for run {RunId}",
                checkpoint.Id, checkpoint.RunId);
            return null;
        }
    }

    private static string Compress(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    private static string Decompress(string compressed)
    {
        var bytes = Convert.FromBase64String(compressed);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
