using System.Text.Json;
using Npgsql;
using ZapJobs.Core;
using ZapJobs.Core.Checkpoints;
using ZapJobs.Core.History;

namespace ZapJobs.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of IJobStorage for production use.
/// Uses a dedicated schema (default: "zapjobs") with clean table names.
/// </summary>
public class PostgreSqlJobStorage : IJobStorage
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly JsonSerializerOptions _jsonOptions;

    // Table names with schema
    private readonly string _definitions;
    private readonly string _runs;
    private readonly string _logs;
    private readonly string _heartbeats;
    private readonly string _continuations;
    private readonly string _deadLetter;
    private readonly string _batches;
    private readonly string _batchJobs;
    private readonly string _batchContinuations;
    private readonly string _rateLimitExecutions;
    private readonly string _checkpoints;
    private readonly string _events;

    public PostgreSqlJobStorage(string connectionString) : this(new PostgreSqlStorageOptions { ConnectionString = connectionString })
    {
    }

    public PostgreSqlJobStorage(PostgreSqlStorageOptions options)
    {
        _connectionString = options.ConnectionString;
        _schema = options.Schema;

        // Build schema-qualified table names
        _definitions = $"{_schema}.definitions";
        _runs = $"{_schema}.runs";
        _logs = $"{_schema}.logs";
        _heartbeats = $"{_schema}.heartbeats";
        _continuations = $"{_schema}.continuations";
        _deadLetter = $"{_schema}.dead_letter";
        _batches = $"{_schema}.batches";
        _batchJobs = $"{_schema}.batch_jobs";
        _batchContinuations = $"{_schema}.batch_continuations";
        _rateLimitExecutions = $"{_schema}.rate_limit_executions";
        _checkpoints = $"{_schema}.checkpoints";
        _events = $"{_schema}.events";

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    // Job Definitions

    public async Task<JobDefinition?> GetJobDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT job_type_id, display_name, description, queue, schedule_type, interval_minutes,
                   cron_expression, time_zone_id, is_enabled, max_retries, timeout_seconds,
                   max_concurrency, prevent_overlapping, last_run_at, next_run_at, last_run_status,
                   config_json, created_at, updated_at
            FROM {_definitions}
            WHERE job_type_id = @jobTypeId
            """, conn);

        cmd.Parameters.AddWithValue("jobTypeId", jobTypeId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapDefinition(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT job_type_id, display_name, description, queue, schedule_type, interval_minutes,
                   cron_expression, time_zone_id, is_enabled, max_retries, timeout_seconds,
                   max_concurrency, prevent_overlapping, last_run_at, next_run_at, last_run_status,
                   config_json, created_at, updated_at
            FROM {_definitions}
            ORDER BY display_name
            """, conn);

        var definitions = new List<JobDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            definitions.Add(MapDefinition(reader));
        }
        return definitions;
    }

    public async Task UpsertDefinitionAsync(JobDefinition definition, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_definitions} (
                job_type_id, display_name, description, queue, schedule_type, interval_minutes,
                cron_expression, time_zone_id, is_enabled, max_retries, timeout_seconds,
                max_concurrency, prevent_overlapping, last_run_at, next_run_at, last_run_status,
                config_json, created_at, updated_at
            ) VALUES (
                @jobTypeId, @displayName, @description, @queue, @scheduleType, @intervalMinutes,
                @cronExpression, @timeZoneId, @isEnabled, @maxRetries, @timeoutSeconds,
                @maxConcurrency, @preventOverlapping, @lastRunAt, @nextRunAt, @lastRunStatus,
                @configJson, @createdAt, @updatedAt
            )
            ON CONFLICT (job_type_id) DO UPDATE SET
                display_name = @displayName,
                description = @description,
                queue = @queue,
                schedule_type = @scheduleType,
                interval_minutes = @intervalMinutes,
                cron_expression = @cronExpression,
                time_zone_id = @timeZoneId,
                is_enabled = @isEnabled,
                max_retries = @maxRetries,
                timeout_seconds = @timeoutSeconds,
                max_concurrency = @maxConcurrency,
                prevent_overlapping = @preventOverlapping,
                last_run_at = @lastRunAt,
                next_run_at = @nextRunAt,
                last_run_status = @lastRunStatus,
                config_json = @configJson,
                updated_at = @updatedAt
            """, conn);

        cmd.Parameters.AddWithValue("jobTypeId", definition.JobTypeId);
        cmd.Parameters.AddWithValue("displayName", definition.DisplayName);
        cmd.Parameters.AddWithValue("description", (object?)definition.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("queue", definition.Queue);
        cmd.Parameters.AddWithValue("scheduleType", (int)definition.ScheduleType);
        cmd.Parameters.AddWithValue("intervalMinutes", (object?)definition.IntervalMinutes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cronExpression", (object?)definition.CronExpression ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timeZoneId", (object?)definition.TimeZoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isEnabled", definition.IsEnabled);
        cmd.Parameters.AddWithValue("maxRetries", definition.MaxRetries);
        cmd.Parameters.AddWithValue("timeoutSeconds", definition.TimeoutSeconds);
        cmd.Parameters.AddWithValue("maxConcurrency", definition.MaxConcurrency);
        cmd.Parameters.AddWithValue("preventOverlapping", definition.PreventOverlapping);
        cmd.Parameters.AddWithValue("lastRunAt", (object?)definition.LastRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("nextRunAt", (object?)definition.NextRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastRunStatus", definition.LastRunStatus.HasValue ? (int)definition.LastRunStatus : DBNull.Value);
        cmd.Parameters.AddWithValue("configJson", (object?)definition.ConfigJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", definition.CreatedAt);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {_definitions} WHERE job_type_id = @jobTypeId", conn);
        cmd.Parameters.AddWithValue("jobTypeId", jobTypeId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Job Runs

    public async Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_runs} (
                id, job_type_id, status, trigger_type, triggered_by, worker_id, queue,
                created_at, scheduled_at, started_at, completed_at, duration_ms,
                progress, total, progress_message,
                items_processed, items_succeeded, items_failed,
                attempt_number, next_retry_at, error_message, stack_trace, error_type,
                input_json, output_json, metadata_json
            ) VALUES (
                @id, @jobTypeId, @status, @triggerType, @triggeredBy, @workerId, @queue,
                @createdAt, @scheduledAt, @startedAt, @completedAt, @durationMs,
                @progress, @total, @progressMessage,
                @itemsProcessed, @itemsSucceeded, @itemsFailed,
                @attemptNumber, @nextRetryAt, @errorMessage, @stackTrace, @errorType,
                @inputJson, @outputJson, @metadataJson
            )
            """, conn);

        AddRunParameters(cmd, run);
        await cmd.ExecuteNonQueryAsync(ct);
        return run.Id;
    }

    public async Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, job_type_id, status, trigger_type, triggered_by, worker_id, queue,
                   created_at, scheduled_at, started_at, completed_at, duration_ms,
                   progress, total, progress_message,
                   items_processed, items_succeeded, items_failed,
                   attempt_number, next_retry_at, error_message, stack_trace, error_type,
                   input_json, output_json, metadata_json
            FROM {_runs}
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", runId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapRun(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<JobRun>> GetPendingRunsAsync(string[] queues, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, job_type_id, status, trigger_type, triggered_by, worker_id, queue,
                   created_at, scheduled_at, started_at, completed_at, duration_ms,
                   progress, total, progress_message,
                   items_processed, items_succeeded, items_failed,
                   attempt_number, next_retry_at, error_message, stack_trace, error_type,
                   input_json, output_json, metadata_json
            FROM {_runs}
            WHERE status = @status AND queue = ANY(@queues)
            ORDER BY created_at
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("status", (int)JobRunStatus.Pending);
        cmd.Parameters.AddWithValue("queues", queues);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadRunsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobRun>> GetRunsForRetryAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, job_type_id, status, trigger_type, triggered_by, worker_id, queue,
                   created_at, scheduled_at, started_at, completed_at, duration_ms,
                   progress, total, progress_message,
                   items_processed, items_succeeded, items_failed,
                   attempt_number, next_retry_at, error_message, stack_trace, error_type,
                   input_json, output_json, metadata_json
            FROM {_runs}
            WHERE status = @status
            ORDER BY next_retry_at
            """, conn);

        cmd.Parameters.AddWithValue("status", (int)JobRunStatus.AwaitingRetry);

        return await ReadRunsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(JobRunStatus status, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, job_type_id, status, trigger_type, triggered_by, worker_id, queue,
                   created_at, scheduled_at, started_at, completed_at, duration_ms,
                   progress, total, progress_message,
                   items_processed, items_succeeded, items_failed,
                   attempt_number, next_retry_at, error_message, stack_trace, error_type,
                   input_json, output_json, metadata_json
            FROM {_runs}
            WHERE status = @status
            ORDER BY created_at DESC
            OFFSET @offset LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("status", (int)status);
        cmd.Parameters.AddWithValue("offset", offset);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadRunsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobRun>> GetRunsByJobTypeAsync(string jobTypeId, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, job_type_id, status, trigger_type, triggered_by, worker_id, queue,
                   created_at, scheduled_at, started_at, completed_at, duration_ms,
                   progress, total, progress_message,
                   items_processed, items_succeeded, items_failed,
                   attempt_number, next_retry_at, error_message, stack_trace, error_type,
                   input_json, output_json, metadata_json
            FROM {_runs}
            WHERE job_type_id = @jobTypeId
            ORDER BY created_at DESC
            OFFSET @offset LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("jobTypeId", jobTypeId);
        cmd.Parameters.AddWithValue("offset", offset);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadRunsAsync(cmd, ct);
    }

    public async Task<bool> HasActiveRunAsync(string jobTypeId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT EXISTS (
                SELECT 1 FROM {_runs}
                WHERE job_type_id = @jobTypeId
                AND status IN (@pending, @running)
            )
            """, conn);

        cmd.Parameters.AddWithValue("jobTypeId", jobTypeId);
        cmd.Parameters.AddWithValue("pending", (int)JobRunStatus.Pending);
        cmd.Parameters.AddWithValue("running", (int)JobRunStatus.Running);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    public async Task UpdateRunAsync(JobRun run, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_runs} SET
                status = @status,
                trigger_type = @triggerType,
                triggered_by = @triggeredBy,
                worker_id = @workerId,
                scheduled_at = @scheduledAt,
                started_at = @startedAt,
                completed_at = @completedAt,
                duration_ms = @durationMs,
                progress = @progress,
                total = @total,
                progress_message = @progressMessage,
                items_processed = @itemsProcessed,
                items_succeeded = @itemsSucceeded,
                items_failed = @itemsFailed,
                attempt_number = @attemptNumber,
                next_retry_at = @nextRetryAt,
                error_message = @errorMessage,
                stack_trace = @stackTrace,
                error_type = @errorType,
                output_json = @outputJson,
                metadata_json = @metadataJson
            WHERE id = @id
            """, conn);

        AddRunParameters(cmd, run);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> TryAcquireRunAsync(Guid runId, string workerId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_runs}
            SET status = @newStatus, worker_id = @workerId, started_at = @startedAt
            WHERE id = @id AND status = @oldStatus
            """, conn);

        cmd.Parameters.AddWithValue("id", runId);
        cmd.Parameters.AddWithValue("workerId", workerId);
        cmd.Parameters.AddWithValue("startedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("newStatus", (int)JobRunStatus.Running);
        cmd.Parameters.AddWithValue("oldStatus", (int)JobRunStatus.Pending);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    // Scheduling

    public async Task<IReadOnlyList<JobDefinition>> GetDueJobsAsync(DateTime asOf, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT job_type_id, display_name, description, queue, schedule_type, interval_minutes,
                   cron_expression, time_zone_id, is_enabled, max_retries, timeout_seconds,
                   max_concurrency, prevent_overlapping, last_run_at, next_run_at, last_run_status,
                   config_json, created_at, updated_at
            FROM {_definitions}
            WHERE is_enabled = true AND next_run_at <= @asOf
            ORDER BY next_run_at
            """, conn);

        cmd.Parameters.AddWithValue("asOf", asOf);

        var definitions = new List<JobDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            definitions.Add(MapDefinition(reader));
        }
        return definitions;
    }

    public async Task UpdateNextRunAsync(string jobTypeId, DateTime? nextRun, DateTime? lastRun = null, JobRunStatus? lastStatus = null, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_definitions}
            SET next_run_at = @nextRun,
                last_run_at = COALESCE(@lastRun, last_run_at),
                last_run_status = COALESCE(@lastStatus, last_run_status),
                updated_at = @updatedAt
            WHERE job_type_id = @jobTypeId
            """, conn);

        cmd.Parameters.AddWithValue("jobTypeId", jobTypeId);
        cmd.Parameters.AddWithValue("nextRun", (object?)nextRun ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastRun", (object?)lastRun ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastStatus", lastStatus.HasValue ? (int)lastStatus : DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Logs

    public async Task AddLogAsync(JobLog log, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_logs} (id, run_id, level, message, category, context_json, duration_ms, exception, timestamp)
            VALUES (@id, @runId, @level, @message, @category, @contextJson, @durationMs, @exception, @timestamp)
            """, conn);

        cmd.Parameters.AddWithValue("id", log.Id);
        cmd.Parameters.AddWithValue("runId", log.RunId);
        cmd.Parameters.AddWithValue("level", (int)log.Level);
        cmd.Parameters.AddWithValue("message", log.Message);
        cmd.Parameters.AddWithValue("category", (object?)log.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("contextJson", (object?)log.ContextJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationMs", (object?)log.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("exception", (object?)log.Exception ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timestamp", log.Timestamp);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddLogsAsync(IEnumerable<JobLog> logs, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var batch = new NpgsqlBatch(conn);

        foreach (var log in logs)
        {
            var cmd = new NpgsqlBatchCommand($"""
                INSERT INTO {_logs} (id, run_id, level, message, category, context_json, duration_ms, exception, timestamp)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                """);

            cmd.Parameters.AddWithValue(log.Id);
            cmd.Parameters.AddWithValue(log.RunId);
            cmd.Parameters.AddWithValue((int)log.Level);
            cmd.Parameters.AddWithValue(log.Message);
            cmd.Parameters.AddWithValue((object?)log.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)log.ContextJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)log.DurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)log.Exception ?? DBNull.Value);
            cmd.Parameters.AddWithValue(log.Timestamp);

            batch.BatchCommands.Add(cmd);
        }

        await batch.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<JobLog>> GetLogsAsync(Guid runId, int limit = 500, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, level, message, category, context_json, duration_ms, exception, timestamp
            FROM {_logs}
            WHERE run_id = @runId
            ORDER BY timestamp DESC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("limit", limit);

        var logs = new List<JobLog>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            logs.Add(new JobLog
            {
                Id = reader.GetGuid(0),
                RunId = reader.GetGuid(1),
                Level = (JobLogLevel)reader.GetInt32(2),
                Message = reader.GetString(3),
                Category = reader.IsDBNull(4) ? null : reader.GetString(4),
                ContextJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                DurationMs = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Exception = reader.IsDBNull(7) ? null : reader.GetString(7),
                Timestamp = reader.GetDateTime(8)
            });
        }
        return logs;
    }

    // Heartbeats

    public async Task SendHeartbeatAsync(JobHeartbeat heartbeat, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_heartbeats} (
                id, worker_id, hostname, process_id, current_job_type, current_run_id,
                timestamp, queues, jobs_processed, jobs_failed, is_shutting_down, started_at
            ) VALUES (
                @id, @workerId, @hostname, @processId, @currentJobType, @currentRunId,
                @timestamp, @queues, @jobsProcessed, @jobsFailed, @isShuttingDown, @startedAt
            )
            ON CONFLICT (worker_id) DO UPDATE SET
                hostname = @hostname,
                process_id = @processId,
                current_job_type = @currentJobType,
                current_run_id = @currentRunId,
                timestamp = @timestamp,
                queues = @queues,
                jobs_processed = @jobsProcessed,
                jobs_failed = @jobsFailed,
                is_shutting_down = @isShuttingDown
            """, conn);

        cmd.Parameters.AddWithValue("id", heartbeat.Id);
        cmd.Parameters.AddWithValue("workerId", heartbeat.WorkerId);
        cmd.Parameters.AddWithValue("hostname", heartbeat.Hostname);
        cmd.Parameters.AddWithValue("processId", heartbeat.ProcessId);
        cmd.Parameters.AddWithValue("currentJobType", (object?)heartbeat.CurrentJobType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currentRunId", (object?)heartbeat.CurrentRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timestamp", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("queues", heartbeat.Queues);
        cmd.Parameters.AddWithValue("jobsProcessed", heartbeat.JobsProcessed);
        cmd.Parameters.AddWithValue("jobsFailed", heartbeat.JobsFailed);
        cmd.Parameters.AddWithValue("isShuttingDown", heartbeat.IsShuttingDown);
        cmd.Parameters.AddWithValue("startedAt", heartbeat.StartedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<JobHeartbeat>> GetHeartbeatsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, worker_id, hostname, process_id, current_job_type, current_run_id,
                   timestamp, queues, jobs_processed, jobs_failed, is_shutting_down, started_at
            FROM {_heartbeats}
            ORDER BY timestamp DESC
            """, conn);

        return await ReadHeartbeatsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobHeartbeat>> GetStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, worker_id, hostname, process_id, current_job_type, current_run_id,
                   timestamp, queues, jobs_processed, jobs_failed, is_shutting_down, started_at
            FROM {_heartbeats}
            WHERE timestamp < @cutoff
            """, conn);

        cmd.Parameters.AddWithValue("cutoff", DateTime.UtcNow - threshold);

        return await ReadHeartbeatsAsync(cmd, ct);
    }

    public async Task CleanupStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {_heartbeats} WHERE timestamp < @cutoff", conn);
        cmd.Parameters.AddWithValue("cutoff", DateTime.UtcNow - threshold);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Continuations

    public async Task AddContinuationAsync(JobContinuation continuation, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_continuations} (
                id, parent_run_id, continuation_job_type_id, condition, input_json,
                pass_parent_output, queue, status, continuation_run_id, created_at
            ) VALUES (
                @id, @parentRunId, @continuationJobTypeId, @condition, @inputJson,
                @passParentOutput, @queue, @status, @continuationRunId, @createdAt
            )
            """, conn);

        cmd.Parameters.AddWithValue("id", continuation.Id);
        cmd.Parameters.AddWithValue("parentRunId", continuation.ParentRunId);
        cmd.Parameters.AddWithValue("continuationJobTypeId", continuation.ContinuationJobTypeId);
        cmd.Parameters.AddWithValue("condition", (int)continuation.Condition);
        cmd.Parameters.AddWithValue("inputJson", (object?)continuation.InputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("passParentOutput", continuation.PassParentOutput);
        cmd.Parameters.AddWithValue("queue", (object?)continuation.Queue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (int)continuation.Status);
        cmd.Parameters.AddWithValue("continuationRunId", (object?)continuation.ContinuationRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", continuation.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<JobContinuation>> GetContinuationsAsync(Guid parentRunId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, parent_run_id, continuation_job_type_id, condition, input_json,
                   pass_parent_output, queue, status, continuation_run_id, created_at
            FROM {_continuations}
            WHERE parent_run_id = @parentRunId
            ORDER BY created_at
            """, conn);

        cmd.Parameters.AddWithValue("parentRunId", parentRunId);

        var continuations = new List<JobContinuation>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            continuations.Add(MapContinuation(reader));
        }
        return continuations;
    }

    public async Task UpdateContinuationAsync(JobContinuation continuation, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_continuations} SET
                status = @status,
                continuation_run_id = @continuationRunId
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", continuation.Id);
        cmd.Parameters.AddWithValue("status", (int)continuation.Status);
        cmd.Parameters.AddWithValue("continuationRunId", (object?)continuation.ContinuationRunId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Dead Letter Queue

    public async Task MoveToDeadLetterAsync(JobRun failedRun, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_deadLetter} (
                id, original_run_id, job_type_id, queue, input_json,
                error_message, error_type, stack_trace, attempt_count,
                moved_at, status, requeued_at, requeued_run_id, notes
            ) VALUES (
                @id, @originalRunId, @jobTypeId, @queue, @inputJson,
                @errorMessage, @errorType, @stackTrace, @attemptCount,
                @movedAt, @status, @requeuedAt, @requeuedRunId, @notes
            )
            """, conn);

        var entry = new DeadLetterEntry
        {
            Id = Guid.NewGuid(),
            OriginalRunId = failedRun.Id,
            JobTypeId = failedRun.JobTypeId,
            Queue = failedRun.Queue,
            InputJson = failedRun.InputJson,
            ErrorMessage = failedRun.ErrorMessage ?? string.Empty,
            ErrorType = failedRun.ErrorType,
            StackTrace = failedRun.StackTrace,
            AttemptCount = failedRun.AttemptNumber,
            MovedAt = DateTime.UtcNow,
            Status = DeadLetterStatus.Pending
        };

        AddDeadLetterParameters(cmd, entry);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<DeadLetterEntry?> GetDeadLetterEntryAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, original_run_id, job_type_id, queue, input_json,
                   error_message, error_type, stack_trace, attempt_count,
                   moved_at, status, requeued_at, requeued_run_id, notes
            FROM {_deadLetter}
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapDeadLetterEntry(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> GetDeadLetterEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var whereClause = "WHERE 1=1";
        if (status.HasValue)
            whereClause += " AND status = @status";
        if (!string.IsNullOrEmpty(jobTypeId))
            whereClause += " AND job_type_id = @jobTypeId";

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, original_run_id, job_type_id, queue, input_json,
                   error_message, error_type, stack_trace, attempt_count,
                   moved_at, status, requeued_at, requeued_run_id, notes
            FROM {_deadLetter}
            {whereClause}
            ORDER BY moved_at DESC
            OFFSET @offset LIMIT @limit
            """, conn);

        if (status.HasValue)
            cmd.Parameters.AddWithValue("status", (int)status.Value);
        if (!string.IsNullOrEmpty(jobTypeId))
            cmd.Parameters.AddWithValue("jobTypeId", jobTypeId);
        cmd.Parameters.AddWithValue("offset", offset);
        cmd.Parameters.AddWithValue("limit", limit);

        var entries = new List<DeadLetterEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(MapDeadLetterEntry(reader));
        }
        return entries;
    }

    public async Task<int> GetDeadLetterCountAsync(DeadLetterStatus? status = null, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var whereClause = status.HasValue ? "WHERE status = @status" : "";

        await using var cmd = new NpgsqlCommand($"""
            SELECT COUNT(*) FROM {_deadLetter} {whereClause}
            """, conn);

        if (status.HasValue)
            cmd.Parameters.AddWithValue("status", (int)status.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task UpdateDeadLetterEntryAsync(DeadLetterEntry entry, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_deadLetter} SET
                status = @status,
                requeued_at = @requeuedAt,
                requeued_run_id = @requeuedRunId,
                notes = @notes
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", entry.Id);
        cmd.Parameters.AddWithValue("status", (int)entry.Status);
        cmd.Parameters.AddWithValue("requeuedAt", (object?)entry.RequeuedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("requeuedRunId", (object?)entry.RequeuedRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)entry.Notes ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Batches

    public async Task CreateBatchAsync(JobBatch batch, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_batches} (
                id, name, parent_batch_id, status, total_jobs, completed_jobs, failed_jobs,
                created_by, created_at, started_at, completed_at, expires_at
            ) VALUES (
                @id, @name, @parentBatchId, @status, @totalJobs, @completedJobs, @failedJobs,
                @createdBy, @createdAt, @startedAt, @completedAt, @expiresAt
            )
            """, conn);

        cmd.Parameters.AddWithValue("id", batch.Id);
        cmd.Parameters.AddWithValue("name", batch.Name);
        cmd.Parameters.AddWithValue("parentBatchId", (object?)batch.ParentBatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (int)batch.Status);
        cmd.Parameters.AddWithValue("totalJobs", batch.TotalJobs);
        cmd.Parameters.AddWithValue("completedJobs", batch.CompletedJobs);
        cmd.Parameters.AddWithValue("failedJobs", batch.FailedJobs);
        cmd.Parameters.AddWithValue("createdBy", (object?)batch.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", batch.CreatedAt);
        cmd.Parameters.AddWithValue("startedAt", (object?)batch.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completedAt", (object?)batch.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expiresAt", (object?)batch.ExpiresAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<JobBatch?> GetBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, name, parent_batch_id, status, total_jobs, completed_jobs, failed_jobs,
                   created_by, created_at, started_at, completed_at, expires_at
            FROM {_batches}
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", batchId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapBatch(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<JobBatch>> GetNestedBatchesAsync(Guid parentBatchId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, name, parent_batch_id, status, total_jobs, completed_jobs, failed_jobs,
                   created_by, created_at, started_at, completed_at, expires_at
            FROM {_batches}
            WHERE parent_batch_id = @parentBatchId
            ORDER BY created_at
            """, conn);

        cmd.Parameters.AddWithValue("parentBatchId", parentBatchId);

        var batches = new List<JobBatch>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            batches.Add(MapBatch(reader));
        }
        return batches;
    }

    public async Task UpdateBatchAsync(JobBatch batch, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_batches} SET
                status = @status,
                total_jobs = @totalJobs,
                completed_jobs = @completedJobs,
                failed_jobs = @failedJobs,
                started_at = @startedAt,
                completed_at = @completedAt,
                expires_at = @expiresAt
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", batch.Id);
        cmd.Parameters.AddWithValue("status", (int)batch.Status);
        cmd.Parameters.AddWithValue("totalJobs", batch.TotalJobs);
        cmd.Parameters.AddWithValue("completedJobs", batch.CompletedJobs);
        cmd.Parameters.AddWithValue("failedJobs", batch.FailedJobs);
        cmd.Parameters.AddWithValue("startedAt", (object?)batch.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completedAt", (object?)batch.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expiresAt", (object?)batch.ExpiresAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddBatchJobAsync(BatchJob batchJob, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_batchJobs} (batch_id, run_id, order_num)
            VALUES (@batchId, @runId, @orderNum)
            """, conn);

        cmd.Parameters.AddWithValue("batchId", batchJob.BatchId);
        cmd.Parameters.AddWithValue("runId", batchJob.RunId);
        cmd.Parameters.AddWithValue("orderNum", batchJob.Order);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<JobRun>> GetBatchJobsAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT r.id, r.job_type_id, r.status, r.trigger_type, r.triggered_by, r.worker_id, r.queue,
                   r.created_at, r.scheduled_at, r.started_at, r.completed_at, r.duration_ms,
                   r.progress, r.total, r.progress_message,
                   r.items_processed, r.items_succeeded, r.items_failed,
                   r.attempt_number, r.next_retry_at, r.error_message, r.stack_trace, r.error_type,
                   r.input_json, r.output_json, r.metadata_json
            FROM {_runs} r
            INNER JOIN {_batchJobs} bj ON r.id = bj.run_id
            WHERE bj.batch_id = @batchId
            ORDER BY bj.order_num
            """, conn);

        cmd.Parameters.AddWithValue("batchId", batchId);

        return await ReadRunsAsync(cmd, ct);
    }

    public async Task AddBatchContinuationAsync(BatchContinuation continuation, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_batchContinuations} (
                id, batch_id, trigger_type, job_type_id, input_json, status, continuation_run_id, created_at
            ) VALUES (
                @id, @batchId, @triggerType, @jobTypeId, @inputJson, @status, @continuationRunId, @createdAt
            )
            """, conn);

        cmd.Parameters.AddWithValue("id", continuation.Id);
        cmd.Parameters.AddWithValue("batchId", continuation.BatchId);
        cmd.Parameters.AddWithValue("triggerType", continuation.TriggerType);
        cmd.Parameters.AddWithValue("jobTypeId", continuation.JobTypeId);
        cmd.Parameters.AddWithValue("inputJson", (object?)continuation.InputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (int)continuation.Status);
        cmd.Parameters.AddWithValue("continuationRunId", (object?)continuation.ContinuationRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", continuation.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<BatchContinuation>> GetBatchContinuationsAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, batch_id, trigger_type, job_type_id, input_json, status, continuation_run_id, created_at
            FROM {_batchContinuations}
            WHERE batch_id = @batchId
            ORDER BY created_at
            """, conn);

        cmd.Parameters.AddWithValue("batchId", batchId);

        var continuations = new List<BatchContinuation>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            continuations.Add(MapBatchContinuation(reader));
        }
        return continuations;
    }

    public async Task UpdateBatchContinuationAsync(BatchContinuation continuation, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_batchContinuations} SET
                status = @status,
                continuation_run_id = @continuationRunId
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", continuation.Id);
        cmd.Parameters.AddWithValue("status", (int)continuation.Status);
        cmd.Parameters.AddWithValue("continuationRunId", (object?)continuation.ContinuationRunId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Rate Limiting

    public async Task RecordRateLimitExecutionAsync(string key, DateTime executedAt, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_rateLimitExecutions} (id, key, executed_at)
            VALUES (@id, @key, @executedAt)
            """, conn);

        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("executedAt", executedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountRateLimitExecutionsAsync(string key, DateTime windowStart, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT COUNT(*) FROM {_rateLimitExecutions}
            WHERE key = @key AND executed_at >= @windowStart
            """, conn);

        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("windowStart", windowStart);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<DateTime?> GetOldestRateLimitExecutionAsync(string key, DateTime windowStart, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT MIN(executed_at) FROM {_rateLimitExecutions}
            WHERE key = @key AND executed_at >= @windowStart
            """, conn);

        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("windowStart", windowStart);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value)
            return null;

        return (DateTime)result;
    }

    public async Task<int> CleanupRateLimitExecutionsAsync(DateTime olderThan, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_rateLimitExecutions}
                WHERE executed_at < @olderThan
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        cmd.Parameters.AddWithValue("olderThan", olderThan);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    // Checkpoints

    public async Task SaveCheckpointAsync(Checkpoint checkpoint, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_checkpoints} (
                id, run_id, key, data_json, version, sequence_number,
                data_size_bytes, is_compressed, created_at, expires_at, metadata_json
            ) VALUES (
                @id, @runId, @key, @dataJson, @version, @sequenceNumber,
                @dataSizeBytes, @isCompressed, @createdAt, @expiresAt, @metadataJson
            )
            """, conn);

        cmd.Parameters.AddWithValue("id", checkpoint.Id);
        cmd.Parameters.AddWithValue("runId", checkpoint.RunId);
        cmd.Parameters.AddWithValue("key", checkpoint.Key);
        cmd.Parameters.AddWithValue("dataJson", checkpoint.DataJson);
        cmd.Parameters.AddWithValue("version", checkpoint.Version);
        cmd.Parameters.AddWithValue("sequenceNumber", checkpoint.SequenceNumber);
        cmd.Parameters.AddWithValue("dataSizeBytes", checkpoint.DataSizeBytes);
        cmd.Parameters.AddWithValue("isCompressed", checkpoint.IsCompressed);
        cmd.Parameters.AddWithValue("createdAt", checkpoint.CreatedAt);
        cmd.Parameters.AddWithValue("expiresAt", (object?)checkpoint.ExpiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadataJson", (object?)checkpoint.MetadataJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Checkpoint?> GetLatestCheckpointAsync(Guid runId, string key, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, key, data_json, version, sequence_number,
                   data_size_bytes, is_compressed, created_at, expires_at, metadata_json
            FROM {_checkpoints}
            WHERE run_id = @runId AND key = @key
            ORDER BY sequence_number DESC
            LIMIT 1
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapCheckpoint(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<Checkpoint>> GetCheckpointsAsync(Guid runId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, key, data_json, version, sequence_number,
                   data_size_bytes, is_compressed, created_at, expires_at, metadata_json
            FROM {_checkpoints}
            WHERE run_id = @runId
            ORDER BY sequence_number DESC
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);

        var checkpoints = new List<Checkpoint>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            checkpoints.Add(MapCheckpoint(reader));
        }
        return checkpoints;
    }

    public async Task<IReadOnlyList<Checkpoint>> GetCheckpointHistoryAsync(Guid runId, string key, int limit = 10, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, key, data_json, version, sequence_number,
                   data_size_bytes, is_compressed, created_at, expires_at, metadata_json
            FROM {_checkpoints}
            WHERE run_id = @runId AND key = @key
            ORDER BY sequence_number DESC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("limit", limit);

        var checkpoints = new List<Checkpoint>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            checkpoints.Add(MapCheckpoint(reader));
        }
        return checkpoints;
    }

    public async Task<int> GetNextCheckpointSequenceAsync(Guid runId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT COALESCE(MAX(sequence_number), 0) + 1
            FROM {_checkpoints}
            WHERE run_id = @runId
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> DeleteCheckpointAsync(Guid checkpointId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            DELETE FROM {_checkpoints} WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", checkpointId);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<int> DeleteCheckpointsForRunAsync(Guid runId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_checkpoints}
                WHERE run_id = @runId
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> DeleteCheckpointsByKeyAsync(Guid runId, string key, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_checkpoints}
                WHERE run_id = @runId AND key = @key
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("key", key);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> DeleteExpiredCheckpointsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_checkpoints}
                WHERE expires_at IS NOT NULL AND expires_at <= NOW()
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> DeleteCheckpointsForCompletedJobsAsync(TimeSpan completedJobAge, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_checkpoints} c
                USING {_runs} r
                WHERE c.run_id = r.id
                AND r.status IN (@completed, @failed)
                AND r.completed_at < @cutoff
                RETURNING c.id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        cmd.Parameters.AddWithValue("completed", (int)JobRunStatus.Completed);
        cmd.Parameters.AddWithValue("failed", (int)JobRunStatus.Failed);
        cmd.Parameters.AddWithValue("cutoff", DateTime.UtcNow - completedJobAge);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static Checkpoint MapCheckpoint(NpgsqlDataReader reader)
    {
        return new Checkpoint
        {
            Id = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            Key = reader.GetString(2),
            DataJson = reader.GetString(3),
            Version = reader.GetInt32(4),
            SequenceNumber = reader.GetInt32(5),
            DataSizeBytes = reader.GetInt32(6),
            IsCompressed = reader.GetBoolean(7),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(8),
            ExpiresAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            MetadataJson = reader.IsDBNull(10) ? null : reader.GetString(10)
        };
    }

    // Events (Event History/Audit Trail)

    public async Task AppendEventAsync(JobEvent @event, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Auto-assign sequence number if not set
        if (@event.SequenceNumber == 0)
        {
            @event.SequenceNumber = await GetNextEventSequenceInternalAsync(conn, ct);
        }

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_events} (
                id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                timestamp, payload_json, correlation_id, causation_id, actor, source,
                tags, version, duration_ms, is_success, metadata_json
            ) VALUES (
                @id, @runId, @workflowId, @eventType, @category, @sequenceNumber, @runSequence,
                @timestamp, @payloadJson, @correlationId, @causationId, @actor, @source,
                @tags, @version, @durationMs, @isSuccess, @metadataJson
            )
            """, conn);

        AddEventParameters(cmd, @event);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendEventsAsync(IEnumerable<JobEvent> events, CancellationToken ct = default)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0) return;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Get starting sequence number for batch
        var nextSeq = await GetNextEventSequenceInternalAsync(conn, ct);

        await using var batch = new NpgsqlBatch(conn);

        foreach (var @event in eventList)
        {
            if (@event.SequenceNumber == 0)
            {
                @event.SequenceNumber = nextSeq++;
            }

            var cmd = new NpgsqlBatchCommand($"""
                INSERT INTO {_events} (
                    id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                    timestamp, payload_json, correlation_id, causation_id, actor, source,
                    tags, version, duration_ms, is_success, metadata_json
                ) VALUES (
                    $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18
                )
                """);

            cmd.Parameters.AddWithValue(@event.Id);
            cmd.Parameters.AddWithValue(@event.RunId);
            cmd.Parameters.AddWithValue((object?)@event.WorkflowId ?? DBNull.Value);
            cmd.Parameters.AddWithValue(@event.EventType);
            cmd.Parameters.AddWithValue(@event.Category);
            cmd.Parameters.AddWithValue(@event.SequenceNumber);
            cmd.Parameters.AddWithValue(@event.RunSequence);
            cmd.Parameters.AddWithValue(@event.Timestamp);
            cmd.Parameters.AddWithValue(@event.PayloadJson);
            cmd.Parameters.AddWithValue((object?)@event.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)@event.CausationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)@event.Actor ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)@event.Source ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)@event.Tags ?? DBNull.Value);
            cmd.Parameters.AddWithValue(@event.Version);
            cmd.Parameters.AddWithValue((object?)@event.DurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)@event.IsSuccess ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)@event.MetadataJson ?? DBNull.Value);

            batch.BatchCommands.Add(cmd);
        }

        await batch.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<JobEvent>> GetEventsAsync(Guid runId, CancellationToken ct = default)
    {
        return await GetEventsAsync(runId, null, null, 1000, 0, ct);
    }

    public async Task<IReadOnlyList<JobEvent>> GetEventsAsync(
        Guid runId,
        string? eventType,
        string? category,
        int limit = 1000,
        int offset = 0,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var whereClause = "WHERE run_id = @runId";
        if (!string.IsNullOrEmpty(eventType))
            whereClause += " AND event_type = @eventType";
        if (!string.IsNullOrEmpty(category))
            whereClause += " AND category = @category";

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            {whereClause}
            ORDER BY sequence_number ASC
            OFFSET @offset LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        if (!string.IsNullOrEmpty(eventType))
            cmd.Parameters.AddWithValue("eventType", eventType);
        if (!string.IsNullOrEmpty(category))
            cmd.Parameters.AddWithValue("category", category);
        cmd.Parameters.AddWithValue("offset", offset);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadEventsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobEvent>> GetEventsAfterSequenceAsync(
        Guid runId,
        long afterSequence,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            WHERE run_id = @runId AND sequence_number > @afterSequence
            ORDER BY sequence_number ASC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("afterSequence", afterSequence);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadEventsAsync(cmd, ct);
    }

    public async Task<JobEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", eventId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapEvent(reader);
        }
        return null;
    }

    public async Task<JobEvent?> GetLatestEventAsync(Guid runId, string eventType, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            WHERE run_id = @runId AND event_type = @eventType
            ORDER BY sequence_number DESC
            LIMIT 1
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("eventType", eventType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapEvent(reader);
        }
        return null;
    }

    public async Task<int> GetEventCountAsync(Guid runId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT COUNT(*) FROM {_events} WHERE run_id = @runId
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<long> GetNextEventSequenceAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await GetNextEventSequenceInternalAsync(conn, ct);
    }

    private async Task<long> GetNextEventSequenceInternalAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT COALESCE(MAX(sequence_number), 0) + 1 FROM {_events}
            """, conn);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<JobEvent>> GetEventsByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            WHERE correlation_id = @correlationId
            ORDER BY sequence_number ASC
            """, conn);

        cmd.Parameters.AddWithValue("correlationId", correlationId);

        return await ReadEventsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobEvent>> GetEventsByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            WHERE workflow_id = @workflowId
            ORDER BY sequence_number ASC
            """, conn);

        cmd.Parameters.AddWithValue("workflowId", workflowId);

        return await ReadEventsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<JobEvent>> SearchEventsAsync(
        string query,
        Guid? runId = null,
        Guid? workflowId = null,
        string[]? eventTypes = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var conditions = new List<string>();

        // Full-text search on payload_json using PostgreSQL's ILIKE or full-text search
        if (!string.IsNullOrEmpty(query))
            conditions.Add("(payload_json ILIKE @query OR metadata_json ILIKE @query)");
        if (runId.HasValue)
            conditions.Add("run_id = @runId");
        if (workflowId.HasValue)
            conditions.Add("workflow_id = @workflowId");
        if (eventTypes?.Length > 0)
            conditions.Add("event_type = ANY(@eventTypes)");
        if (from.HasValue)
            conditions.Add("timestamp >= @from");
        if (to.HasValue)
            conditions.Add("timestamp <= @to");

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, run_id, workflow_id, event_type, category, sequence_number, run_sequence,
                   timestamp, payload_json, correlation_id, causation_id, actor, source,
                   tags, version, duration_ms, is_success, metadata_json
            FROM {_events}
            {whereClause}
            ORDER BY timestamp DESC
            LIMIT @limit
            """, conn);

        if (!string.IsNullOrEmpty(query))
            cmd.Parameters.AddWithValue("query", $"%{query}%");
        if (runId.HasValue)
            cmd.Parameters.AddWithValue("runId", runId.Value);
        if (workflowId.HasValue)
            cmd.Parameters.AddWithValue("workflowId", workflowId.Value);
        if (eventTypes?.Length > 0)
            cmd.Parameters.AddWithValue("eventTypes", eventTypes);
        if (from.HasValue)
            cmd.Parameters.AddWithValue("from", from.Value);
        if (to.HasValue)
            cmd.Parameters.AddWithValue("to", to.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadEventsAsync(cmd, ct);
    }

    public async Task<int> DeleteEventsForRunAsync(Guid runId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_events}
                WHERE run_id = @runId
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        cmd.Parameters.AddWithValue("runId", runId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> DeleteOldEventsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted AS (
                DELETE FROM {_events}
                WHERE timestamp < @cutoff
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted
            """, conn);

        cmd.Parameters.AddWithValue("cutoff", DateTimeOffset.UtcNow - maxAge);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static void AddEventParameters(NpgsqlCommand cmd, JobEvent @event)
    {
        cmd.Parameters.AddWithValue("id", @event.Id);
        cmd.Parameters.AddWithValue("runId", @event.RunId);
        cmd.Parameters.AddWithValue("workflowId", (object?)@event.WorkflowId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eventType", @event.EventType);
        cmd.Parameters.AddWithValue("category", @event.Category);
        cmd.Parameters.AddWithValue("sequenceNumber", @event.SequenceNumber);
        cmd.Parameters.AddWithValue("runSequence", @event.RunSequence);
        cmd.Parameters.AddWithValue("timestamp", @event.Timestamp);
        cmd.Parameters.AddWithValue("payloadJson", @event.PayloadJson);
        cmd.Parameters.AddWithValue("correlationId", (object?)@event.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("causationId", (object?)@event.CausationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actor", (object?)@event.Actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source", (object?)@event.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tags", (object?)@event.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("version", @event.Version);
        cmd.Parameters.AddWithValue("durationMs", (object?)@event.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isSuccess", (object?)@event.IsSuccess ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadataJson", (object?)@event.MetadataJson ?? DBNull.Value);
    }

    private static JobEvent MapEvent(NpgsqlDataReader reader)
    {
        return new JobEvent
        {
            Id = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            WorkflowId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            EventType = reader.GetString(3),
            Category = reader.GetString(4),
            SequenceNumber = reader.GetInt64(5),
            RunSequence = reader.GetInt32(6),
            Timestamp = reader.GetFieldValue<DateTimeOffset>(7),
            PayloadJson = reader.GetString(8),
            CorrelationId = reader.IsDBNull(9) ? null : reader.GetString(9),
            CausationId = reader.IsDBNull(10) ? null : reader.GetString(10),
            Actor = reader.IsDBNull(11) ? null : reader.GetString(11),
            Source = reader.IsDBNull(12) ? null : reader.GetString(12),
            Tags = reader.IsDBNull(13) ? null : reader.GetFieldValue<string[]>(13),
            Version = reader.GetInt32(14),
            DurationMs = reader.IsDBNull(15) ? null : reader.GetInt64(15),
            IsSuccess = reader.IsDBNull(16) ? null : reader.GetBoolean(16),
            MetadataJson = reader.IsDBNull(17) ? null : reader.GetString(17)
        };
    }

    private static async Task<IReadOnlyList<JobEvent>> ReadEventsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var events = new List<JobEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(MapEvent(reader));
        }
        return events;
    }

    // Maintenance

    public async Task<int> CleanupOldRunsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted_runs AS (
                DELETE FROM {_runs}
                WHERE completed_at < @cutoff
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted_runs
            """, conn);

        cmd.Parameters.AddWithValue("cutoff", DateTime.UtcNow - retention);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> CleanupOldLogsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            WITH deleted_logs AS (
                DELETE FROM {_logs}
                WHERE timestamp < @cutoff
                RETURNING id
            )
            SELECT COUNT(*) FROM deleted_logs
            """, conn);

        cmd.Parameters.AddWithValue("cutoff", DateTime.UtcNow - retention);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT
                (SELECT COUNT(*) FROM {_definitions}) as total_jobs,
                (SELECT COUNT(*) FROM {_runs}) as total_runs,
                (SELECT COUNT(*) FROM {_runs} WHERE status = 0) as pending_runs,
                (SELECT COUNT(*) FROM {_runs} WHERE status = 2) as running_runs,
                (SELECT COUNT(*) FROM {_runs} WHERE status = 3 AND DATE(completed_at) = CURRENT_DATE) as completed_today,
                (SELECT COUNT(*) FROM {_runs} WHERE status = 4 AND DATE(completed_at) = CURRENT_DATE) as failed_today,
                (SELECT COUNT(*) FROM {_heartbeats} WHERE timestamp > NOW() - INTERVAL '2 minutes') as active_workers,
                (SELECT COUNT(*) FROM {_logs}) as total_logs,
                (SELECT COUNT(*) FROM {_deadLetter} WHERE status = 0) as dead_letter_count
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new JobStorageStats(
                TotalJobs: reader.GetInt32(0),
                TotalRuns: reader.GetInt32(1),
                PendingRuns: reader.GetInt32(2),
                RunningRuns: reader.GetInt32(3),
                CompletedToday: reader.GetInt32(4),
                FailedToday: reader.GetInt32(5),
                ActiveWorkers: reader.GetInt32(6),
                TotalLogEntries: reader.GetInt64(7),
                DeadLetterCount: reader.GetInt32(8)
            );
        }

        return new JobStorageStats(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public async Task<int> GetDeadLetterCountAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {_runs} WHERE status = @status", conn);
        cmd.Parameters.AddWithValue("status", (int)JobRunStatus.Failed);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    // Helper methods

    private static JobDefinition MapDefinition(NpgsqlDataReader reader)
    {
        return new JobDefinition
        {
            JobTypeId = reader.GetString(0),
            DisplayName = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Queue = reader.GetString(3),
            ScheduleType = (ScheduleType)reader.GetInt32(4),
            IntervalMinutes = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            CronExpression = reader.IsDBNull(6) ? null : reader.GetString(6),
            TimeZoneId = reader.IsDBNull(7) ? null : reader.GetString(7),
            IsEnabled = reader.GetBoolean(8),
            MaxRetries = reader.GetInt32(9),
            TimeoutSeconds = reader.GetInt32(10),
            MaxConcurrency = reader.GetInt32(11),
            PreventOverlapping = reader.GetBoolean(12),
            LastRunAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            NextRunAt = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
            LastRunStatus = reader.IsDBNull(15) ? null : (JobRunStatus)reader.GetInt32(15),
            ConfigJson = reader.IsDBNull(16) ? null : reader.GetString(16),
            CreatedAt = reader.GetDateTime(17),
            UpdatedAt = reader.GetDateTime(18)
        };
    }

    private static JobRun MapRun(NpgsqlDataReader reader)
    {
        return new JobRun
        {
            Id = reader.GetGuid(0),
            JobTypeId = reader.GetString(1),
            Status = (JobRunStatus)reader.GetInt32(2),
            TriggerType = (JobTriggerType)reader.GetInt32(3),
            TriggeredBy = reader.IsDBNull(4) ? null : reader.GetString(4),
            WorkerId = reader.IsDBNull(5) ? null : reader.GetString(5),
            Queue = reader.GetString(6),
            CreatedAt = reader.GetDateTime(7),
            ScheduledAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            StartedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            CompletedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            DurationMs = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            Progress = reader.GetInt32(12),
            Total = reader.GetInt32(13),
            ProgressMessage = reader.IsDBNull(14) ? null : reader.GetString(14),
            ItemsProcessed = reader.GetInt32(15),
            ItemsSucceeded = reader.GetInt32(16),
            ItemsFailed = reader.GetInt32(17),
            AttemptNumber = reader.GetInt32(18),
            NextRetryAt = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
            ErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20),
            StackTrace = reader.IsDBNull(21) ? null : reader.GetString(21),
            ErrorType = reader.IsDBNull(22) ? null : reader.GetString(22),
            InputJson = reader.IsDBNull(23) ? null : reader.GetString(23),
            OutputJson = reader.IsDBNull(24) ? null : reader.GetString(24),
            MetadataJson = reader.IsDBNull(25) ? null : reader.GetString(25)
        };
    }

    private static JobContinuation MapContinuation(NpgsqlDataReader reader)
    {
        return new JobContinuation
        {
            Id = reader.GetGuid(0),
            ParentRunId = reader.GetGuid(1),
            ContinuationJobTypeId = reader.GetString(2),
            Condition = (ContinuationCondition)reader.GetInt32(3),
            InputJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            PassParentOutput = reader.GetBoolean(5),
            Queue = reader.IsDBNull(6) ? null : reader.GetString(6),
            Status = (ContinuationStatus)reader.GetInt32(7),
            ContinuationRunId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
            CreatedAt = reader.GetDateTime(9)
        };
    }

    private static DeadLetterEntry MapDeadLetterEntry(NpgsqlDataReader reader)
    {
        return new DeadLetterEntry
        {
            Id = reader.GetGuid(0),
            OriginalRunId = reader.GetGuid(1),
            JobTypeId = reader.GetString(2),
            Queue = reader.GetString(3),
            InputJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            ErrorMessage = reader.GetString(5),
            ErrorType = reader.IsDBNull(6) ? null : reader.GetString(6),
            StackTrace = reader.IsDBNull(7) ? null : reader.GetString(7),
            AttemptCount = reader.GetInt32(8),
            MovedAt = reader.GetDateTime(9),
            Status = (DeadLetterStatus)reader.GetInt32(10),
            RequeuedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            RequeuedRunId = reader.IsDBNull(12) ? null : reader.GetGuid(12),
            Notes = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    private static JobBatch MapBatch(NpgsqlDataReader reader)
    {
        return new JobBatch
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            ParentBatchId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Status = (BatchStatus)reader.GetInt32(3),
            TotalJobs = reader.GetInt32(4),
            CompletedJobs = reader.GetInt32(5),
            FailedJobs = reader.GetInt32(6),
            CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt = reader.GetDateTime(8),
            StartedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            CompletedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            ExpiresAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11)
        };
    }

    private static BatchContinuation MapBatchContinuation(NpgsqlDataReader reader)
    {
        return new BatchContinuation
        {
            Id = reader.GetGuid(0),
            BatchId = reader.GetGuid(1),
            TriggerType = reader.GetString(2),
            JobTypeId = reader.GetString(3),
            InputJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            Status = (ContinuationStatus)reader.GetInt32(5),
            ContinuationRunId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
            CreatedAt = reader.GetDateTime(7)
        };
    }

    private static void AddRunParameters(NpgsqlCommand cmd, JobRun run)
    {
        cmd.Parameters.AddWithValue("id", run.Id);
        cmd.Parameters.AddWithValue("jobTypeId", run.JobTypeId);
        cmd.Parameters.AddWithValue("status", (int)run.Status);
        cmd.Parameters.AddWithValue("triggerType", (int)run.TriggerType);
        cmd.Parameters.AddWithValue("triggeredBy", (object?)run.TriggeredBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("workerId", (object?)run.WorkerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("queue", run.Queue);
        cmd.Parameters.AddWithValue("createdAt", run.CreatedAt);
        cmd.Parameters.AddWithValue("scheduledAt", (object?)run.ScheduledAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("startedAt", (object?)run.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completedAt", (object?)run.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationMs", (object?)run.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("progress", run.Progress);
        cmd.Parameters.AddWithValue("total", run.Total);
        cmd.Parameters.AddWithValue("progressMessage", (object?)run.ProgressMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("itemsProcessed", run.ItemsProcessed);
        cmd.Parameters.AddWithValue("itemsSucceeded", run.ItemsSucceeded);
        cmd.Parameters.AddWithValue("itemsFailed", run.ItemsFailed);
        cmd.Parameters.AddWithValue("attemptNumber", run.AttemptNumber);
        cmd.Parameters.AddWithValue("nextRetryAt", (object?)run.NextRetryAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("errorMessage", (object?)run.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("stackTrace", (object?)run.StackTrace ?? DBNull.Value);
        cmd.Parameters.AddWithValue("errorType", (object?)run.ErrorType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("inputJson", (object?)run.InputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outputJson", (object?)run.OutputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadataJson", (object?)run.MetadataJson ?? DBNull.Value);
    }

    private static void AddDeadLetterParameters(NpgsqlCommand cmd, DeadLetterEntry entry)
    {
        cmd.Parameters.AddWithValue("id", entry.Id);
        cmd.Parameters.AddWithValue("originalRunId", entry.OriginalRunId);
        cmd.Parameters.AddWithValue("jobTypeId", entry.JobTypeId);
        cmd.Parameters.AddWithValue("queue", entry.Queue);
        cmd.Parameters.AddWithValue("inputJson", (object?)entry.InputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("errorMessage", entry.ErrorMessage);
        cmd.Parameters.AddWithValue("errorType", (object?)entry.ErrorType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("stackTrace", (object?)entry.StackTrace ?? DBNull.Value);
        cmd.Parameters.AddWithValue("attemptCount", entry.AttemptCount);
        cmd.Parameters.AddWithValue("movedAt", entry.MovedAt);
        cmd.Parameters.AddWithValue("status", (int)entry.Status);
        cmd.Parameters.AddWithValue("requeuedAt", (object?)entry.RequeuedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("requeuedRunId", (object?)entry.RequeuedRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)entry.Notes ?? DBNull.Value);
    }

    private static async Task<IReadOnlyList<JobRun>> ReadRunsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var runs = new List<JobRun>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            runs.Add(MapRun(reader));
        }
        return runs;
    }

    private static async Task<IReadOnlyList<JobHeartbeat>> ReadHeartbeatsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var heartbeats = new List<JobHeartbeat>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            heartbeats.Add(new JobHeartbeat
            {
                Id = reader.GetGuid(0),
                WorkerId = reader.GetString(1),
                Hostname = reader.GetString(2),
                ProcessId = reader.GetInt32(3),
                CurrentJobType = reader.IsDBNull(4) ? null : reader.GetString(4),
                CurrentRunId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
                Timestamp = reader.GetDateTime(6),
                Queues = reader.GetFieldValue<string[]>(7),
                JobsProcessed = reader.GetInt32(8),
                JobsFailed = reader.GetInt32(9),
                IsShuttingDown = reader.GetBoolean(10),
                StartedAt = reader.GetDateTime(11)
            });
        }
        return heartbeats;
    }
}
