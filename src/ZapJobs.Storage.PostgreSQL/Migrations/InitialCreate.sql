-- ZapJobs PostgreSQL Schema
-- Creates a dedicated 'zapjobs' schema with clean table names

-- Create schema
CREATE SCHEMA IF NOT EXISTS zapjobs;

-- Job Definitions table
CREATE TABLE IF NOT EXISTS zapjobs.definitions (
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
    ON zapjobs.definitions(next_run_at)
    WHERE is_enabled = TRUE;

-- Job Runs table
CREATE TABLE IF NOT EXISTS zapjobs.runs (
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
CREATE INDEX IF NOT EXISTS idx_runs_status ON zapjobs.runs(status);
CREATE INDEX IF NOT EXISTS idx_runs_job_type ON zapjobs.runs(job_type_id);
CREATE INDEX IF NOT EXISTS idx_runs_created_at ON zapjobs.runs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_runs_completed_at ON zapjobs.runs(completed_at) WHERE completed_at IS NOT NULL;

-- Index for pending jobs query (status + queue)
CREATE INDEX IF NOT EXISTS idx_runs_pending
    ON zapjobs.runs(queue, created_at)
    WHERE status = 0;

-- Index for retry jobs query
CREATE INDEX IF NOT EXISTS idx_runs_retry
    ON zapjobs.runs(next_retry_at)
    WHERE status = 5;

-- Job Logs table
CREATE TABLE IF NOT EXISTS zapjobs.logs (
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
CREATE INDEX IF NOT EXISTS idx_logs_run_id ON zapjobs.logs(run_id);
CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON zapjobs.logs(timestamp DESC);

-- Worker Heartbeats table
CREATE TABLE IF NOT EXISTS zapjobs.heartbeats (
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
CREATE INDEX IF NOT EXISTS idx_heartbeats_timestamp ON zapjobs.heartbeats(timestamp DESC);

-- Job Continuations table
CREATE TABLE IF NOT EXISTS zapjobs.continuations (
    id UUID PRIMARY KEY,
    parent_run_id UUID NOT NULL,
    continuation_job_type_id VARCHAR(255) NOT NULL,
    condition INTEGER NOT NULL DEFAULT 0,
    input_json TEXT,
    pass_parent_output BOOLEAN NOT NULL DEFAULT FALSE,
    queue VARCHAR(100),
    status INTEGER NOT NULL DEFAULT 0,
    continuation_run_id UUID,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Index for finding continuations by parent run
CREATE INDEX IF NOT EXISTS idx_continuations_parent
    ON zapjobs.continuations(parent_run_id)
    WHERE status = 0;

-- Comments
COMMENT ON SCHEMA zapjobs IS 'ZapJobs background job processing tables';
COMMENT ON TABLE zapjobs.definitions IS 'Job type configurations and schedules';
COMMENT ON TABLE zapjobs.runs IS 'Individual job executions';
COMMENT ON TABLE zapjobs.logs IS 'Execution logs for job runs';
COMMENT ON TABLE zapjobs.heartbeats IS 'Worker health check heartbeats';
COMMENT ON TABLE zapjobs.continuations IS 'Job continuation chains';
