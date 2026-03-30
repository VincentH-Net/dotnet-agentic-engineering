# Uno Plaform Build and Run Directive

This lets you:

- Avoid unnecessary dotnet build prior to starting an app, speeding up the agent's inner loop
- Keep a separate time-stamped log file of each app run, outside source control, which the agent can analyze to solve runtime errors and warnings
- Pass in a RUN_BY_AGENT startup arg to the app so it can e.g. disable Hot Reload / Hot Design UI, which can get in the way of agents testing the app

Copy below markdown in your `AGENTS.MD`

~~~md
## Build and Run

Build and run app via `uno_app_start` (net10.0-desktop) with `args: ["RUN_BY_AGENT"]`. To capture the app's stdout output, you MUST use the `stdoutFile` parameter with a file path, which MUST be within the `bin` folder of the app. The file MUST not exist yet and MUST be named with the current **local** time in ISO 8601 format: `app-stdout.YYYY-MM-DDTHHMMSS.log`. You MUST query the system clock for the actual current local time — do NOT estimate or hardcode it, and do NOT use UTC.

Do NOT run `dotnet build` prior to run the app - it would be redundant because `uno_app_start` already does a build.

### Verifying App Startup

- Use `uno_app_get_runtime_info` to check if the app is connected
- Call repeatedly until the app reports as connected - do NOT wait between repeats

### Stopping the App

- Use `uno_app_get_runtime_info` to get PID
- Use `uno_app_close` to terminate the app
~~~
