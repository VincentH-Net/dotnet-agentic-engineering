# Uno Platform Build and Run Directive

This lets you:

- Avoid unnecessary dotnet build prior to starting an app, speeding up the agent's inner loop
- Keep a separate time-stamped log file of each app run, outside source control, which the agent can analyze to solve runtime errors and warnings
- Pass in a `AGENT_CONSOLE_LOG=<path>` startup arg to the app so it can e.g.:
  - Disable Hot Reload / Hot Design UI, which can get in the way of agents testing the app
  - Enable debug logging to specified file as early as possible during app start, so the agents can see and address any runtime warnings and errors

Use the `ensure-directives` skill to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-agentic-engineering:uno-build-and-run:start -->
## Build and Run
Build and run app via `uno_app_start` with the actual desktop target framework from the project file, for example `net10.0-desktop`, and `args: ["AGENT_CONSOLE_LOG=<path>"]`. 
The specified path is where the app's stdout and stderr output will be captured. You MUST specify this path, 
which MUST be within the `bin` folder of the app. The file MUST not exist yet and MUST be named 
with the current **local** time in ISO 8601 format: `app-stdout.YYYY-MM-DDTHHMMSS.log`. 
You MUST query the system clock for the actual current local time - do NOT estimate or hardcode it, and do NOT use UTC.

Do NOT run `dotnet build` prior to run the app - it would be redundant because `uno_app_start` already does a build.

### Verifying App Startup

- Use `uno_app_get_runtime_info` to check if the app is connected
- Call repeatedly until the app reports as connected - do NOT wait between repeats
- The specified log file must exist and must contain a log entry that mentions `uno-agentic-support`. If this is not true, first stop the app, then use the `uno-agentic-support` skill to fix logging, then start the app again and verify that the logging was fixed.
- If the Hot Design / Hot Reload UI is visible in the app, first stop the app, then use the `uno-agentic-support` skill to fix disabling Hot Design / Hot Reload UI, then start the app again and ensure that the Hot Design / Hot Reload UI is not visible.

### Stopping the App

- Use `uno_app_get_runtime_info` to get PID
- Use `uno_app_close` to terminate the app
<!-- dotnet-agentic-engineering:uno-build-and-run:end -->
~~~
