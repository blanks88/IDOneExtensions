# Deploying the Syscom sync to Heroku

`Syscom/SyscomApp` is a console app that syncs the Syscom catalog into the IDOne PostgreSQL database and then exits.
It is deployed to Heroku as a Docker image (`web` image, scaled to zero) and run every hour by **Heroku Scheduler**.
There is no long-running dyno: every run is a one-off dyno that starts, syncs and stops.

```
Heroku Scheduler ──▶ one-off dyno (image from Syscom/SyscomApp/Dockerfile)
                         └─ /app/bin/run-syscom → dotnet /app/SyscomApp.dll ──▶ Syscom API ──▶ Heroku Postgres
```

## 1. Prerequisites

- [Heroku CLI](https://devcenter.heroku.com/articles/heroku-cli) logged in (`heroku login`).
- Docker running locally.
- A Heroku app (`alusa-syscom-sync` below) and the IDOne database it will write to.
- Syscom API credentials (client id / client secret).

All commands run from the **repository root**, because the Dockerfile copies the whole solution as build context.

## 2. Configuration (environment variables)

The app reads its configuration only from environment variables and exits with an error naming the missing one.

| Variable               | Description                                                                              |
|------------------------|------------------------------------------------------------------------------------------|
| `ALUSA_CONNECTION_STRING`    | Npgsql connection string for the IDOne database. Must be Npgsql format, not `postgres://`|
| `SYSCOM_CLIENT_ID`     | Syscom API client id                                                                     |
| `SYSCOM_CLIENT_SECRET` | Syscom API client secret                                                                 |

Connection string format (Heroku Postgres requires SSL):

```
Host=<host>;Port=5432;Database=<db>;User ID=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=true
```

Heroku exposes the database as a URL in `DATABASE_URL` (`postgres://user:password@host:port/db`). Convert it to the
format above, or read the pieces with:

```bash
heroku config:get DATABASE_URL -a alusa-syscom-sync
```

Set the variables on the app using your credentials:

```bash
heroku config:set -a alusa-syscom-sync \
  ALUSA_CONNECTION_STRING="Host=<host>;Port=5432;Database=<db>;User ID=<user>;Password=<password>;SSL Mode=Require" \
  SYSCOM_CLIENT_ID="<value>" \
  SYSCOM_CLIENT_SECRET="<value>"
```

> **Credential rotation:** Heroku Postgres can rotate its credentials (`heroku pg:credentials:rotate`, maintenance,
> failover). `ALUSA_CONNECTION_STRING` is a copy, so it goes stale when that happens. Update it with `heroku config:set`
> after any rotation. Alternatively, create a dedicated credential for this app with `heroku pg:credentials:create`
> and use it in `ALUSA_CONNECTION_STRING`.

## 3. Build and release the image

```bash
heroku stack:set container -a alusa-syscom-sync
heroku container:login

docker build --platform linux/amd64 \
  -f Syscom/SyscomApp/Dockerfile \
  -t registry.heroku.com/alusa-syscom-sync/web .

docker push registry.heroku.com/alusa-syscom-sync/web
heroku container:release web -a alusa-syscom-sync
```

Notes:
- `--platform linux/amd64` is required when building on Apple Silicon; Heroku dynos run amd64.
- `heroku container:push` is not used because it expects a `Dockerfile` in the current directory; this one lives in
  `Syscom/SyscomApp/`.
- Do **not** scale the `web` process type up. It is only used as the image for scheduled one-off dynos. Make sure it
  stays at zero:
  ```bash
  heroku ps:scale web=0 -a alusa-syscom-sync
  ```

## 4. Test a run manually

Before scheduling anything, run the sync once in a one-off dyno and watch the output:

```bash
heroku run -a alusa-syscom-sync -x "/app/bin/run-syscom"
```

- `-x` passes the process exit code through (non-zero if configuration is missing or the sync fails).
- Logs are written to the console (Serilog, Debug level), so you see them directly in the terminal.

The runner uses `exec dotnet /app/SyscomApp.dll`, preserving the application's exit code and forwarding signals
without an intermediate shell. Each invocation performs one complete sync and exits. The Dockerfile uses `CMD`
so that Scheduler and manual commands can override it.

## 5. Schedule every hour with Heroku Scheduler

Check whether the add-on already exists before creating it:

```bash
heroku addons -a alusa-syscom-sync
# Only if Scheduler is not installed:
heroku addons:create scheduler:standard -a alusa-syscom-sync
heroku addons:open scheduler -a alusa-syscom-sync
```

In the Scheduler dashboard, edit the existing Syscom job (or **Add job** if none exists):

| Field | Value |
|-------|-------|
| Run command | `/app/bin/run-syscom` |
| Frequency | **Hourly** |
| Minute | **:00** |
| Dyno size | **Standard-1X** |

Save the job and confirm its next run. Keep exactly one Syscom job to avoid duplicate syncs.
This starts a full sync every hour (24 planned runs per day). No four-hour guard or in-process timer is needed.
The schedule is saved in the add-on dashboard; deploying this repository alone does not create or update it.

**Container requirement:** Scheduler uses the `web` image and cannot select `worker`. Release the image as `web`
and keep `web=0`; this app does not serve HTTP. If an older `worker` formation exists, also scale it to zero:

```bash
heroku ps:scale worker=0 -a alusa-syscom-sync
```

Watch the first scheduled execution and confirm a successful exit in the logs. A run must finish within an hour:
Scheduler terminates jobs around their interval, with possible brief overlap. It can also rarely skip or duplicate
a run; this setup does not provide an exactly-once guarantee or a distributed lock.

References: [Heroku Scheduler](https://devcenter.heroku.com/articles/scheduler),
[Container Registry and Runtime](https://devcenter.heroku.com/articles/container-registry-and-runtime).

## 6. Operations

| Task                          | Command                                                       |
|-------------------------------|---------------------------------------------------------------|
| Follow logs of scheduled runs | `heroku logs --tail -a alusa-syscom-sync --dyno scheduler`                |
| Check current dynos           | `heroku ps -a alusa-syscom-sync`                                          |
| Show config (includes secrets)      | `heroku config -a alusa-syscom-sync`; avoid pasting its output anywhere   |
| Change a variable             | `heroku config:set NAME=value -a alusa-syscom-sync` (applies to the next run, no redeploy) |
| Deploy a new version          | Repeat section 3                                              |
| Roll back                     | `heroku releases -a alusa-syscom-sync` then `heroku rollback v<N> -a alusa-syscom-sync` |
| Remove the schedule           | `heroku addons:open scheduler -a alusa-syscom-sync` and delete the job    |

Heroku bills one-off dyno time per second while a run is active, so the cost is roughly *(run duration × 24 × days per month)* at the dyno's hourly price. Measure one manual run (section 4) to estimate it.

## 7. Troubleshooting

| Symptom                                                | Likely cause / fix                                                                  |
|--------------------------------------------------------|-------------------------------------------------------------------------------------|
| `The ALUSA_CONNECTION_STRING environment variable is not set`| Variable missing on the app. Same message pattern for `SYSCOM_CLIENT_ID/SECRET`.    |
| `Failed to connect` / timeout to the database          | Wrong host or port, credentials rotated, or the database is not reachable from Heroku. |
| `SSL`/`no pg_hba.conf entry` errors                    | Add `SSL Mode=Require;Trust Server Certificate=true` to `ALUSA_CONNECTION_STRING`.        |
| `exec format error` when the dyno starts               | Image built for arm64. Rebuild with `--platform linux/amd64`.                       |
| Scheduled job does nothing / uses the wrong image | Release the image as `web` (section 3), then test `/app/bin/run-syscom` (section 4). Scheduler cannot use a `worker` image. |
| Authentication errors against Syscom                   | Check `SYSCOM_CLIENT_ID` / `SYSCOM_CLIENT_SECRET`.                                  |

## 8. Security notes

Store credentials in Heroku config vars; the examples above use placeholders. Previously exposed credentials must
be rotated. Keep `ALUSA_CONNECTION_STRING` updated after database credential rotation.
