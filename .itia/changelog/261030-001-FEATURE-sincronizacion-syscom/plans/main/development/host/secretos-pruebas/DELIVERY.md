# Development delivery

## 1. Identification

- Task/spec: Plan section 5, credential externalization and safe test isolation; host operational settings from sections 1 and 3.
- Agent and skills: host; dev-delivery.
- Repository: /Users/blanks88/Workspace/Alusa/IDOneExtensions-sync-improvements
- Change, plan and task: 261030-001-FEATURE-sincronizacion-syscom / main / secretos-pruebas
- Plan: [PLAN.md](../../../PLAN.md), task secretos-pruebas
- Dependencies: Persistence contribution provides SyscomSyncOptions and concurrent-run exception; HTTP contribution provides configurable client options.
- Delivery file: .itia/changelog/261030-001-FEATURE-sincronizacion-syscom/plans/main/development/host/secretos-pruebas/DELIVERY.md
- Previous delivery: Not applicable; first delivery.
- Delivered revision: feature/syscom-sync-hardening at 638e297 plus uncommitted shared-worktree changes.
- Implementation commits: None; coordinating lead owns commits.
- Remaining uncommitted scope: Program.cs, SyscomAppConfiguration.cs, environment.example, SyscomWorkerTests.cs, SyscomConfigurationTests.cs and SyscomTestProject.csproj under Syscom.
- Verification time: 2026-10-30 11:00 -0600, America/Mexico_City.

## 2. Result

- State: INCOMPLETE.
- Problem addressed: The console and database-writing test embedded credentials. A default test run could call the real vendor and write to a database.
- Delivered behavior: The console requires three environment secrets, validates operational options and exits before connecting when configuration is missing. The live worker test is skipped by default and uses separate integration variables, an explicit run flag and isolated-database acknowledgment when intentionally enabled.
- Remaining scope or deviations: Executor provisioning, credential rotation and isolated live validation remain pending. Final integrated test run awaits the settled shared worktree.

## 3. Changes

| File/component | Change and reason |
| --- | --- |
| Syscom/SyscomApp/Program.cs | Removed embedded credentials; mapped environment configuration to client and worker; preserved cancellation event drain and added Unix SIGTERM handling; dispose UoW asynchronously; return 0 success, 1 failure, 2 configuration error, 3 cancellation, 4 concurrent run. Error output names exception types only; HTTP redirects are disabled. |
| Syscom/SyscomApp/SyscomAppConfiguration.cs | Parses required secrets and bounded duration, retention, company, request timeout and OAuth options. Accepts production HTTPS or loopback simulator URL; errors identify variable names, not values. |
| Syscom/SyscomApp/environment.example | Documents placeholders, optional settings and live-test gates. The application does not load this file. |
| Syscom/SyscomTestProject/SyscomWorkerTests.cs | Converts destructive test to discovery-time opt-in skip, uses exclusive integration variables and isolated-database acknowledgment, and sanitizes provider failure output. |
| Syscom/SyscomTestProject/SyscomConfigurationTests.cs | Adds isolated parser coverage with fixture values. |
| Syscom/SyscomTestProject/SyscomTestProject.csproj | References the console project to test the actual configuration parser without launching the process. |

- PR draft: Not requested.

## 4. Acceptance criteria

| ID | Expected behavior | State | Remaining gap |
| --- | --- | --- | --- |
| CA-01 | No embedded live credentials in owned host or worker-test source; configuration comes from environment. | Implemented; inspected | Rotating credentials in all external consumers is pending. |
| CA-02 | Missing/invalid configuration fails before network or DB access, naming variables but never their values. | Focused tests passed; startup checked | Final post-edit suite and independent QA pending. |
| CA-03 | Default dotnet test does not synchronize live data; an intentional run requires separate integration values and isolated database. | Focused run reported live test skipped | No authorized isolated DB supplied for opt-in run. |
| CA-04 | Host applies bounded request/run timeouts, retention and company settings, with scheduler-readable outcome codes. | Build passed; parser tests passed | Integrated behavior and scheduler interpretation unverified. |
| CA-05 | Ctrl+C and Unix SIGTERM cancel active work while shutdown callbacks drain and resources are disposed. | Code inspected | Signal-driven runtime validation pending. |

## 5. Verification

- Tests added: Parse_ReportsOnlyMissingVariableNames checks safe required-variable errors; Parse_RejectsUnsafeBaseUrl rejects plaintext remote and unauthorized hosts; Parse_AcceptsLoopbackSimulatorAndOperationalSettings checks explicit options; Parse_RejectsInvalidSettingsByName checks malformed and out-of-range settings.
- Existing tests modified: RunWorker_SyncsProducts now uses SyscomIntegrationFactAttribute discovery skip and integration-only configuration, with no embedded secrets.
- Checks performed: Focused test run passed 12 cases and skipped 1 live test before the final redirect-handler and SIGTERM edits; no-env console invocation exited 2 and printed only the three missing variable names; SyscomApp build succeeded with 0 warnings and 0 errors before those edits; git diff --check passed. Filename-only credential scan found no matching embedded credential pattern in owned source.
- Checks not run or failed: Final test project suite after settled persistence changes, integration test, signal-driven cancellation, authorized provider/database connectivity, scheduler injection and independent QA. A transient shared-worktree build failure occurred while persistence source and tests were being edited; coordinating lead owns final suite. Artifact handoff checker could not run because no trusted assignment/baseline was supplied.

