## 1. Identification

- Task: investigate SYSCOM failures, correct the cause and update production.
- Agent: developer (Codex); alusa-dev, syscom-sync-to-completion, dev-debugging, dev-dotnet, git-commit and dev-delivery skills.
- Repository: IDOneExtensions; change 261110-001-HOTFIX-timeout-promocion-syscom / main / timeout.
- Plan: [main](../../../PLAN.md). First delivery; no dependencies.
- Implementation revision: `2c56ef9465051b7f34fa86fe20b195541b93fa79`, branch `feature/syscom-heroku-release`.
- Final reporting is recorded in a separate documentation commit; tested source remains at the implementation revision. No intentionally uncommitted scope.
- Verification time: 2026-11-10 15:40:32 -0600 (America/Mexico_City).

## 2. Result

- State: READY_FOR_QA; bounded independent verification and final source review both PASS.
- Stale staging statistics estimated one row for the new download, selecting a quadratic nested-loop comparison of missing models and timing out during promotion.
- Download completion now refreshes staging statistics before promotion. The production comparison changed to a hash anti join; the complete corrected synchronization succeeded on Heroku release `v5`.
- Production reconciliation was performed by the lead, separately from independent local verification. The next scheduled run has not yet been observed.

## 3. Changes

| Component | Change and reason |
| --- | --- |
| `SyscomDatabaseWorker.CompleteDownloadAsync` | Analyze staging before marking the download complete so promotion sees the new run's data distribution. |
| `SyscomStagingStatisticsTests` | Regression with two catalogs at production scale, autoanalysis disabled, cardinality estimate and promotion/integrity checks. |
| Operational documentation and changelog | Explain the failure mechanism and verified recovery. |

No API, schema, package, price-rule, identity-rule, scheduler or timeout change. No PR requested; no GitHub push performed.

## 4. Acceptance criteria

| ID | Expected behavior | State | Remaining gap |
| --- | --- | --- | --- |
| CA-01 | Reproduce and fix the promotion failure with a regression | PASS | None; independent RED replay and GREEN verification passed. |
| CA-02 | Deploy and complete a real synchronization | PASS | None; release v5, durable Succeeded/Complete and successful process exit. |
| CA-03 | Preserve product identity and fixed-price rows | PASS | None; before/after protected identity and exact fixed-row fingerprints matched. |
| CA-04 | Reconcile promoted catalog and all global percentage prices | PASS | None; catalog, values, cardinality and uniqueness checks passed. |

## 5. Verification

- Added test: `CompletedDownload_PlansNewRunAtCatalogScale_AndPromotesMissingProducts`; verifies that the completed new run has a catalog-scale planner estimate and that promotion records only missing models while preserving fixed prices. Existing tests were not changed.
- Build and local unit/integration suite: PASS. Existing provider-dependent and Postman simulation cases were skipped because their separate test environments were not configured; the real provider path was exercised by the production run.
- Independent functional verification: PASS for the bounded implementation; affected persistence tests and RED replay against the original production file succeeded.
- Independent final source review: PASS, no actionable findings; reviewed code unchanged afterward.
- Real production execution and read-only reconciliation: PASS for staging, catalog, all percentage prices, existing identities, fixed lists and absence of active runs.
- No HTTP contract or EF entity/migration changes; dedicated architecture/model-diff suites are not applicable to this SQL-statistics-only change. Existing suite built the affected projects.
- Documentation lookup: PostgreSQL 15 ANALYZE guidance via Context7; Npgsql 10.0.3 command timeout confirmed from installed XML documentation because Context7 had no version-specific 10.x entry.
- Limit: independent agents verified local behavior/source; production reconciliation was lead-owned. No claim of an observed scheduled execution after deployment.

## 6. Pending work and QA review

- No remaining implementation or recovery action. Next scheduled execution remains an observation for normal operations.
- Public contracts and data schema unchanged. Reverting the image restores the old code; data/statistics do not require a schema rollback. Reverting would reintroduce the stale-statistics risk.
- Local setup: .NET 10, localhost PostgreSQL database `idone_sync_tests`, `SYSCOM_TEST_DB_CONNECTION` pointing only there. Fixture creates and drops its own random schema. Local validation used PostgreSQL 18.6; actual production execution used PostgreSQL 15.19.
- Never rerun the historical real-provider test against an arbitrary database. A new production sync requires authorization and a fresh protected-data baseline.

| QA case | Criterion/risk | Input and steps | Expected result |
| --- | --- | --- | --- |
| QA-01 | CA-01 | Run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~SyscomStagingStatisticsTests`; replay in scratch with only the worker file restored from `ffd365b`. | Fixed revision passes; original fails the cardinality assertion. |
| QA-02 | CA-01, CA-03 | Run the existing persistence, full-model identity, collision, eligibility, measurement and nullable-currency tests against the disposable local database. | All affected tests pass; rollback and preservation remain intact. |
| QA-03 | CA-02 | Read Heroku release and run status plus `syscom_sync_runs` for `a1620128-d844-43d9-b4a6-b826435499d2`. | v5 corrected image, successful exit, Succeeded/Complete, no active run. |
| QA-04 | CA-03, CA-04 | Read the target run's staging and join catalog fields; recompute eligible percentage prices using configured currencies/rates; compare protected identity and fixed-price fingerprints captured before execution. | No mismatches, missing or unexpected prices, duplicate pairs or changes to protected identity/fixed rows. Lead completed the before/after comparison during recovery; a new execution needs a new baseline. |

Independent review: PASS for the bounded source change and local behavior. Production acceptance: lead-verified. No retained diagnostic attachments.
