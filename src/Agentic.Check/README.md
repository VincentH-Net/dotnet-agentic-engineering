# Agentic.Check

The `agentic-check` .NET tool optimizes your repo for agentic engineering with .NET - based technologies.

- Detects which .NET based technologies and features you use
- Recommends an optimal set of agentic directives and skills for those
- You select which to apply
- Directives are installed / updated in AGENTS.md, directly from the
    dotnet-agentic-engineering GitHub repo
- Skills are installed / updated directly from source GitHub skill repo's with 
    'gh skill'

The skills available for composition are carefully selected and tested from 
best-in-class GitHub repo's. The composition minimizes context usage and avoids
contradictions and ambiguities, reducing agent mistakes.

Currently supports foundational agentic habits, .NET, ASP.NET, Microsoft Orleans
and Uno Platform

Uno Platform skills are selected depending on detected:
- MVVM or MVUX update pattern
- Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
- Fluent / Material / Cupertino design system
