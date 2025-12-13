using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZapJobs.Core;

namespace ZapJobs.DeadLetter;

/// <summary>
/// Implementation of IDeadLetterManager for managing dead letter queue operations
/// </summary>
public class DeadLetterManager : IDeadLetterManager
{
    private readonly IJobStorage _storage;
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<DeadLetterManager> _logger;

    public DeadLetterManager(
        IJobStorage storage,
        IJobScheduler scheduler,
        ILogger<DeadLetterManager> logger)
    {
        _storage = storage;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> GetEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        return await _storage.GetDeadLetterEntriesAsync(status, jobTypeId, limit, offset, ct);
    }

    public async Task<DeadLetterEntry?> GetEntryAsync(Guid id, CancellationToken ct = default)
    {
        return await _storage.GetDeadLetterEntryAsync(id, ct);
    }

    public async Task<int> GetCountAsync(DeadLetterStatus? status = null, CancellationToken ct = default)
    {
        return await _storage.GetDeadLetterCountAsync(status, ct);
    }

    public async Task<Guid> RequeueAsync(Guid deadLetterId, string? newInput = null, CancellationToken ct = default)
    {
        var entry = await _storage.GetDeadLetterEntryAsync(deadLetterId, ct);
        if (entry == null)
            throw new InvalidOperationException($"Dead letter entry {deadLetterId} not found");

        if (entry.Status != DeadLetterStatus.Pending)
            throw new InvalidOperationException($"Dead letter entry {deadLetterId} is not in Pending status");

        // Use new input if provided, otherwise use original input
        object? input = null;
        var inputJson = newInput ?? entry.InputJson;
        if (!string.IsNullOrEmpty(inputJson))
        {
            input = JsonSerializer.Deserialize<object>(inputJson);
        }

        // Create new run through scheduler
        var newRunId = await _scheduler.EnqueueAsync(entry.JobTypeId, input, entry.Queue, ct);

        // Update dead letter entry
        entry.Status = DeadLetterStatus.Requeued;
        entry.RequeuedAt = DateTime.UtcNow;
        entry.RequeuedRunId = newRunId;
        await _storage.UpdateDeadLetterEntryAsync(entry, ct);

        _logger.LogInformation(
            "Requeued dead letter entry {DeadLetterId} as new run {NewRunId} for job {JobTypeId}",
            deadLetterId, newRunId, entry.JobTypeId);

        return newRunId;
    }

    public async Task DiscardAsync(Guid deadLetterId, string? notes = null, CancellationToken ct = default)
    {
        var entry = await _storage.GetDeadLetterEntryAsync(deadLetterId, ct);
        if (entry == null)
            throw new InvalidOperationException($"Dead letter entry {deadLetterId} not found");

        if (entry.Status != DeadLetterStatus.Pending)
            throw new InvalidOperationException($"Dead letter entry {deadLetterId} is not in Pending status");

        entry.Status = DeadLetterStatus.Discarded;
        entry.Notes = notes;
        await _storage.UpdateDeadLetterEntryAsync(entry, ct);

        _logger.LogInformation(
            "Discarded dead letter entry {DeadLetterId} for job {JobTypeId}",
            deadLetterId, entry.JobTypeId);
    }

    public async Task ArchiveAsync(Guid deadLetterId, string? notes = null, CancellationToken ct = default)
    {
        var entry = await _storage.GetDeadLetterEntryAsync(deadLetterId, ct);
        if (entry == null)
            throw new InvalidOperationException($"Dead letter entry {deadLetterId} not found");

        if (entry.Status != DeadLetterStatus.Pending)
            throw new InvalidOperationException($"Dead letter entry {deadLetterId} is not in Pending status");

        entry.Status = DeadLetterStatus.Archived;
        entry.Notes = notes;
        await _storage.UpdateDeadLetterEntryAsync(entry, ct);

        _logger.LogInformation(
            "Archived dead letter entry {DeadLetterId} for job {JobTypeId}",
            deadLetterId, entry.JobTypeId);
    }

    public async Task<int> RequeueAllAsync(string jobTypeId, CancellationToken ct = default)
    {
        var entries = await _storage.GetDeadLetterEntriesAsync(
            status: DeadLetterStatus.Pending,
            jobTypeId: jobTypeId,
            limit: int.MaxValue,
            ct: ct);

        var count = 0;
        foreach (var entry in entries)
        {
            try
            {
                await RequeueAsync(entry.Id, ct: ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to requeue dead letter entry {DeadLetterId}", entry.Id);
            }
        }

        _logger.LogInformation(
            "Requeued {Count} dead letter entries for job {JobTypeId}",
            count, jobTypeId);

        return count;
    }

    public async Task<int> DiscardAllAsync(string jobTypeId, string? notes = null, CancellationToken ct = default)
    {
        var entries = await _storage.GetDeadLetterEntriesAsync(
            status: DeadLetterStatus.Pending,
            jobTypeId: jobTypeId,
            limit: int.MaxValue,
            ct: ct);

        var count = 0;
        foreach (var entry in entries)
        {
            try
            {
                await DiscardAsync(entry.Id, notes, ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discard dead letter entry {DeadLetterId}", entry.Id);
            }
        }

        _logger.LogInformation(
            "Discarded {Count} dead letter entries for job {JobTypeId}",
            count, jobTypeId);

        return count;
    }
}
