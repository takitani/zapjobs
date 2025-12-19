-- ZapJobs Checkpoints Schema
-- Migration: AddCheckpoints
-- Enables durable checkpoint/resume for long-running jobs

-- Checkpoints table
CREATE TABLE IF NOT EXISTS zapjobs.checkpoints (
    id UUID PRIMARY KEY,
    run_id UUID NOT NULL REFERENCES zapjobs.runs(id) ON DELETE CASCADE,
    key VARCHAR(255) NOT NULL,
    data_json TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    sequence_number INTEGER NOT NULL,
    data_size_bytes INTEGER NOT NULL,
    is_compressed BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ,
    metadata_json TEXT
);

-- Index for getting latest checkpoint by run + key
CREATE INDEX IF NOT EXISTS idx_checkpoints_run_key
    ON zapjobs.checkpoints(run_id, key, sequence_number DESC);

-- Index for getting all checkpoints for a run
CREATE INDEX IF NOT EXISTS idx_checkpoints_run
    ON zapjobs.checkpoints(run_id, sequence_number DESC);

-- Index for expired checkpoint cleanup
CREATE INDEX IF NOT EXISTS idx_checkpoints_expires
    ON zapjobs.checkpoints(expires_at)
    WHERE expires_at IS NOT NULL;

-- Unique constraint for run + sequence (no duplicate sequences)
CREATE UNIQUE INDEX IF NOT EXISTS idx_checkpoints_run_seq
    ON zapjobs.checkpoints(run_id, sequence_number);

-- Comments
COMMENT ON TABLE zapjobs.checkpoints IS 'Stores checkpoint state for job resumption after failures';
COMMENT ON COLUMN zapjobs.checkpoints.key IS 'Checkpoint identifier (e.g., progress, cursor, batch-state)';
COMMENT ON COLUMN zapjobs.checkpoints.data_json IS 'Serialized checkpoint data (may be compressed if is_compressed=true)';
COMMENT ON COLUMN zapjobs.checkpoints.version IS 'Schema version for checkpoint data migrations';
COMMENT ON COLUMN zapjobs.checkpoints.sequence_number IS 'Auto-incrementing sequence per run for ordering';
COMMENT ON COLUMN zapjobs.checkpoints.is_compressed IS 'Whether data_json is GZip compressed and base64 encoded';
COMMENT ON COLUMN zapjobs.checkpoints.expires_at IS 'When this checkpoint expires (null = never)';
COMMENT ON COLUMN zapjobs.checkpoints.metadata_json IS 'Additional metadata (job type, attempt, progress)';
