-- ZapJobs Webhooks Schema
-- Migration: AddWebhooks

-- Webhook Subscriptions table
CREATE TABLE IF NOT EXISTS zapjobs.webhook_subscriptions (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    url VARCHAR(2048) NOT NULL,
    events INTEGER NOT NULL DEFAULT 0,
    job_type_filter VARCHAR(255),
    queue_filter VARCHAR(255),
    secret VARCHAR(255),
    headers_json TEXT,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    last_success_at TIMESTAMP,
    last_failure_at TIMESTAMP,
    failure_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Index for enabled subscriptions
CREATE INDEX IF NOT EXISTS idx_webhook_subscriptions_enabled
    ON zapjobs.webhook_subscriptions(is_enabled)
    WHERE is_enabled = TRUE;

-- Index for event type matching
CREATE INDEX IF NOT EXISTS idx_webhook_subscriptions_events
    ON zapjobs.webhook_subscriptions(events)
    WHERE is_enabled = TRUE;

-- Webhook Deliveries table
CREATE TABLE IF NOT EXISTS zapjobs.webhook_deliveries (
    id UUID PRIMARY KEY,
    subscription_id UUID NOT NULL REFERENCES zapjobs.webhook_subscriptions(id) ON DELETE CASCADE,
    event INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    status_code INTEGER NOT NULL DEFAULT 0,
    response_body TEXT,
    error_message TEXT,
    attempt_number INTEGER NOT NULL DEFAULT 1,
    success BOOLEAN NOT NULL DEFAULT FALSE,
    duration_ms INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    next_retry_at TIMESTAMP
);

-- Index for pending retries
CREATE INDEX IF NOT EXISTS idx_webhook_deliveries_pending
    ON zapjobs.webhook_deliveries(next_retry_at)
    WHERE success = FALSE AND next_retry_at IS NOT NULL;

-- Index for deliveries by subscription
CREATE INDEX IF NOT EXISTS idx_webhook_deliveries_subscription
    ON zapjobs.webhook_deliveries(subscription_id, created_at DESC);

-- Index for cleanup queries
CREATE INDEX IF NOT EXISTS idx_webhook_deliveries_created
    ON zapjobs.webhook_deliveries(created_at);

-- Comments
COMMENT ON TABLE zapjobs.webhook_subscriptions IS 'Webhook subscription configurations for event notifications';
COMMENT ON TABLE zapjobs.webhook_deliveries IS 'Webhook delivery attempts and retry tracking';
COMMENT ON COLUMN zapjobs.webhook_subscriptions.events IS 'Bitmask of WebhookEventType flags';
COMMENT ON COLUMN zapjobs.webhook_subscriptions.job_type_filter IS 'Wildcard pattern for job type filtering (e.g., send-*)';
COMMENT ON COLUMN zapjobs.webhook_subscriptions.queue_filter IS 'Wildcard pattern for queue filtering';
COMMENT ON COLUMN zapjobs.webhook_subscriptions.secret IS 'Secret key for HMAC-SHA256 signature';
COMMENT ON COLUMN zapjobs.webhook_subscriptions.headers_json IS 'Custom HTTP headers as JSON object';
