# REQUIREMENTS (Statement of Work) — Synchroteam Integration Middleware

This document describes the requirements and scope of work (SOW) for building a middleware service that synchronizes data between your internal systems (PostgreSQL via EF Core 8) and Synchroteam (v3 REST API). It is designed to be cost‑aware with an AWS‑serverless‑first deployment.


## 1. Overview & Constraints

What we know / constraints:
- Synchroteam provides a v3 REST API for CRUD/upsert of customers, sites, contacts (and optionally jobs, equipment, etc.).
- All calls must be over HTTPS with TLS 1.2+.
- The API supports upsert semantics: a single POST can create or update based on presence of id or myId.
- We must keep in sync at least three entities: customers, sites, contacts. Extensibility for jobs/equipment is desirable.
- Webhooks from Synchroteam should be consumed to avoid constant polling.
- Minimal‑cost AWS deployment to start, with a path to scale later.
- Internal system database is PostgreSQL; use .NET 8 with EF Core 8 for persistence.

Assumptions:
- Access to Synchroteam tenant (domain/instance), API credentials, and webhook configuration control is available.
- Basic schema/tables exist or will be added to support ID mapping and change tracking.
- Internal authentication/authorization method will be defined (e.g., API keys/JWT/OIDC), but initial PoC may use API key.


## 2. Key Functional Requirements

1) Synchroteam Client Library
- A strongly‑typed client wrapper around Synchroteam v3 API endpoints for Customers, Sites, Contacts.
- Supports create/update (upsert), get by id/myId, list with filters (e.g., updatedSince), and delete (if required).
- Handles auth headers, base URL configuration, timeouts, retries, error normalization.

2) Internal Middleware API
- Endpoints for push sync operations from internal systems/UI, e.g.:
  - POST /sync-to-st/customer
  - POST /sync-to-st/site
  - POST /sync-to-st/contact
- Validates input, enqueues background jobs, and returns 202 Accepted with tracking ID.
- Read endpoints for status/health and optional reconciliation operations.

3) Scheduler / Polling (Delta Fetch)
- Periodic jobs to fetch changed entities from Synchroteam since a given timestamp (updatedSince or equivalent).
- Upserts fetched entities into the internal database via EF Core 8.
- Maintains watermarks per entity type for efficient delta sync.

4) Webhook Endpoint (Public)
- Public endpoint(s) to receive Synchroteam event notifications (e.g., customer.updated).
- Validates authenticity (HMAC, IP allowlist, or agreed token) and enqueues processing jobs.
- Worker fetches full record from Synchroteam and upserts into internal DB.

5) Conflict Resolution, Mapping, Errors, Retry, Rate Limiting, Logging
- Conflict policy: configurable per entity (authoritative system or timestamp‑based).
- ID mapping table(s) to track internal <-> Synchroteam IDs plus hashes and timestamps.
- Exponential backoff/retry for transient errors (timeouts, 429, 5xx).
- Dead‑letter for persistent failures with alerting.
- Structured logging, correlation IDs, and audit trails of sync events.

6) Security, Secrets, Scaling
- Enforce HTTPS end‑to‑end.
- Secrets in AWS Secrets Manager/SSM Parameter Store.
- Throttling to respect Synchroteam rate limits; circuit breaker behavior during outages.
- IAM least privilege; ability to scale workers horizontally if needed.


## 3. Architecture & Module Decomposition

High‑level:
[ Internal Systems / DB ] <--> [ Middleware Service ] <--> [ Synchroteam API / Webhooks ]

Modules:
- Domain + Data Access (EF Core 8, PostgreSQL)
- Synchroteam Client Library (HTTP, resilience)
- Middleware API (HTTP handlers for push sync, webhook, admin/health)
- Queue/Worker Layer (SQS + Lambda; optional Step Functions)
- Scheduler (CloudWatch Events / EventBridge + Lambda)
- Observability (CloudWatch Logs/Metrics, alarms)

Data flows:
- Push sync (internal -> ST):
  1) Internal system calls POST /sync-to-st/customer.
  2) API validates, enqueues SyncCustomerUpsertJob.
  3) Worker dequeues, calls Synchroteam Client upsert.
  4) Worker updates mapping table and status in DB.
- Webhook (ST -> internal):
  1) Synchroteam calls our webhook: customer.updated.
  2) Handler validates, enqueues HandleCustomerChanged.
  3) Worker fetches full record via client and upserts into DB.
- Scheduled poll (ST -> internal):
  1) Cron triggers pollContactsUpdatedSince(t0).
  2) Client lists updates; worker upserts each into DB.


## 4. Data Model & Mapping

ID mapping table (example):
- entity_type (customer/site/contact)
- internal_id (GUID/int)
- external_id (Synchroteam id)
- external_my_id (optional; if used as stable key)
- last_synced_at (UTC)
- last_synced_hash (content hash for idempotency)
- version/etag (if exposed by ST)
- sync_status, last_error

Change detection:
- Compute hash of canonicalized payload to detect changes quickly.
- Use updatedSince cursors from Synchroteam where available.

Conflict policy:
- Configurable per entity: internal‑wins, external‑wins, or last‑write‑wins by timestamp.


## 5. Error Handling, Retry, Idempotency

