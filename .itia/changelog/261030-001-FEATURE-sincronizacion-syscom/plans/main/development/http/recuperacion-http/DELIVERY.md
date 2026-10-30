## 1. Identification

- Task/spec: `recuperacion-http`, section 3 of the approved SYSCOM synchronization plan.
- Agent and skills: HTTP developer; `itia-software-engineering:dev-delivery`.
- Repository: `IDOneExtensions-sync-improvements` worktree.
- Change, plan and task: `261030-001-FEATURE-sincronizacion-syscom`; `main`; `recuperacion-http`.
- Plan: [PLAN.md](../../../PLAN.md), section 3.
- Dependencies: Host-level run cancellation/status handled by the host contribution; isolated test configuration handled by the host contribution.
- Delivery file: `.itia/changelog/261030-001-FEATURE-sincronizacion-syscom/plans/main/development/http/recuperacion-http/DELIVERY.md`.
- Previous delivery: Not applicable — first delivery.
- Delivered revision: `feature/syscom-sync-hardening`, base `638e297`, uncommitted contribution.
- Implementation commits: None; shared checkout commit is coordinated by the lead.
- Remaining uncommitted scope: `SyscomClient.cs`, `SyscomClientOptions.cs`, `SyscomClientTests.cs`, and new `SyscomHttpResilienceTests.cs`.
- Verification time: 2026-10-30 11:00:14 -06:00 (America/Mexico_City).

## 2. Result

- State: INCOMPLETE.
- Problem addressed: GET calls previously had no bounded recovery and error exceptions included response bodies. The access token was neither tracked by expiry nor refreshed after 401.
- Delivered behavior: The client renews a token before known expiry, renews once after a GET 401, and retries transient GET failures within attempt, request-timeout, delay, and total-duration limits. Token POST remains single-attempt; exceptions and client logs omit response content and credentials.
- Remaining scope or deviations: Final test rerun is pending because a concurrent persistence-test edit temporarily breaks compilation. Host integration, full run-level status and scheduler exit codes are separate contributions. No live SYSCOM endpoint was contacted.

## 3. Changes

| File/component | Change and reason |
| --- | --- |
| `Syscom/Syscom/SyscomClient.cs` | Centralized GET execution, token state and renewal lock, safe failures, transient retry with Retry-After and jitter, cancellation and time budgets. |
| `Syscom/Syscom/SyscomClientOptions.cs` | Added tunable request/GET budgets and token skew; made BaseUrl and OAuthTokenPath configurable and added form-encoded token request switch while preserving legacy defaults. |
| `Syscom/SyscomTestProject/SyscomClientTests.cs` | Removed embedded client credentials and token output from skipped integration fixtures. |
| `Syscom/SyscomTestProject/SyscomHttpResilienceTests.cs` | Added isolated HTTP-handler cases without external requests. |

- PR draft: Not requested.

## 4. Acceptance criteria

| ID | Expected behavior | State | Remaining gap |
| --- | --- | --- | --- |
| CA-01 | Save expiry and renew token before known expiry; renew/replay a GET once on 401. | PASS at prior tested revision | Final rerun after last logger assertion and concurrent build recovery. |
| CA-02 | Retry only transient GET outcomes with bounded attempts, backoff, jitter and Retry-After; permanent failures and token POST are not retried. | NOT_RUN on final revision | Retry-After minimum changed after the prior passing run; final rerun pending. |
| CA-03 | Bound each request and total GET operation; honor caller cancellation. | NOT_RUN on final revision | Retry-After wait changed after the prior passing run; final rerun pending. Host controls full synchronization duration separately. |
| CA-04 | Never expose token, credentials or response body in client logs or exceptions. | NOT_RUN on final revision | Final logger assertion added after prior passing run; final rerun pending. |
| CA-05 | Preserve existing public methods and support both legacy JSON and configured form-encoded token request. | PASS at prior tested revision | Current documented token path/content type have not been tested against live SYSCOM. |

## 5. Verification

- Tests added: `ExpiringTokenIsRenewedBeforeNextGet`, `UnauthorizedRenewsAndReplaysOnlyOnce`, `UnauthorizedRefreshCanRecover`, `TransientStatusesRetryWithFiniteAttemptsAndRetryAfter`, `PermanentFailureDoesNotRetryOrExposeResponseBody`, `NetworkFailureRetriesOnlyConfiguredNumberOfTimes`, `RequestTimeoutRetriesAndCallerCancellationStopsImmediately`, `RetryAfterIsCappedByGetDuration`, `InvalidJsonNeverExposesResponseContent`, and two cases of `TokenPostIsNeverRetriedAndUsesConfiguredEncoding`.
- Existing tests modified: `SyscomClientTests` no longer contains embedded credentials or prints an access token; the three live tests remain explicitly skipped.
- Checks performed: Earlier focused HTTP suite passed 11/11 before the final logger assertion and Retry-After correction. `Syscom.csproj` built with zero warnings and zero errors before the Retry-After correction. Scoped `git diff --check` passed before the last correction. These checks do not establish the final revision, live-provider compatibility or end-to-end sync behavior.
- Checks not run or failed: Final focused test rerun was blocked by concurrent `SyscomPersistenceTests.cs` compilation against an in-progress database worker. Full test suite, live provider, SQL, and scheduler checks were outside this contribution. The artifact handoff checker cannot be asserted without the lead-owned trusted assignment and pre-change baseline.

