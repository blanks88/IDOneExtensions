# SYSCOM persistence, pagination and transaction correction

## 1. Identification

- Task/spec: Plan tasks `aislamiento`, `carga-paginada`, and `promocion-precios`; correction of independent review findings on `74c3e57`.
- Agent and skills: persistence; `dev-delivery`.
- Repository: `/Users/blanks88/Workspace/Alusa/IDOneExtensions-sync-improvements`.
- Change, plan and task: `261030-001-FEATURE-sincronizacion-syscom` / `main` / `sync-persistence`.
- Plan: [main plan](../../../PLAN.md).
- Dependencies: HTTP recovery and host exit-code contributions are owned separately; disposable local PostgreSQL provided by lead.
- Delivery file: `.itia/changelog/261030-001-FEATURE-sincronizacion-syscom/plans/main/development/persistence/sync-persistence/DELIVERY.md`.
- Previous delivery: [pre-review delivery](history/0001-DELIVERY.md).
- Delivered revision: `feature/syscom-sync-hardening` at `74c3e57` plus uncommitted corrective changes.
- Implementation commits: `74c3e57` contains initial persistence implementation; lead owns corrective commit.
- Remaining uncommitted scope: `IDOneRepository/IUnitOfWork.cs`, `Syscom/Syscom/Load/SyscomDatabaseWorker.cs`, `SyscomWorker.cs`, `Syscom/SyscomTestProject/SyscomPersistenceTests.cs`, and this contribution's reports.
- Verification time: 2026-10-30 11:14 -06:00 (America/Mexico_City).

## 2. Result

- State: READY_FOR_QA for assigned corrective scope; independent rereview is pending.
- Problem addressed: The first implementation could promote a truncated multi-page category, used synchronous commit despite a cancellation token, and issued a lookup plus upsert for every product.
- Delivered behavior: The worker checks nonempty advertised pages and compares received products with a positive declared `cantidad`, while accepting an explicit zero-product catalogue. UoW commit and rollback are asynchronous and token aware; each page is deduplicated in memory and staged in 500-row JSON recordset batches, with the distinct staging count calculated once after download.
- Remaining scope or deviations: `cantidad=0` on a nonempty response is treated as absent for backward compatibility because the response model defaults to zero. A nonempty but truncated page sequence with no positive total cannot be detected solely from the current response metadata. No product deactivation is performed.

## 3. Changes

| File/component | Change and reason |
| --- | --- |
| `Syscom/Syscom/Load/SyscomWorker.cs` | Tracks received count per category; rejects empty advertised pages, changed positive totals, and a final count below or above a positive total before promotion; accepts explicit empty catalogue. |
| `IDOneRepository/IUnitOfWork.cs` | Uses `CommitAsync`, `RollbackAsync` and async disposal with the passed token. A token cancelled before commit leaves the transaction available for rollback. |
| `Syscom/Syscom/Load/SyscomDatabaseWorker.cs` | Deduplicates each page by code, detects collisions within page and against staged rows, chooses a deterministic winner using all stored product fields, and executes one collision query plus one JSON recordset upsert per 500-row batch. Moves `products_staged` count to download completion. |
| `Syscom/SyscomTestProject/SyscomPersistenceTests.cs` | Adds PostgreSQL regressions for incomplete pagination, explicit empty catalogue, 801-row batch crossing, within-page collision, and cancelled-before-commit rollback. |

- PR draft: Not requested.

## 4. Acceptance criteria

| ID | Expected behavior | State | Remaining gap |
| --- | --- | --- | --- |
| CA-01 | Multi-page categories cannot promote after an empty page, changed positive total, or received count short of a positive declared total. An explicit zero-product catalogue remains valid. | PASS | Provider responses with no positive total cannot prove completeness of nonempty pages. |
| CA-02 | Cancellation immediately before commit stops commit and leaves a transaction that can be rolled back without a sentinel write. Rollback uses its token asynchronously. | PASS | Independent QA pending. |
| CA-03 | A page is persisted in bounded batches with deterministic duplicate selection, collision detection within/across pages, and correct page/run counters. | PASS | Exact statement-count instrumentation was not added; 801-row regression crosses the 500-row boundary. |
| CA-04 | Existing isolation, global percentage-price scope and atomic promotion remain intact. | PASS | Independent rerun of the full simulation is lead/reviewer work. |

## 5. Verification

