-- ZapJobs Rate Limiting Schema
-- Migration: AddRateLimiting

-- Rate Limit Executions table
CREATE TABLE IF NOT EXISTS zapjobs.rate_limit_executions (
    id UUID PRIMARY KEY,
    key VARCHAR(255) NOT NULL,
    executed_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Index for counting executions within a time window
CREATE INDEX IF NOT EXISTS idx_rate_limit_key_time
    ON zapjobs.rate_limit_executions(key, executed_at DESC);

-- Index for cleanup of old records
CREATE INDEX IF NOT EXISTS idx_rate_limit_cleanup
    ON zapjobs.rate_limit_executions(executed_at);

-- Comments
COMMENT ON TABLE zapjobs.rate_limit_executions IS 'Tracks job executions for rate limiting';
