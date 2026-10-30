# SYSCOM persistence and worker hardening

## 1. Identification

- Task/spec: Plan tasks `aislamiento`, `carga-paginada`, and `promocion-precios`; worker result portion of `recuperacion-http`.
- Agent and skills: persistence; `dev-delivery`.
- Repository: `/Users/blanks88/Workspace/Alusa/IDOneExtensions-sync-improvements`.
- Change, plan and task: `261030-001-FEATURE-sincronizacion-syscom` / `main` / `sync-persistence`.
- Plan: [main plan](../../../../PLAN.md).
- Dependencies: HTTP recovery and host exit-code contributions are owned by other agents; disposable local PostgreSQL provided by lead.
- Delivery file: `.itia/changelog/261030-001-FEATURE-sincronizacion-syscom/plans/main/development/persistence/sync-persistence/DELIVERY.md`.
- Previous delivery: Not applicable — first delivery.
- Delivered revision: `feature/syscom-sync-hardening`, uncommitted shared worktree.
- Implementation commits: None; lead owns integration commit.
- Remaining uncommitted scope: `Syscom/Syscom/Load/SyscomWorker.cs`, `SyscomDatabaseWorker.cs`, `SyscomSyncOptions.cs`, `Syscom/SyscomTestProject/SyscomPersistenceTests.cs`, `SyscomWorkerLoggingTests.cs`, and this contribution's reports.
- Verification time: 2026-10-30 11:03 -06:00 (America/Mexico_City).

## 2. Result

- State: READY_FOR_QA for assigned persistence and worker scope; independent QA remains pending.
- Problem addressed: The former sync accumulated each category in memory, reused one unscoped staging table, and replaced global percentage prices without validating the complete new set or an explicit promotion transaction.
- Delivered behavior: Each run now holds a PostgreSQL session advisory lock, stages one page at a time under its own run ID, records outcome and missing products, and promotes only after complete download. SAT, units, products and global percentage prices commit together after candidate validation; a failure preserves prior production rows.
- Remaining scope or deviations: No product deactivation rule was defined, so missing products are recorded without deletion. HTTP retries and scheduler exit codes are separate contributions. Schema creation occurs at runtime and needs deployment privileges or prior DBA application.

## 3. Changes

| File/component | Change and reason |
| --- | --- |
| `Syscom/Syscom/Load/SyscomWorker.cs` | Iterates category pages with bounded memory, validates page progress, applies a run deadline, records failures/cancellation, and logs exception type without exception details. |
| `Syscom/Syscom/Load/SyscomDatabaseWorker.cs` | Uses UoW's open PostgreSQL session for advisory lock; creates run-scoped history/staging/missing tables without reading legacy staging; writes each page transactionally; resolves duplicates deterministically and rejects normalized/truncated collisions; marks orphan runs; retains the latest successful baseline per company; promotes SAT, units, products and validated global prices in one transaction. |
| `Syscom/Syscom/Load/SyscomSyncOptions.cs` | Exposes `MaximumDuration`, `RetentionDays`, and `CompanyId` defaults while preserving the existing three-argument worker constructor. |
| `Syscom/SyscomTestProject/SyscomPersistenceTests.cs` | Adds a local-only, disposable PostgreSQL schema fixture with another company's product, percentage and fixed lists, and 11 persistence/worker tests. |
| `Syscom/SyscomTestProject/SyscomWorkerLoggingTests.cs` | Updates the three outcome cases for the real DB lifecycle and checks sensitive provider text is absent from the final log. |

- PR draft: Not requested.

## 4. Acceptance criteria

| ID | Expected behavior | State | Remaining gap |
| --- | --- | --- | --- |
| CA-01 | Concurrent runs cannot write; staging and history are scoped by run, and incomplete runs cannot promote. | PASS | Independent QA pending. |
| CA-02 | Pages are staged individually with bounded memory; repeated pages fail; duplicate winner is deterministic and code collisions fail. | PASS | Peak memory and vendor timing still need measurement in an approved environment. |
| CA-03 | Outcome, stage and counters are recorded; cancellation and maximum duration stop work without promotion; logs omit sensitive exception details. | PASS | Scheduler exit-code validation belongs to host contribution. |
| CA-04 | SAT, units, products and every eligible product/list percentage price replace atomically, including other companies; invalid FX/null candidates abort without data loss. | PASS | QA should exercise the production-like rate cardinality and schema. |
| CA-05 | Missing products are compared with the previous successful run for the company and recorded without deletion; retention preserves that baseline. | PASS | Commercial deactivation rule remains undefined by design. |

## 5. Verification

- Tests added: `Promotion_ReplacesGlobalPercentagePrices_AndPreservesFixedList` (global and fixed-list scope); `MissingExchangeRate_RollsBackProductsAndPrices` and `PriceInsertFailure_RollsBackDeletedPricesAndProductChanges` (atomic failure); `NormalizedCodeCollision_RejectsSecondPage` and `Duplicates_AreChosenByCategoryThenStableProductFields` (identity rules); `AdvisoryLock_RejectsConcurrentRun` (exclusion); `IncompleteRun_CannotPromote_AndNextRunDoesNotReuseStaging` (run isolation/orphans); `SuccessfulRuns_RecordMissingProductsWithoutDeletingThem` and `Retention_PreservesLastSuccessfulBaseline` (missing and retention); `RepeatedPage_FailsRunWithoutPromotion` (pagination); `Cancellation_RecordsCancelledAndLeavesProductionUntouched` (cancellation).
- Existing tests modified: `RunAsync_LogsActualOutcomeWithoutExceptionDetails` in `SyscomWorkerLoggingTests` now uses the disposable PostgreSQL fixture for success, failure and cancellation.
- Checks performed: Library and test-project Debug builds passed during implementation. Focused persistence test run passed 10/10 before final review edits. Lead's integrated test-project run on settled shared source passed 40 tests with 4 live tests skipped, including the 11 persistence tests; `git diff --check` passed for assigned tracked files.
- Checks not run or failed: Release build and independent QA are pending lead/reviewer execution. No live SYSCOM request, existing customer database write, deployment or timing/memory benchmark was performed by this contribution.

