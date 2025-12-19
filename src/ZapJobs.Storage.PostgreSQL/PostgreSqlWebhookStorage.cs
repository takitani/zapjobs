using System.Text.RegularExpressions;
using Npgsql;
using ZapJobs.Core;

namespace ZapJobs.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of IWebhookStorage
/// </summary>
public class PostgreSqlWebhookStorage : IWebhookStorage
{
    private readonly string _connectionString;
    private readonly string _subscriptions;
    private readonly string _deliveries;

    public PostgreSqlWebhookStorage(string connectionString, string schema = "zapjobs")
    {
        _connectionString = connectionString;
        _subscriptions = $"{schema}.webhook_subscriptions";
        _deliveries = $"{schema}.webhook_deliveries";
    }

    public PostgreSqlWebhookStorage(PostgreSqlStorageOptions options)
        : this(options.ConnectionString, options.Schema)
    {
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    #region Subscriptions

    public async Task<Guid> CreateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_subscriptions}
                (id, name, url, events, job_type_filter, queue_filter, secret, headers_json,
                 is_enabled, last_success_at, last_failure_at, failure_count, created_at, updated_at)
            VALUES
                (@id, @name, @url, @events, @jobTypeFilter, @queueFilter, @secret, @headersJson,
                 @isEnabled, @lastSuccessAt, @lastFailureAt, @failureCount, @createdAt, @updatedAt)
            """, conn);

        cmd.Parameters.AddWithValue("id", subscription.Id);
        cmd.Parameters.AddWithValue("name", subscription.Name);
        cmd.Parameters.AddWithValue("url", subscription.Url);
        cmd.Parameters.AddWithValue("events", (int)subscription.Events);
        cmd.Parameters.AddWithValue("jobTypeFilter", (object?)subscription.JobTypeFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("queueFilter", (object?)subscription.QueueFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("secret", (object?)subscription.Secret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("headersJson", (object?)subscription.HeadersJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isEnabled", subscription.IsEnabled);
        cmd.Parameters.AddWithValue("lastSuccessAt", (object?)subscription.LastSuccessAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastFailureAt", (object?)subscription.LastFailureAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("failureCount", subscription.FailureCount);
        cmd.Parameters.AddWithValue("createdAt", subscription.CreatedAt);
        cmd.Parameters.AddWithValue("updatedAt", subscription.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
        return subscription.Id;
    }

    public async Task<WebhookSubscription?> GetSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, name, url, events, job_type_filter, queue_filter, secret, headers_json,
                   is_enabled, last_success_at, last_failure_at, failure_count, created_at, updated_at
            FROM {_subscriptions}
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapSubscription(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(bool enabledOnly = false, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, url, events, job_type_filter, queue_filter, secret, headers_json,
                   is_enabled, last_success_at, last_failure_at, failure_count, created_at, updated_at
            FROM {_subscriptions}
            """;

        if (enabledOnly)
        {
            sql += " WHERE is_enabled = TRUE";
        }

        sql += " ORDER BY created_at DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);

