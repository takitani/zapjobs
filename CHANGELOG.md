# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Job Continuations** - Chain jobs to run after a parent completes
  - New `JobContinuation` entity with `ContinuationCondition` and `ContinuationStatus` enums
  - `IJobScheduler.ContinueWithAsync()` methods for creating continuations
  - Support for `OnSuccess`, `OnFailure`, and `Always` conditions
  - Option to pass parent job output as continuation input (`passParentOutput`)
  - Automatic continuation triggering in `JobExecutor` after job completion
  - PostgreSQL `zapjobs.continuations` table for persistence
  - InMemory storage support for testing
  - Comprehensive unit tests for all continuation functionality
