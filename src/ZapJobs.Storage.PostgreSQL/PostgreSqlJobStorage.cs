using System.Text.Json;
using Npgsql;
using ZapJobs.Core;

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
                   max_concurrency, last_run_at, next_run_at, last_run_status, config_json,
                   created_at, updated_at
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
                   max_concurrency, last_run_at, next_run_at, last_run_status, config_json,
                   created_at, updated_at
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
                max_concurrency, last_run_at, next_run_at, last_run_status, config_json,
                created_at, updated_at
            ) VALUES (
                @jobTypeId, @displayName, @description, @queue, @scheduleType, @intervalMinutes,
                @cronExpression, @timeZoneId, @isEnabled, @maxRetries, @timeoutSeconds,
                @maxConcurrency, @lastRunAt, @nextRunAt, @lastRunStatus, @configJson,
                @createdAt, @updatedAt
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
                   max_concurrency, last_run_at, next_run_at, last_run_status, config_json,
                   created_at, updated_at
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
                (SELECT COUNT(*) FROM {_logs}) as total_logs
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
                TotalLogEntries: reader.GetInt64(7)
            );
        }

        return new JobStorageStats(0, 0, 0, 0, 0, 0, 0, 0);
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
            LastRunAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            NextRunAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            LastRunStatus = reader.IsDBNull(14) ? null : (JobRunStatus)reader.GetInt32(14),
            ConfigJson = reader.IsDBNull(15) ? null : reader.GetString(15),
            CreatedAt = reader.GetDateTime(16),
            UpdatedAt = reader.GetDateTime(17)
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
