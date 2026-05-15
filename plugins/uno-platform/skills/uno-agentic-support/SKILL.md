---
name: uno-agentic-support
description: In-app support for agentic development of Uno Platform apps. Use when running or preparing a Uno Platform app for agent-driven execution with uno_app_start. Also use when the expected log file specified in AGENT_CONSOLE_LOG is missing or lacks "uno-agentic-support" entry, or Hot Reload / Hot Design remains visible during agent UI testing.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0.2"
  framework: uno-platform
  category: agentic-support
  sources: https://github.com/VincentH-Net
---

# In-app support for agentic development of Uno Platform apps

Use when using agents to run a Uno Platform app.
This skill ensures that the Uno app is properly set up to support agentic development.

When the app is run by an agent - i.e. the `AGENT_CONSOLE_LOG` is passed in as a command-line argument or else if the `AGENT_CONSOLE_LOG` environment variable is set - the skill will ensure that:
- comprehensive, early-start logging from the app is available in the log file specified by the agent
- Uno Studio Hot Reload / Hot Design UI is disabled so it does not interfere with app UI testing and does not slow down app startup

## 1. When to use this skill

Select this skill for any of:
- The agent is expected to run the Uno app in the current working folder
- The agent did run the Uno app in the current working folder and `AGENT_CONSOLE_LOG` was passed in as a command-line argument or the `AGENT_CONSOLE_LOG` environment variable is set, and any of below is true:
  - the specified log file was not created or does not contain a log entry that mentions `uno-agentic-support`
  - the specified log file contains a log entry that mentions `uno-agentic-support: true` but the Uno app is still showing the Hot Reload / Hot Design UI when launched by the agent

## 2. STOP — prerequisite gate before applying edits

Before applying edits from this skill, run this prerequisite check in the current working folder and act on the result.

A **Uno Platform** app project must exist in the working folder. Find the Uno app `.csproj` in the working folder. A valid Uno app project must use the Uno SDK and contain `App.xaml.cs` in or under the project folder.

If the Uno Platform app project is not found, report this to the user and STOP.

## 3. Ensure App.xaml.cs is wired up for agentic development

In `App.xaml.cs`:

- Adapt, don't paste blindly. Uno templates and application host setup vary. Preserve existing app behavior, logging providers, filters, host configuration, namespaces, and comments unless they directly conflict with agentic support.

- Ensure that the `App` class includes these code snippets:

    ```csharp
    #if DEBUG
        readonly bool startedByAgent;
    #endif

    #if DEBUG
        void InitializeAgentSupport(string? agentConsoleLogPath)
        {
            if (agentConsoleLogPath is not null)
            {
                var stream = new FileStream(agentConsoleLogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                #pragma warning disable CA2000 // Dispose objects before losing scope
                var writer = new StreamWriter(stream) { AutoFlush = true };
                #pragma warning restore CA2000 // Dispose objects before losing scope
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            // Make sure that loggings made before the application host is initialized are captured
            LogExtensionPoint.AmbientLoggerFactory = LoggerFactory.Create(ConfigureAppLogging);
            #if HAS_UNO
                Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
            #endif
        }

        static string? ResolveAgentConsoleLogPath()
        {
            const string key = "AGENT_CONSOLE_LOG";

            string prefix = key + "=";
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string value = arg[prefix.Length..];
                    if (!string.IsNullOrEmpty(value)) return value;
                }
            }

            string? fromEnv = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(fromEnv) ? null : fromEnv;
        }
    #endif

        void ConfigureAppLogging(ILoggingBuilder logBuilder)
        {
            _ = logBuilder.SetMinimumLevel(LogLevel.Warning);
        #if DEBUG
            // Set the default log level for the app's own namespace to Debug
            _ = logBuilder.AddFilter(GetType().Namespace!.Split('.')[0], LogLevel.Debug);
            // Make sure that agents receive the logs in the console
            if (startedByAgent) _ = logBuilder.AddConsole();

            // Uno Platform namespace filter groups
            // Uncomment individual methods to see more detailed logging
            //// Generic Xaml events
            //logBuilder.XamlLogLevel(LogLevel.Debug);
            //// Layout specific messages
            //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
            //// Storage messages
            //logBuilder.StorageLogLevel(LogLevel.Debug);
            //// Binding related messages
            //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
            //// Binder memory references tracking
            //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
            //// DevServer and HotReload related
            //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
            //// Debug JS interop
            //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);
        #endif
        }
    ```

-  The `App` constructor must begin with below statements. Add them if not present, or move them to the top of the constructor if present but not at the top:
    ```csharp
    #if DEBUG
        string? agentConsoleLogPath = ResolveAgentConsoleLogPath();
        startedByAgent = agentConsoleLogPath is not null;
        InitializeAgentSupport(agentConsoleLogPath);
        var logger = this.Log();
        if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug("Application Launched in DEBUG mode. uno-agentic-support: {StartedByAgent}", startedByAgent);
    #endif
    ```

- In the `OnLaunched` method:
  - The application host configuration must call `.UseLogging()` with `configure: ConfigureAppLogging` and `enableUnoLogging: true`.
  - If `.UseLogging()` already exists, preserve existing app-specific logging setup and adapt it so `ConfigureAppLogging` still runs and Uno logging remains enabled.
  - If `.UseLogging()` is missing, add it in the existing host configuration chain:
    ```csharp
        .UseLogging(
            configure: ConfigureAppLogging,
            enableUnoLogging: true
        )
    ```
  - In `ConfigureAppLogging`, preserve any existing app-specific filters and providers. Add the agent-specific pieces only if they are missing:
    - `logBuilder.SetMinimumLevel(LogLevel.Warning);`
    - `logBuilder.AddFilter(GetType().Namespace!.Split('.')[0], LogLevel.Debug);`
    - `if (startedByAgent) logBuilder.AddConsole();`
  - If an existing `UseStudio` call is present, replace it with:
    ```csharp
        #if DEBUG
        MainWindow.UseStudio(showHotReloadIndicator: !startedByAgent, launchHotDesignOnStart: !startedByAgent);
        #endif
    ```

## 4. Validate

After editing:

- Build or start the app using the repository's active Build and Run directive; pass in `AGENT_CONSOLE_LOG=<new-log-path>`.
- Confirm the log file exists.
- Confirm it contains `uno-agentic-support:`. Match this case-insensitively.
- Confirm Hot Reload / Hot Design UI is not visible.
