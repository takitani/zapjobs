-- ZapJobs Batch Jobs Schema
-- Migration: AddBatches

-- Job Batches table
CREATE TABLE IF NOT EXISTS zapjobs.batches (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    parent_batch_id UUID REFERENCES zapjobs.batches(id),
    status INTEGER NOT NULL DEFAULT 0,
    total_jobs INTEGER NOT NULL DEFAULT 0,
    completed_jobs INTEGER NOT NULL DEFAULT 0,
    failed_jobs INTEGER NOT NULL DEFAULT 0,
    created_by VARCHAR(255),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    started_at TIMESTAMP,
    completed_at TIMESTAMP,
    expires_at TIMESTAMP
);

-- Index for nested batches query
CREATE INDEX IF NOT EXISTS idx_batches_parent
    ON zapjobs.batches(parent_batch_id)
    WHERE parent_batch_id IS NOT NULL;

-- Index for status queries
CREATE INDEX IF NOT EXISTS idx_batches_status
    ON zapjobs.batches(status);

-- Index for cleanup queries
CREATE INDEX IF NOT EXISTS idx_batches_expires
    ON zapjobs.batches(expires_at)
    WHERE expires_at IS NOT NULL;

-- Batch Jobs link table
CREATE TABLE IF NOT EXISTS zapjobs.batch_jobs (
    batch_id UUID NOT NULL REFERENCES zapjobs.batches(id) ON DELETE CASCADE,
    run_id UUID NOT NULL REFERENCES zapjobs.runs(id) ON DELETE CASCADE,
    order_num INTEGER NOT NULL,
    PRIMARY KEY (batch_id, run_id)
);

-- Index for getting jobs by batch
CREATE INDEX IF NOT EXISTS idx_batch_jobs_batch
    ON zapjobs.batch_jobs(batch_id, order_num);

-- Batch Continuations table
CREATE TABLE IF NOT EXISTS zapjobs.batch_continuations (
    id UUID PRIMARY KEY,
    batch_id UUID NOT NULL REFERENCES zapjobs.batches(id) ON DELETE CASCADE,
    trigger_type VARCHAR(50) NOT NULL, -- 'success', 'failure', 'complete'
    job_type_id VARCHAR(255) NOT NULL,
    input_json TEXT,
    status INTEGER NOT NULL DEFAULT 0,
    continuation_run_id UUID,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Index for finding continuations by batch
CREATE INDEX IF NOT EXISTS idx_batch_continuations_batch
    ON zapjobs.batch_continuations(batch_id)
    WHERE status = 0;

-- Add batch_id column to runs if not exists
ALTER TABLE zapjobs.runs ADD COLUMN IF NOT EXISTS batch_id UUID;

-- Index for finding runs by batch
CREATE INDEX IF NOT EXISTS idx_runs_batch
    ON zapjobs.runs(batch_id)
    WHERE batch_id IS NOT NULL;

-- Comments
COMMENT ON TABLE zapjobs.batches IS 'Job batch groupings for atomic batch operations';
COMMENT ON TABLE zapjobs.batch_jobs IS 'Links job runs to their parent batch';
COMMENT ON TABLE zapjobs.batch_continuations IS 'Batch-level continuations triggered on batch completion';