## 6. Pending work and QA review

- Failures, blockers and risks: External executors may still lack required variables. Removing literals from the current files does not remove them from Git history or rotate exposed secrets. An enabled integration test can modify the specified database.
- Required action and owner: Deployment owner inventories scheduler, manual, container and CI consumers; configures secret manager values, verifies presence without connecting, then performs authorized read-only connectivity and coordinated rotation. Coordinating lead runs settled suite and assigns independent QA.
- Reproduction setup: .NET SDK 10.0.401 on macOS; worktree at the delivered branch. Use no live environment values for default checks. For an authorized live test, provision a disposable isolated PostgreSQL database and SYSCOM test account, set SYSCOM_RUN_INTEGRATION_TESTS=1, SYSCOM_INTEGRATION_DATABASE_ISOLATED=1 and all SYSCOM_INTEGRATION_* required names in environment.example.
- Public contracts, configuration and data schema: Application now requires CONNECTION_STRING, SYSCOM_CLIENT_ID and SYSCOM_CLIENT_SECRET. Optional SYSCOM_BASE_URL permits only developers.syscom.mx over HTTPS or loopback; SYSCOM_OAUTH_TOKEN_PATH and SYSCOM_OAUTH_FORM_ENCODED retain legacy defaults; SYSCOM_REQUEST_TIMEOUT_SECONDS, SYSCOM_MAXIMUM_DURATION_SECONDS, SYSCOM_RETENTION_DAYS and SYSCOM_COMPANY_ID have validated defaults. Exit codes are 0/1/2/3/4 as above. No host-owned data schema change.
- QA environment and setup: Run commands below against this checkout with .NET 10.0. Use fixture values for parser tests; no credentials or customer data required. Live case requires separately authorized isolated DB, seeded schema and vendor test account.
- QA cleanup: Parser/default test cases create no remote data. For an authorized live case, reset or dispose the isolated DB according to the test owner’s fixture procedure.

| QA case | Criteria / risk | Preconditions and exact input | Steps or exact command | Expected observable result |
| --- | --- | --- | --- | --- |
| QA-01 | CA-02, CA-03 | Required app variables unset; live run flag unset | Run `env -u CONNECTION_STRING -u SYSCOM_CLIENT_ID -u SYSCOM_CLIENT_SECRET dotnet run --project Syscom/SyscomApp/SyscomApp.csproj`; capture exit code and stderr. | Exit 2; only required variable names appear; no provider or database request. |
| QA-02 | CA-02, CA-04 | No live variables; fixture parser inputs from SyscomConfigurationTests | Run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~SyscomConfigurationTests`; capture summary. | All parser tests pass, including bounded options and URL rejection; no network or DB writes. |
| QA-03 | CA-03 | SYSCOM_RUN_INTEGRATION_TESTS unset | Run `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~RunWorker_SyncsProducts`; capture summary. | Test is reported skipped; no vendor or DB operation. |
| QA-04 | CA-01 | Current owned files, no need for secret values | Inspect Program.cs and SyscomWorkerTests.cs for credential literals; review git diff against 638e297 without echoing removed values. | No embedded live credential remains in current owned files; example contains placeholders only. |
| QA-05 | CA-03, CA-04 | Explicitly authorized isolated DB and vendor test account; all integration variables present | Run the opt-in worker test with the isolated flag and integration variables; capture result and run status in isolated DB. | Test completes or fails with safe error type; no production DB is touched; scheduler behavior and count expectations are validated against the fixture. |
| QA-06 | CA-05 | Authorized disposable fixture with a running sync on Linux/macOS | Start the console; send Ctrl+C during one run and SIGTERM during a second run; observe exit codes and run status, then repeat with a completed run. | Cancellation exits 3 after callbacks drain and persistence records cancellation; success exits 0; no disposed-token exception or secret output. |

- QA coverage exclusions: QA-05 and QA-06 are pending authorized fixtures; scheduler deployment and credential rotation require operations outside this source contribution.
- Independent review: PENDING.

## Verificación integrada posterior por el lead

La integración final compila en Release sin warnings y pasa 40 pruebas con 4 pruebas
contra proveedor real omitidas. Incluye PostgreSQL 17 y simulación HTTP Postman local.
Este resultado sustituye los pendientes de compilación/suite de la entrega parcial.
La revisión independiente y la activación remota permanecen separadas.
