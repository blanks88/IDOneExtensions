#!/bin/sh
set -eu

# Run one sync; Heroku Scheduler owns the hourly schedule.
exec dotnet /app/SyscomApp.dll
