-- ZapJobs Event History Schema
-- Migration: AddEvents
-- Enables complete audit trail, time-travel debugging, and event analytics

-- Events table - immutable append-only event log
CREATE TABLE IF NOT EXISTS zapjobs.events (
    id UUID PRIMARY KEY,
    run_id UUID NOT NULL REFERENCES zapjobs.runs(id) ON DELETE CASCADE,
    workflow_id UUID,
    event_type VARCHAR(255) NOT NULL,
    category VARCHAR(50) NOT NULL DEFAULT 'job',
    sequence_number BIGINT NOT NULL,
    run_sequence INTEGER NOT NULL DEFAULT 0,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload_json TEXT NOT NULL DEFAULT '{}',
    correlation_id VARCHAR(255),
    causation_id VARCHAR(255),
    actor VARCHAR(255),
    source VARCHAR(255),
    tags TEXT[],
    version INTEGER NOT NULL DEFAULT 1,
    duration_ms BIGINT,
    is_success BOOLEAN,
    metadata_json TEXT
);

-- Global sequence number (unique across all events)
CREATE UNIQUE INDEX IF NOT EXISTS idx_events_sequence
    ON zapjobs.events(sequence_number);

-- Primary query pattern: get events for a run ordered by sequence
CREATE INDEX IF NOT EXISTS idx_events_run_seq
    ON zapjobs.events(run_id, sequence_number ASC);

-- Query by event type within a run
CREATE INDEX IF NOT EXISTS idx_events_run_type
    ON zapjobs.events(run_id, event_type, sequence_number ASC);

-- Query by category within a run
CREATE INDEX IF NOT EXISTS idx_events_run_category
    ON zapjobs.events(run_id, category, sequence_number ASC);

-- Correlation ID for distributed tracing
CREATE INDEX IF NOT EXISTS idx_events_correlation
    ON zapjobs.events(correlation_id)
    WHERE correlation_id IS NOT NULL;

-- Causation ID for event chain tracking
CREATE INDEX IF NOT EXISTS idx_events_causation
    ON zapjobs.events(causation_id)
    WHERE causation_id IS NOT NULL;

-- Workflow ID for workflow-level queries
CREATE INDEX IF NOT EXISTS idx_events_workflow
    ON zapjobs.events(workflow_id, sequence_number ASC)
    WHERE workflow_id IS NOT NULL;

-- Time-based queries for analytics and cleanup
CREATE INDEX IF NOT EXISTS idx_events_timestamp
    ON zapjobs.events(timestamp DESC);

-- Event type aggregation (for analytics)
CREATE INDEX IF NOT EXISTS idx_events_type_timestamp
    ON zapjobs.events(event_type, timestamp DESC);

-- Category aggregation
CREATE INDEX IF NOT EXISTS idx_events_category_timestamp
    ON zapjobs.events(category, timestamp DESC);

-- Full-text search on payload (GIN index for LIKE queries)
-- Note: For advanced full-text search, consider adding tsvector column
CREATE INDEX IF NOT EXISTS idx_events_payload_gin
    ON zapjobs.events USING gin(payload_json gin_trgm_ops);

-- Tags array search
CREATE INDEX IF NOT EXISTS idx_events_tags
    ON zapjobs.events USING gin(tags)
    WHERE tags IS NOT NULL;

-- Actor-based queries (who triggered events)
CREATE INDEX IF NOT EXISTS idx_events_actor
    ON zapjobs.events(actor, timestamp DESC)
    WHERE actor IS NOT NULL;

-- Success/failure filtering for analytics
CREATE INDEX IF NOT EXISTS idx_events_success
    ON zapjobs.events(is_success, timestamp DESC)
    WHERE is_success IS NOT NULL;

-- Comments for documentation
COMMENT ON TABLE zapjobs.events IS 'Immutable event log for job execution audit trail and time-travel debugging';
COMMENT ON COLUMN zapjobs.events.run_id IS 'The job run this event belongs to';
COMMENT ON COLUMN zapjobs.events.workflow_id IS 'Optional workflow/saga ID for multi-job workflows';
COMMENT ON COLUMN zapjobs.events.event_type IS 'Event type (e.g., job.started, activity.completed, custom.order_processed)';
COMMENT ON COLUMN zapjobs.events.category IS 'Event category: job, activity, workflow, state, progress, system, custom';
COMMENT ON COLUMN zapjobs.events.sequence_number IS 'Global monotonically increasing sequence number';
COMMENT ON COLUMN zapjobs.events.run_sequence IS 'Per-run sequence number for ordering within a job';
COMMENT ON COLUMN zapjobs.events.timestamp IS 'When the event occurred';
COMMENT ON COLUMN zapjobs.events.payload_json IS 'Event-specific data as JSON';
COMMENT ON COLUMN zapjobs.events.correlation_id IS 'Distributed tracing correlation ID';
COMMENT ON COLUMN zapjobs.events.causation_id IS 'ID of the event that caused this event';
COMMENT ON COLUMN zapjobs.events.actor IS 'Who/what triggered this event (user, system, job)';
COMMENT ON COLUMN zapjobs.events.source IS 'Component that generated this event';
COMMENT ON COLUMN zapjobs.events.tags IS 'Searchable tags array';
COMMENT ON COLUMN zapjobs.events.version IS 'Schema version for payload evolution';
COMMENT ON COLUMN zapjobs.events.duration_ms IS 'Duration in milliseconds (for timing events)';
COMMENT ON COLUMN zapjobs.events.is_success IS 'Success indicator (for completion events)';
COMMENT ON COLUMN zapjobs.events.metadata_json IS 'Additional metadata not part of payload';

-- Enable pg_trgm extension for full-text search (run separately if needed)
-- CREATE EXTENSION IF NOT EXISTS pg_trgm;