- Tests added in this correction: `IncompletePagination_FailsWithoutPromoting` (three inputs: empty final page, short declared total, changed declared total); `ExplicitEmptyCatalogue_CompletesWithoutStagedProducts` (zero catalogue); `LargePage_StagesAcrossBoundedBatchesAndCountsOnce` (801 rows crossing chunk boundary); `NormalizedCodeCollision_WithinPageRollsBackEntirePage` (same-page truncation collision); `CancelledBeforeCommit_RemainsRollbackableAndLeavesNoSentinel` (real PostgreSQL cancellation before commit).
- Existing tests modified: None in this correction. Existing persistence and logging tests were rerun unchanged.
- Checks performed: Test-project Debug build passed with no errors. Focused disposable PostgreSQL persistence and logging run passed 21/21 after all corrective source edits. A broader filtered run including simulator tests passed 20 tests with 4 simulator tests skipped before the last same-page collision test was added. `git diff --check` passed for assigned source files.
- Checks not run or failed: Full Release suite, active Postman local simulation, live vendor and production DB were not run by this contribution. Independent rereview on the corrective revision is pending.

## 6. Pending work and QA review

- Failures, blockers and risks: When SYSCOM omits a positive total, a nonempty but short final page is indistinguishable from a complete one. The batch uses one page as its memory boundary and 500 rows as its SQL transfer boundary; page size still depends on vendor behavior. The original schema and runtime DDL considerations in the archived delivery remain relevant.
- Required action and owner: Lead runs integrated and Release checks, commits corrections, and requests independent rereview. Operations reviews schema privileges before deployment. Product owner defines any later deactivation rule.
- Reproduction setup: PostgreSQL 17 disposable local `idone_sync_tests` database; set `SYSCOM_TEST_DB_CONNECTION='Host=127.0.0.1;Port=32768;Database=idone_sync_tests;Username=postgres'` with the actual local port/user. The fixture rejects non-loopback and other database names, creates `sync_<guid>` schema, seeds company 2 `OTHER-2`, currencies/rates, percentage lists 10/11 and fixed list 12, then drops its own schema. From repository root run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter 'FullyQualifiedName~SyscomPersistenceTests|FullyQualifiedName~SyscomWorkerLoggingTests'`. No vendor credentials are needed.
- Public contracts, configuration and data schema: `IUnitOfWork` method signatures remain unchanged; commit/rollback now honor cancellation and run asynchronously. `SyscomWorker` constructor and run signature remain unchanged. No new table or column is introduced by this correction; staging still uses run-scoped tables from `74c3e57`.
- QA environment and setup: .NET 10 and isolated PostgreSQL 17; disposable local DB privileges; no external account. Tests use `PostgresFact`/`PostgresTheory` opt-in and the nonparallel `Postgres sync` collection.
- QA cleanup: Fixture drops only its generated `sync_<guid>` schema. If interrupted, inspect `sync_` schemas in disposable `idone_sync_tests` and remove only confirmed orphan fixture schemas.

| QA case | Criteria / risk | Preconditions and exact input | Steps or exact command | Expected observable result |
| --- | --- | --- | --- | --- |
| QA-01 | CA-01 | One category: page 1 of 2 has product A and `cantidad=2`; page 2 has no products. | Run `IncompletePagination_FailsWithoutPromoting` with `empty-final`. | Run is Failed after one staged page; A is absent from portfolio. |
| QA-02 | CA-01 | Page 1 of 2 has A, page 2 has B, both declare `cantidad=3`; repeat with page 2 declaring 3 after page 1 declared 2. | Run `IncompletePagination_FailsWithoutPromoting` with `short-total` and `changed-total`. | Short case fails after two staged pages; changed case fails before page 2 staging; neither promotes A/B. |
| QA-03 | CA-01 | One category returns page 1, `paginas=0`, `cantidad=0`, `productos=[]`. | Run `ExplicitEmptyCatalogue_CompletesWithoutStagedProducts`. | Run Succeeded with one page loaded and zero staged products. |
| QA-04 | CA-02 | Disposable DB; insert `CANCELLED-COMMIT` inside UoW transaction; cancel token before commit. | Run `CancelledBeforeCommit_RemainsRollbackableAndLeavesNoSentinel`. | Commit throws cancellation; explicit rollback succeeds; sentinel count is zero. |
| QA-05 | CA-03 | One page with 801 unique products, plus same-page models differing only after 35 normalized characters. | Run `LargePage_StagesAcrossBoundedBatchesAndCountsOnce` and `NormalizedCodeCollision_WithinPageRollsBackEntirePage`. | Large page stages 801 rows with one page and 801 seen/staged; collision leaves zero rows and zero loaded pages. |
| QA-06 | CA-04 | Disposable fixture with company 2 product, lists 10/11 percentage and list 12 fixed; missing FX and price-trigger variants. | Run `Promotion_ReplacesGlobalPercentagePrices_AndPreservesFixedList`, `MissingExchangeRate_RollsBackProductsAndPrices`, and `PriceInsertFailure_RollsBackDeletedPricesAndProductChanges`. | Global eligible prices include company 2, fixed price remains, and failures preserve all previous production rows. |

- QA coverage exclusions: No provider response without `cantidad` can prove completeness of nonempty pages; no live vendor pacing, scheduler deployment or production database privilege validation in this contribution.
- Independent review: PENDING on corrective revision; prior review on `74c3e57` failed and prompted this correction.
