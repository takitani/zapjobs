-- ZapJobs PostgreSQL Schema
-- Run this script to create the required tables

-- Job Definitions table
CREATE TABLE IF NOT EXISTS zapjobs_definitions (
    job_type_id VARCHAR(255) PRIMARY KEY,
    display_name VARCHAR(255) NOT NULL,
    description TEXT,
    queue VARCHAR(100) NOT NULL DEFAULT 'default',
    schedule_type INTEGER NOT NULL DEFAULT 0,
    interval_minutes INTEGER,
    cron_expression VARCHAR(100),
    time_zone_id VARCHAR(100),
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    max_retries INTEGER NOT NULL DEFAULT 3,
    timeout_seconds INTEGER NOT NULL DEFAULT 3600,
    max_concurrency INTEGER NOT NULL DEFAULT 1,
    last_run_at TIMESTAMP,
    next_run_at TIMESTAMP,
    last_run_status INTEGER,
    config_json TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Create index for due jobs query
CREATE INDEX IF NOT EXISTS idx_definitions_next_run
    ON zapjobs_definitions(next_run_at)
    WHERE is_enabled = TRUE;

-- Job Runs table
CREATE TABLE IF NOT EXISTS zapjobs_runs (
    id UUID PRIMARY KEY,
    job_type_id VARCHAR(255) NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    trigger_type INTEGER NOT NULL DEFAULT 0,
    triggered_by VARCHAR(255),
    worker_id VARCHAR(255),
    queue VARCHAR(100) NOT NULL DEFAULT 'default',
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    scheduled_at TIMESTAMP,
    started_at TIMESTAMP,
    completed_at TIMESTAMP,
    duration_ms INTEGER,
    progress INTEGER NOT NULL DEFAULT 0,
    total INTEGER NOT NULL DEFAULT 0,
    progress_message TEXT,
    items_processed INTEGER NOT NULL DEFAULT 0,
    items_succeeded INTEGER NOT NULL DEFAULT 0,
    items_failed INTEGER NOT NULL DEFAULT 0,
    attempt_number INTEGER NOT NULL DEFAULT 1,
    next_retry_at TIMESTAMP,
    error_message TEXT,
    stack_trace TEXT,
    error_type VARCHAR(500),
    input_json TEXT,
    output_json TEXT,
    metadata_json TEXT
);

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_runs_status ON zapjobs_runs(status);
CREATE INDEX IF NOT EXISTS idx_runs_job_type ON zapjobs_runs(job_type_id);
CREATE INDEX IF NOT EXISTS idx_runs_created_at ON zapjobs_runs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_runs_completed_at ON zapjobs_runs(completed_at) WHERE completed_at IS NOT NULL;

-- Index for pending jobs query (status + queue)
CREATE INDEX IF NOT EXISTS idx_runs_pending
    ON zapjobs_runs(queue, created_at)
    WHERE status = 0;

-- Index for retry jobs query
CREATE INDEX IF NOT EXISTS idx_runs_retry
    ON zapjobs_runs(next_retry_at)
    WHERE status = 5;

-- Job Logs table
CREATE TABLE IF NOT EXISTS zapjobs_logs (
    id UUID PRIMARY KEY,
    run_id UUID NOT NULL,
    level INTEGER NOT NULL DEFAULT 2,
    message TEXT NOT NULL,
    category VARCHAR(100),
    context_json TEXT,
    duration_ms INTEGER,
    exception TEXT,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for logs
CREATE INDEX IF NOT EXISTS idx_logs_run_id ON zapjobs_logs(run_id);
CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON zapjobs_logs(timestamp DESC);

-- Worker Heartbeats table
CREATE TABLE IF NOT EXISTS zapjobs_heartbeats (
    id UUID,
    worker_id VARCHAR(255) PRIMARY KEY,
    hostname VARCHAR(255) NOT NULL,
    process_id INTEGER NOT NULL,
    current_job_type VARCHAR(255),
    current_run_id UUID,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW(),
    queues TEXT[] NOT NULL DEFAULT ARRAY['default'],
    jobs_processed INTEGER NOT NULL DEFAULT 0,
    jobs_failed INTEGER NOT NULL DEFAULT 0,
    is_shutting_down BOOLEAN NOT NULL DEFAULT FALSE,
    started_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Index for active workers query
CREATE INDEX IF NOT EXISTS idx_heartbeats_timestamp ON zapjobs_heartbeats(timestamp DESC);

-- Add foreign key constraints (optional, can be disabled for performance)
-- ALTER TABLE zapjobs_runs ADD CONSTRAINT fk_runs_definition
--     FOREIGN KEY (job_type_id) REFERENCES zapjobs_definitions(job_type_id) ON DELETE CASCADE;

-- ALTER TABLE zapjobs_logs ADD CONSTRAINT fk_logs_run
--     FOREIGN KEY (run_id) REFERENCES zapjobs_runs(id) ON DELETE CASCADE;

-- Comments
COMMENT ON TABLE zapjobs_definitions IS 'Job type configurations and schedules';
COMMENT ON TABLE zapjobs_runs IS 'Individual job executions';
COMMENT ON TABLE zapjobs_logs IS 'Execution logs for job runs';
COMMENT ON TABLE zapjobs_heartbeats IS 'Worker health check heartbeats';
