# dna

> [!NOTE]
> dna : [dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering) in one command

`dna` provides shorthands for the [Agentic.Check](https://www.nuget.org/packages/Agentic.Check) and [Agentic](https://www.nuget.org/packages/InnoWvate.Agentic) tools, so you have less to remember and type:

```bash
dna -h                        # show help for available commands
dna check                     # run the latest Agentic.Check to set up or update this repo
dna check -h                  # show help for Agentic.Check parameters
dna prompt-log                # show the latest 20 prompt logs
dna prompt-log -h             # show help for prompt-log parameters
```

`dna` is deliberately tiny and versioned independently. Functionality lives in the repository-pinned `dotnet agentic` and the latest `agentic.check`.

## Prerequisites

- .NET 10 or later SDK selected for the current folder

## Install, update and usage

See [Get started](https://github.com/VincentH-Net/dotnet-agentic-engineering#get-started)