## 6. Pending work and QA review

- Failures, blockers and risks: Runtime DDL requires create/alter privileges or a DBA rollout. Duplicate principal exchange rates, nonpositive rates, unsupported foreign-to-foreign conversion, null candidate prices and candidate-count mismatch fail closed. The legacy `syscom_products_staging` table remains unused; no automatic deletion or migration is attempted.
- Required action and owner: Lead integrates and commits; DBA/operations reviews schema rollout; independent reviewer executes QA; product owner defines any later deactivation rule.
- Reproduction setup: PostgreSQL 17 disposable local `idone_sync_tests` database; set `SYSCOM_TEST_DB_CONNECTION='Host=127.0.0.1;Port=32768;Database=idone_sync_tests;Username=postgres'` with a locally provided port/user. The fixture rejects non-loopback and other database names, creates `sync_<guid>` schema, seeds company 2 `OTHER-2`, currencies and rates, percentage lists 10/11 and fixed list 12, then drops its own schema. Run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter 'FullyQualifiedName~SyscomPersistenceTests|FullyQualifiedName~SyscomWorkerLoggingTests'` from the repository root. No vendor credentials are needed for these cases.
- Public contracts, configuration and data schema: Existing `SyscomWorker(loggerFactory, client, uow)` and `RunAsync(CancellationToken)` remain valid; optional `SyscomSyncOptions` adds 30-minute maximum duration, 30-day retention, company 1 defaults. New `syscom_sync_runs`, `syscom_sync_staging`, `syscom_sync_missing` tables are created on first run, with legacy staging left untouched. Before deployment, review required permissions, apply or precreate the schema, and plan rollback by restoring the prior binary; retain new history/staging for diagnosis.
- QA environment and setup: .NET 10 and isolated PostgreSQL 17; no external account or role beyond disposable local DB privileges. Tests use `PostgresFact`/`PostgresTheory` opt-in and a nonparallel `Postgres sync` collection.
- QA cleanup: Fixture disposes and drops only its generated `sync_<guid>` schema. If interrupted, manually inspect `sync_` schemas in the disposable `idone_sync_tests` DB and remove only confirmed orphan fixture schemas.

| QA case | Criteria / risk | Preconditions and exact input | Steps or exact command | Expected observable result |
| --- | --- | --- | --- | --- |
| QA-01 | CA-01 | Disposable DB; first run stages `OLD-PARTIAL`; second stages `NEW-COMPLETE`. | Run `AdvisoryLock_RejectsConcurrentRun` and `IncompleteRun_CannotPromote_AndNextRunDoesNotReuseStaging`. | Concurrent begin throws `SyscomSyncAlreadyRunningException`; incomplete run cannot promote, becomes Failed/Interrupted after lock reacquisition, and only second run's product enters production. |
| QA-02 | CA-02 | Disposable DB; duplicate `DUPLICATE` across categories; distinct models sharing the first 35 normalized characters; page 2 repeats page 1. | Run `Duplicates_AreChosenByCategoryThenStableProductFields`, `NormalizedCodeCollision_RejectsSecondPage`, and `RepeatedPage_FailsRunWithoutPromotion`. | Category 1 wins regardless of arrival order; collision and repeated page throw; repeated-page run is Failed with one page recorded and no product promotion. |
| QA-03 | CA-03 | Disposable DB; mocked provider success, exception text `sensitive provider payload`, and token cancellation. | Run `RunAsync_LogsActualOutcomeWithoutExceptionDetails` and `Cancellation_RecordsCancelledAndLeavesProductionUntouched`. | Final log level/message reflects success, failure or cancellation without sensitive text; cancelled run is recorded and sentinel price remains. |
| QA-04 | CA-04 | Disposable DB fixture company 2 `OTHER-2`, percentage lists 10/11, fixed list 12; missing rate and failing price trigger variants. | Run `Promotion_ReplacesGlobalPercentagePrices_AndPreservesFixedList`, `MissingExchangeRate_RollsBackProductsAndPrices`, and `PriceInsertFailure_RollsBackDeletedPricesAndProductChanges`. | Two products yield four global percentage prices; fixed-list sentinel stays; both failures leave original prices and prevent new product promotion. |
| QA-05 | CA-05 | First success has `PRESENT` and `MISSING`; next success has `PRESENT`; old success is aged two days with one-day retention. | Run `SuccessfulRuns_RecordMissingProductsWithoutDeletingThem` and `Retention_PreservesLastSuccessfulBaseline`. | `MISSING` appears in run's missing table and remains in portfolio; latest successful baseline survives retention and supports comparison. |

- QA coverage exclusions: No live vendor pacing, scheduler behavior, production database privileges or end-to-end deployment validation in this contribution; these require separate authorized environments.
- Independent review: PENDING.

## Verificación integrada posterior por el lead

La integración final compila en Release sin warnings y pasa 40 pruebas con 4 pruebas
contra proveedor real omitidas. Incluye PostgreSQL 17 y simulación HTTP Postman local.
Este resultado sustituye los pendientes de compilación/suite de la entrega parcial.
La revisión independiente y la activación remota permanecen separadas.