        var subscriptions = new List<WebhookSubscription>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            subscriptions.Add(MapSubscription(reader));
        }
        return subscriptions;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetMatchingSubscriptionsAsync(
        WebhookEventType eventType,
        string? jobTypeId,
        string? queue,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Get all enabled subscriptions that match the event type
        // Filtering by jobTypeFilter and queueFilter is done in memory (supports wildcards)
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, name, url, events, job_type_filter, queue_filter, secret, headers_json,
                   is_enabled, last_success_at, last_failure_at, failure_count, created_at, updated_at
            FROM {_subscriptions}
            WHERE is_enabled = TRUE
              AND (events & @eventType) = @eventType
            """, conn);

        cmd.Parameters.AddWithValue("eventType", (int)eventType);

        var subscriptions = new List<WebhookSubscription>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var subscription = MapSubscription(reader);

            // Apply wildcard filters in memory
            if (MatchesFilter(subscription.JobTypeFilter, jobTypeId) &&
                MatchesFilter(subscription.QueueFilter, queue))
            {
                subscriptions.Add(subscription);
            }
        }
        return subscriptions;
    }

    public async Task UpdateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_subscriptions}
            SET name = @name,
                url = @url,
                events = @events,
                job_type_filter = @jobTypeFilter,
                queue_filter = @queueFilter,
                secret = @secret,
                headers_json = @headersJson,
                is_enabled = @isEnabled,
                last_success_at = @lastSuccessAt,
                last_failure_at = @lastFailureAt,
                failure_count = @failureCount,
                updated_at = @updatedAt
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", subscription.Id);
        cmd.Parameters.AddWithValue("name", subscription.Name);
        cmd.Parameters.AddWithValue("url", subscription.Url);
        cmd.Parameters.AddWithValue("events", (int)subscription.Events);
        cmd.Parameters.AddWithValue("jobTypeFilter", (object?)subscription.JobTypeFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("queueFilter", (object?)subscription.QueueFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("secret", (object?)subscription.Secret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("headersJson", (object?)subscription.HeadersJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isEnabled", subscription.IsEnabled);
        cmd.Parameters.AddWithValue("lastSuccessAt", (object?)subscription.LastSuccessAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastFailureAt", (object?)subscription.LastFailureAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("failureCount", subscription.FailureCount);
        cmd.Parameters.AddWithValue("updatedAt", subscription.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Delete deliveries first (foreign key constraint)
        await using var deleteDeliveries = new NpgsqlCommand(
            $"DELETE FROM {_deliveries} WHERE subscription_id = @id", conn);
        deleteDeliveries.Parameters.AddWithValue("id", id);
        await deleteDeliveries.ExecuteNonQueryAsync(ct);

        // Delete subscription
        await using var deleteSubscription = new NpgsqlCommand(
            $"DELETE FROM {_subscriptions} WHERE id = @id", conn);
        deleteSubscription.Parameters.AddWithValue("id", id);
        await deleteSubscription.ExecuteNonQueryAsync(ct);
    }

    #endregion

    #region Deliveries

    public async Task<Guid> CreateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO {_deliveries}
                (id, subscription_id, event, payload_json, status_code, response_body, error_message,
                 attempt_number, success, duration_ms, created_at, next_retry_at)
            VALUES
                (@id, @subscriptionId, @event, @payloadJson, @statusCode, @responseBody, @errorMessage,
                 @attemptNumber, @success, @durationMs, @createdAt, @nextRetryAt)
            """, conn);

        cmd.Parameters.AddWithValue("id", delivery.Id);
        cmd.Parameters.AddWithValue("subscriptionId", delivery.SubscriptionId);
        cmd.Parameters.AddWithValue("event", (int)delivery.Event);
        cmd.Parameters.AddWithValue("payloadJson", delivery.PayloadJson);
        cmd.Parameters.AddWithValue("statusCode", delivery.StatusCode);
        cmd.Parameters.AddWithValue("responseBody", (object?)delivery.ResponseBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("errorMessage", (object?)delivery.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("attemptNumber", delivery.AttemptNumber);
        cmd.Parameters.AddWithValue("success", delivery.Success);
        cmd.Parameters.AddWithValue("durationMs", delivery.DurationMs);
        cmd.Parameters.AddWithValue("createdAt", delivery.CreatedAt);
        cmd.Parameters.AddWithValue("nextRetryAt", (object?)delivery.NextRetryAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        return delivery.Id;
    }

    public async Task<WebhookDelivery?> GetDeliveryAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, subscription_id, event, payload_json, status_code, response_body, error_message,
                   attempt_number, success, duration_ms, created_at, next_retry_at
            FROM {_deliveries}
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapDelivery(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(Guid subscriptionId, int limit = 50, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, subscription_id, event, payload_json, status_code, response_body, error_message,
                   attempt_number, success, duration_ms, created_at, next_retry_at
            FROM {_deliveries}
            WHERE subscription_id = @subscriptionId
            ORDER BY created_at DESC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("subscriptionId", subscriptionId);
        cmd.Parameters.AddWithValue("limit", limit);

        var deliveries = new List<WebhookDelivery>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            deliveries.Add(MapDelivery(reader));
        }
        return deliveries;
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetPendingDeliveriesAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT id, subscription_id, event, payload_json, status_code, response_body, error_message,
                   attempt_number, success, duration_ms, created_at, next_retry_at
            FROM {_deliveries}
            WHERE success = FALSE
              AND next_retry_at IS NOT NULL
              AND next_retry_at <= @now
            ORDER BY next_retry_at ASC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("limit", limit);

        var deliveries = new List<WebhookDelivery>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            deliveries.Add(MapDelivery(reader));
        }
        return deliveries;
    }

    public async Task UpdateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            UPDATE {_deliveries}
            SET status_code = @statusCode,
                response_body = @responseBody,
                error_message = @errorMessage,
                attempt_number = @attemptNumber,
                success = @success,
                duration_ms = @durationMs,
                next_retry_at = @nextRetryAt
            WHERE id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", delivery.Id);
        cmd.Parameters.AddWithValue("statusCode", delivery.StatusCode);
        cmd.Parameters.AddWithValue("responseBody", (object?)delivery.ResponseBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("errorMessage", (object?)delivery.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("attemptNumber", delivery.AttemptNumber);
        cmd.Parameters.AddWithValue("success", delivery.Success);
        cmd.Parameters.AddWithValue("durationMs", delivery.DurationMs);
        cmd.Parameters.AddWithValue("nextRetryAt", (object?)delivery.NextRetryAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteOldDeliveriesAsync(DateTime olderThan, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {_deliveries} WHERE created_at < @olderThan", conn);
        cmd.Parameters.AddWithValue("olderThan", olderThan);

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    #endregion

    #region Mappers

    private static WebhookSubscription MapSubscription(NpgsqlDataReader reader)
    {
        return new WebhookSubscription
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Url = reader.GetString(2),
            Events = (WebhookEventType)reader.GetInt32(3),
            JobTypeFilter = reader.IsDBNull(4) ? null : reader.GetString(4),
            QueueFilter = reader.IsDBNull(5) ? null : reader.GetString(5),
            Secret = reader.IsDBNull(6) ? null : reader.GetString(6),
            HeadersJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            IsEnabled = reader.GetBoolean(8),
            LastSuccessAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            LastFailureAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            FailureCount = reader.GetInt32(11),
            CreatedAt = reader.GetDateTime(12),
            UpdatedAt = reader.GetDateTime(13)
        };
    }

    private static WebhookDelivery MapDelivery(NpgsqlDataReader reader)
    {
        return new WebhookDelivery
        {
            Id = reader.GetGuid(0),
            SubscriptionId = reader.GetGuid(1),
            Event = (WebhookEventType)reader.GetInt32(2),
            PayloadJson = reader.GetString(3),
            StatusCode = reader.GetInt32(4),
            ResponseBody = reader.IsDBNull(5) ? null : reader.GetString(5),
            ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
            AttemptNumber = reader.GetInt32(7),
            Success = reader.GetBoolean(8),
            DurationMs = reader.GetInt32(9),
            CreatedAt = reader.GetDateTime(10),
            NextRetryAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11)
        };
    }

    private static bool MatchesFilter(string? filter, string? value)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (string.IsNullOrEmpty(value))
            return false;

        var pattern = "^" + Regex.Escape(filter).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
    }

    #endregion
}
