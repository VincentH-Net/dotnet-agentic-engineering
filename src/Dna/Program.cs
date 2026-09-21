using System.Reflection;
using Agentic;
using Dna;

if (args is ["check", .. var arguments])
{
    // Agentic.Check verifies this launcher version and must not replace the running dna.
    Environment.SetEnvironmentVariable(DnaLauncherContract.VersionVariable,
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    return await ToolLauncher.CheckAsync(arguments, Environment.CurrentDirectory, Console.Error).ConfigureAwait(false);
}

if (await LocalCompanion.IsMissingAsync(Environment.CurrentDirectory).ConfigureAwait(false))
{
    await Console.Error.WriteLineAsync("`dotnet agentic` is not installed in the scope of the current working folder.").ConfigureAwait(false);
    await Console.Error.WriteLineAsync("Please run `dna check` to install it.").ConfigureAwait(false);
    return 1;
}

return await ToolLauncher.RunAsync(["tool", "run", "agentic", "--", .. args], Environment.CurrentDirectory, Console.Error).ConfigureAwait(false);
