## 1. Identification

- Task/spec: SYSCOM synchronization stage timing and run metrics persistence.
- Agent and skills: metrics (/root/metrics); dev-delivery.
- Repository: /Users/blanks88/Workspace/Alusa/IDOneExtensions-sync-performance
- Change, plan and task: 261030-002-FEATURE-rendimiento-sincronizacion-syscom; main; metricas.
- Plan: [PLAN.md](../../../PLAN.md), metrics task.
- Dependencies: pipeline passes one shared `SyscomSyncMetrics` per run and persists it after pipeline tasks stop.
- Delivery file: `.itia/changelog/261030-002-FEATURE-rendimiento-sincronizacion-syscom/plans/main/development/metrics/metricas/DELIVERY.md`.
- Previous delivery: Not applicable — first delivery.
- Delivered revision: feature/syscom-sync-performance based on da1f198; implementation remains uncommitted in shared worktree.
- Implementation commits: None; lead owns staging and commits.
- Remaining uncommitted scope: assigned implementation, tests, and reports pending integrated verification and lead commit.
- Verification time: 2026-10-30 14:03:49 -06:00 (America/Mexico_City).

## 2. Result

- State: READY_FOR_QA.
- Problem addressed: Prior SYSCOM runs recorded counts and outcome but no elapsed time by synchronization stage.
- Delivered behavior: Run timing scopes use a monotonic clock and accumulate repeated calls safely across threads. The database worker times staging, catalog promotion, global price recalculation, and full promotion, then can save a JSON snapshot on the run without changing its state.
- Remaining scope or deviations: None in the assigned metrics implementation. Pipeline wiring belongs to the pipeline task; independent QA remains pending.

## 3. Changes

| File/component | Change and reason |
| --- | --- |
| `Syscom/Syscom/Load/SyscomSyncMetrics.cs` | Added stage enum, injectable monotonic clock, thread-safe scope accounting, total elapsed time, and JSON snapshot. |
| `Syscom/Syscom/Load/SyscomDatabaseWorker.cs` | Added optional metrics dependency, scoped timers, additive `metrics jsonb` migration, and state-preserving `RecordMetricsAsync`. Existing SQL pricing logic and transaction order remain intact. |
| `Syscom/SyscomTestProject/SyscomMetricsTests.cs` | Added deterministic timing tests and a disposable PostgreSQL legacy schema upgrade/persistence test. |

- PR draft: Not requested.

## 4. Acceptance criteria

| ID | Expected behavior | State | Remaining gap |
| --- | --- | --- | --- |
| CA-01 | Monotonic, thread-safe, additive timings for all six stages and total elapsed time; disposal counts failed work. | PASS | None; deterministic metrics tests passed. |
| CA-02 | Database staging and promotion sub-stages measured without changing price rules, rollback, or atomicity. | PASS | None; existing pricing and rollback regressions passed. |
| CA-03 | Existing run tables upgrade additively and persist JSON metrics on success/failure without state changes. | PASS | None; legacy schema/failure persistence and combined success path passed. |
| CA-04 | Worker can share a metrics instance and save after tasks stop with bounded best effort. | PASS | None; integrated pipeline tests passed; pipeline agent owns wiring. |

## 5. Verification

- Tests added: `Measure_UsesMonotonicClockAndAccumulatesRepeatedStageCalls` covers monotonic additive timing and JSON; `Measure_NestedAndOverlappingScopesAccumulateIndependentlyAndDisposeOnce` covers nested scopes and idempotent disposal; `Measure_ConcurrentScopesDisposeWithoutLostTime` covers concurrent disposal; `Measure_ExceptionStillRecordsElapsedStageTime` covers failed work; `RecordMetrics_UpgradesLegacyRunTableAndPreservesFailedState` covers PostgreSQL migration and failed-run snapshot persistence.
- Existing tests modified: None.
- Checks performed: `git diff --check` passed for tracked assigned changes; scoped source review found the global candidate validation, delete and insert statements unchanged. Lead's final integrated Release run passed 81 tests and skipped 4, including all metrics, pricing, rollback and pipeline tests.
- Checks not run or failed: Independent QA pending. This agent did not run the shared build independently.

## 6. Pending work and QA review

- Failures, blockers and risks: None identified in the final integrated Release run; independent review remains pending.
- Required action and owner: Lead performs independent review and the combined commit.
- Reproduction setup: `SYSCOM_TEST_DB_CONNECTION` targets a disposable PostgreSQL 17 `idone_sync_tests` database on localhost; run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~SyscomMetricsTests` from repository root. No provider credentials or live SYSCOM call are needed.
- Public contracts, configuration and data schema: Optional third `SyscomDatabaseWorker` constructor argument accepts `SyscomSyncMetrics`; `RecordMetricsAsync(Guid,CancellationToken)` writes a snapshot to nullable `syscom_sync_runs.metrics jsonb`. `BeginRunAsync` adds the column if missing. JSON keys are `total_elapsed_ms`, `preparation_ms`, `download_ms`, `staging_ms`, `catalog_promotion_ms`, `price_recalculation_ms`, and `promotion_ms`. Stage times can overlap and cannot be summed to get total elapsed time.
- QA environment and setup: .NET 10 test project, disposable PostgreSQL 17, `PostgresSyncFixture`; no role or external account required.
- QA cleanup: `PostgresSyncFixture` drops its isolated test schema on disposal.

| QA case | Criteria / risk | Preconditions and exact input | Steps or exact command | Expected observable result |
| --- | --- | --- | --- | --- |
| QA-01 | CA-01 | No DB; manual clock advances 12, 3, and 7 ms across two Download scopes. | Run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~SyscomMetricsTests`. | Download is 19 ms, total is 22 ms, and JSON has matching keys. |
| QA-02 | CA-01 | No DB; overlapping Staging scopes and nested Promotion/CatalogPromotion scopes. | Run the same filtered test command. | Concurrent and nested durations accumulate independently, double disposal does not count twice, and exception disposal retains 17 ms. |
| QA-03 | CA-03 | Local disposable DB; fixture creates a legacy `syscom_sync_runs` table without `metrics`. | Set `SYSCOM_TEST_DB_CONNECTION` to the fixture database, then run the filtered test command. | `BeginRunAsync` adds `metrics jsonb`; a failed run stores Download=14 ms while status remains Failed and stage remains Downloading. |
| QA-04 | CA-02 | Local disposable DB with existing SYSCOM persistence fixtures. | Run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~SyscomPersistenceTests`. | Global percentage lists for all products are recalculated, fixed list remains, and price-insert failure rolls back products and prices. |
| QA-05 | CA-04 | Combined pipeline revision and local disposable DB. | Run the full `SyscomTestProject` suite with `SYSCOM_TEST_DB_CONNECTION` set. | Successful and failed runs record JSON metrics; original failure/cancellation remains observable and no promotion occurs for incomplete download. |

- QA coverage exclusions: Live provider and production DB excluded by the plan.
- Independent review: PENDING.
