using Agentic;
using System.Text;

Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false, true);
return await AgenticCli.InvokeAsync(args).ConfigureAwait(false);