## 6. Pending work and QA review

- Failures, blockers and risks: Shared-checkout test compilation must settle. Legacy `/oauth/token` JSON remains the default for compatibility; current SYSCOM documentation describes `/api/v1/oauth/token` with form encoding, configurable via `OAuthTokenPath` and `UseFormEncodedTokenRequest`. Live compatibility is unverified.
- Required action and owner: Lead reruns focused and integrated checks after concurrent edits; lead supplies scoped artifact-check inputs and coordinates host configuration. Independent reviewer validates this contribution.
- Reproduction setup: .NET SDK 10; `dotnet test Syscom/SyscomTestProject/SyscomTestProject.csproj --filter FullyQualifiedName~SyscomHttpResilienceTests -v:q`; all test traffic uses an in-process handler and sentinel strings.
- Public contracts, configuration and data schema: Existing `ISyscomClient` signatures and `SyscomClientOptions` constructor are unchanged. New init options: `RequestTimeout` (30 seconds), `MaxGetAttempts` (4 total transient GET sends), `GetRetryBaseDelay` (250 ms), `GetRetryMaxDelay` (10 seconds), `MaxGetDuration` (2 minutes), `TokenRefreshSkew` (1 minute), `BaseUrl`, `OAuthTokenPath`, and `UseFormEncodedTokenRequest` (false). One 401 refresh/replay can add one GET beyond `MaxGetAttempts`; it remains within `MaxGetDuration`. No data schema change. A caller with only a supplied access token cannot renew after 401 without client credentials.
- QA environment and setup: .NET 10 local test project; no database or account is required. Run the filtered test command above in the assigned checkout after all concurrent edits compile.
- QA cleanup: Not applicable — in-memory HTTP fixtures have no external side effects.

| QA case | Criteria / risk | Preconditions and exact input | Steps or exact command | Expected observable result |
| --- | --- | --- | --- | --- |
| QA-01 | CA-01 | Fixture token `expires_in=1`, skew 2 seconds, then two GETs. | Run filtered `ExpiringTokenIsRenewedBeforeNextGet`. | Two POSTs and GET bearer tokens `token-1`, then `token-2`; no token or credential in captured logger output. |
| QA-02 | CA-01 | GET sequence 401 then 200, and a separate persistent 401 sequence. | Run filtered `UnauthorizedRefreshCanRecover` and `UnauthorizedRenewsAndReplaysOnlyOnce`. | Recoverable case succeeds after one refresh; persistent case stops after two GETs/two token POSTs without body content in exception. |
| QA-03 | CA-02 | GET sequence 429 with Retry-After zero, 503, then 200; separate 400 and network-error fixtures. | Run filtered `TransientStatusesRetryWithFiniteAttemptsAndRetryAfter`, `PermanentFailureDoesNotRetryOrExposeResponseBody`, and `NetworkFailureRetriesOnlyConfiguredNumberOfTimes`. | Transient sequence succeeds on third GET; 400 has one GET; network failure stops at configured two attempts and hides sentinel. |
| QA-04 | CA-03 | Handler waits indefinitely with 20 ms request timeout; caller token is separately canceled; Retry-After one second, maximum backoff 5 ms, and total budget 50 ms. | Run filtered `RequestTimeoutRetriesAndCallerCancellationStopsImmediately` and `RetryAfterIsCappedByGetDuration`. | Timeout stops after two GETs; caller cancellation throws cancellation without a new GET; total budget stops the Retry-After wait after only one GET. |
| QA-05 | CA-04 | HTTP 400 body and malformed JSON include `body-sentinel-do-not-log`; credentials include `secret-sentinel-do-not-log`. | Run filtered `PermanentFailureDoesNotRetryOrExposeResponseBody` and `InvalidJsonNeverExposesResponseContent`. | Exceptions and captured logger omit both sentinels. |
| QA-06 | CA-02, CA-05 | Token POST returns 503 in legacy JSON and configured form modes. | Run filtered `TokenPostIsNeverRetriedAndUsesConfiguredEncoding`. | Exactly one POST per case; path and content type match mode; exception omits body sentinel. |

- QA coverage exclusions: Live-provider token contract and host-level run status require separately authorized integration validation.
- Independent review: PENDING.

## Verificación integrada posterior por el lead

La integración final compila en Release sin warnings y pasa 40 pruebas con 4 pruebas
contra proveedor real omitidas. Incluye PostgreSQL 17 y simulación HTTP Postman local.
Este resultado sustituye los pendientes de compilación/suite de la entrega parcial.
La revisión independiente y la activación remota permanecen separadas.