- Idempotent upserts via Synchroteam’s semantics; include safe dedup keys (myId) where possible.
- Retries with exponential backoff and jitter on 408/429/5xx.
- Do not retry on 4xx validation errors; log and dead‑letter with the payload snapshot.
- Partial failure isolation in batch operations; continue with non‑failing items.
- Correlate logs with request IDs and job IDs.


## 6. Rate Limiting, Throttling & Backoff

- Respect documented or observed Synchroteam limits. Start conservatively (e.g., 2–5 RPS per entity type) and tune.
- Client‑side token bucket/leaky bucket throttling.
- On 429 or rate‑limit errors, exponential backoff with Retry‑After support.


## 7. Security & Reliability

- HTTPS/TLS 1.2+ everywhere; API Gateway in front of public endpoints.
- Webhook validation: HMAC signature or shared secret, optional IP allowlist.
- Authentication/Authorization on internal API (API keys or JWT/OIDC); rate limit your public API.
- Secrets in Secrets Manager/Parameter Store, rotated per policy.
- IAM least privilege for Lambda, SQS, Secrets, RDS/DynamoDB if used.
- Circuit breaker to prevent cascading failures when ST is degraded.
- DLQs for SQS and alerting on poison messages.
- Comprehensive logging and audit trail of sync activity and decisions.


## 8. Deployment / Infrastructure on AWS (Cost‑aware)

Minimal starting architecture (serverless‑first):
- Amazon API Gateway — public HTTP for middleware and webhook paths.
- AWS Lambda — implement middleware handlers, workers, schedulers.
- Amazon SQS — queue between API and workers; DLQ configured.
- AWS Secrets Manager or SSM Parameter Store — hold ST credentials and config.
- Amazon CloudWatch — logs, metrics, dashboards, alarms.
- Optional AWS Step Functions — for complex multi‑step workflows.
- Database: Start with existing PostgreSQL (Amazon RDS/Aurora Serverless v2 or self‑managed) accessed via EF Core 8.

Networking:
- Private subnets for DB; Lambdas in VPC if required to access RDS.
- Security groups restricting ingress/egress minimally.

Cost notes:
- Use provisioned‑concurrency only if cold starts are problematic on critical paths.
- Batch operations and scheduled polling to control call volume.


## 9. CI/CD & Operations

- Infrastructure as code (AWS CDK) for repeatable environments.
- Pipeline: Git → Build → Test → Deploy (per environment).
- Blue/green or canary for sensitive workers/webhooks.
- Versioning and automated rollback.
- Observability: structured logs, custom metrics (e.g., sync throughput, errors, retries), alarms.


## 10. Phased Plan & Timeline

Phase 1: Prototype / PoC
- Build minimal Synchroteam client (customers, sites). 
- Create POST /sync/customer endpoint in Lambda through API Gateway.
- Deploy minimal stack via CDK.
- Test with sample records end‑to‑end.

Phase 2: Webhooks & Pull Sync
- Implement webhook endpoints and job processing.
- Implement scheduled delta polling per entity with watermarks.
- Add ID mapping + conflict resolution logic.
- Add error handling, retry, structured logging.

Phase 3: Bulk / Bootstrap & Reconciliation
- Bulk import endpoint for initial data load.
- Reconciliation routines to detect mismatches and fix.
- Basic admin dashboard/monitoring pages or CloudWatch dashboards.

Phase 4: Harden, Monitor, Scale
- Circuit breakers, client throttling.
- Metrics, alarms, SLOs.
- Performance profiling and optimization.
- Consider containerized workers if Lambda constraints hit; cost optimization review.


## 11. Non‑Functional Requirements

- Availability: Target 99.9% for public endpoints initially (subject to AWS regional SLAs).
- Performance: Median p50 < 300 ms for simple API acknowledgments (enqueue); background work can be asynchronous.
- Scalability: Should handle thousands of records/day initially, with headroom to grow.
- Security: Adhere to least privilege, encrypted at rest/in transit, secrets hygiene.
- Observability: Traceability of each sync job from trigger to completion.


## 12. Acceptance Criteria

- REQUIREMENTS.md (this document) stored at repository root and reviewed/approved.
- Synchroteam client scaffolding and API surface defined for customers/sites/contacts.
- Middleware API routes and request/response contracts documented.
- Webhook validation approach documented and tested with a sample secret.
- Delta polling strategy with watermark storage defined.
- ID mapping schema proposed (EF Core 8 entity + migration plan).
- Retry/backoff, DLQ, and error classification behavior documented.
- AWS architecture diagram or description aligns with serverless‑first minimal design.
- CI/CD pipeline definition outline (CDK + deploy steps) present.


## 13. Open Questions

- Exact Synchroteam event types and webhook payload schema to finalize signatures.
- Documented rate limits for Synchroteam v3; confirm if tenant‑specific.
- Authoritative‑system policy per entity (internal‑wins vs external‑wins) and conflict rules.
- Multi‑environment strategy (dev/stage/prod) and resource naming conventions.
- Authentication method for internal API (API key vs JWT/OIDC) at launch.


## 14. References

- Synchroteam API v3 documentation (vendor site)
- AWS CDK, Lambda, API Gateway, SQS, Secrets Manager, CloudWatch docs
- EF Core 8 documentation (Microsoft)
