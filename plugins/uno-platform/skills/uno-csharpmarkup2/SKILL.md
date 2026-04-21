---
name: uno-csharpmarkup2
description: Build a Uno Platform 6 UI in pure C# using the concise, declarative, strongly-typed C# Markup 2 (CSharpForMarkup) library — replacing XAML with a fluent-builder, code-first approach. Covers BOTH the initial Presentation-project setup (via the `mcs-uno-markup2` template) AND ongoing per-page authoring (via the `New-View.ps1` helper that ships in the generated Presentation project and drives the `mcs-uno-view` item template under the hood). Use for new or existing Uno 6 apps on .NET 10/9 with MVVM (CommunityToolkit.Mvvm) or MVUX (Uno.Extensions.Reactive), with Uno.Extensions Navigation/Toolkit, and for supported C# Markup 2 packages (LiveCharts2, ScottPlot, Mapsui). Explains the bind-without-strings pattern (CallerArgumentExpression), Spread for variable-length children, null-as-conditional-child, attached-property syntax (e.g. `Grid_Row(0)`), Assign/Invoke markup↔logic bridging, the partial-class `<Name>Page.cs` / `<Name>Page.logic.cs` split, and the MVUX `BasePage<Bindable…Model>` wiring.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.1"
  framework: uno-platform
  category: ui-markup
  sources:
    - Modern.CSharp.Templates (mcs-uno-markup2, mcs-uno-view)
    - github.com/VincentH-Net/CSharpForMarkup
---

# C# Markup 2 (CSharpForMarkup) for Uno Platform 6

Use when you want to build a Uno Platform 6 UI in **pure C#** using the concise, declarative, strongly-typed **C# Markup 2** (`CSharpForMarkup`) library — no XAML, no CSS, no HTML. Provides a Flutter-like fluent-builder developer experience with allocation-free, reflection-free implementation and compile-time-checked bindings.

> This skill is about **CSharpForMarkup** / **C# Markup 2** by VincentH-Net, distributed as the `CSharpMarkup.WinUI.*` / `CSharpMarkup.WPF` NuGet package family. It is **not** the same technology as the separately-named "C# Markup" shipped by the Uno Platform team under `Uno.Extensions.Markup`. The APIs, project structure, and conventions differ — only use the sources listed in the References section of this skill.

## 1. When to use this skill

Select this skill for any of:

- Starting a new Uno 6 app and choosing C# over XAML for the UI layer
- Adding a C# Markup 2 Presentation layer to an existing Uno 6 app (partial conversion is supported — existing XAML pages can coexist with new C# Markup 2 pages)
- Wanting compile-time-checked binding paths via `CallerArgumentExpression` — no string paths, full rename support
- Wanting Flutter-like `children`-list composition with null-as-conditional-child and variable-length `Spread`
- Targeting .NET 10 or .NET 9 with either **MVVM** (CommunityToolkit.Mvvm) or **MVUX** (Uno.Extensions.Reactive)
- Using Uno.Extensions Navigation / Toolkit from code-first markup
- Integrating charts (LiveCharts2 2.0), plots (ScottPlot 5.1), or maps (Mapsui 5.0) into a C# Markup 2 UI

## 2. Prerequisites

- A **Uno Platform 6** solution already exists with an app project containing `App.cs` (the `mcs-uno-markup2` template layers a Presentation project **onto** an existing Uno app; it does not scaffold a full Uno solution from scratch — run `dotnet new unoapp` first)
- .NET 10 (LTS) or .NET 9 (STS) SDK installed
- PowerShell 7+ (required for `New-View.ps1` and the post-action scripts — pass `--allow-scripts Yes`)
- `Modern.CSharp.Templates` installed:

```bash
dotnet new install Modern.CSharp.Templates
```

## 3. Generate the Presentation project

In the root of your Uno solution (alongside the existing app project):

```bash
dotnet new mcs-uno-markup2 \
  --appRootNamespace Company.MyApp \
  --tfm net10.0 \
  --presentation mvvm \
  --renderer skia \
  --allow-scripts Yes
```

### Required parameters

| Option | Purpose | Values |
|---|---|---|
| `-ap`, `--appRootNamespace` | Root namespace (no trailing dot) of the existing Uno app project — the project that contains `App.cs`. Used as the prefix for the generated Presentation project's root namespace. | e.g. `Company.MyApp` |
| `-p`, `--presentation` | Design pattern baked into the Presentation project | `none` / `mvvm` / `mvux` |
| `-r`, `--renderer` | Renderer for iOS, Android, and WebAssembly targets (desktop uses Skia regardless) | `skia` / `native` |

Note that the `--presentation` parameter determines the default presentation pattern used by the `New-View.ps1` script when adding new pages. You can override the pattern per-page when running `New-View.ps1`, but the project-level default is set by this parameter.

### Optional parameters

| Option | Purpose | Values / default |
|---|---|---|
| `-tf`, `--tfm` | Target .NET version | `net9.0` / `net10.0` (default) |
| `-n`, `--name` | Output project name (default: output directory name) | string |
| `-o`, `--output` | Output folder | path |
| `--allow-scripts` | Run post-action PowerShell scripts (required for full setup) | `No` / `Prompt` (default) / `Yes` |

### Choosing `--presentation`

- **`mvux`** — pair with `Uno.Extensions.Reactive` for immutable state + feeds/commands. Generated models use `IState<T>`, `IListFeed<T>`, etc.
- **`mvvm`** — pair with `CommunityToolkit.Mvvm` `[ObservableProperty]` / `[RelayCommand]` + `ObservableObject`.
- **`none`** — pure code-behind in `<page>.logic.cs` partial files with no viewmodel layer.

### Choosing `--renderer`

- **`skia`** — Uno's Skia renderer on iOS/Android/WebAssembly. Preferred for pixel-consistent cross-platform UI and for compatibility with Uno Toolkit controls that assume Skia.
- **`native`** — Native platform controls on iOS/Android and the older WebAssembly renderer. Choose only when native-control fidelity is required (e.g. specific UIKit behavior).

Desktop (macOS/Windows/Linux) uses Skia regardless of this flag.

## 4. Core C# Markup 2 patterns to know

The template sets up the Presentation layer; these are the patterns you apply inside it when authoring UI.

### 4.1 Declarative fluent builder

Each markup file defines UI via a chain of extension-method property setters on layouts and views. Property values are strongly typed (enums, not strings). Automatic type conversion lets you write `.Margin(12)` instead of `new Thickness(12)`.

### 4.2 Bind without strings (CallerArgumentExpression)

`.Bind(...)` accepts a C# expression and the compiler captures the path text. No `nameof()`, no string literals, full rename / refactor support:

```csharp
.Bind(vm.SelectedTweet)               // binds path "SelectedTweet"
.Bind(vm.SelectedTweet.Title)         // binds path "Title"
.Bind(vm?.SelectedTweet?.Title)       // also binds path "Title" (null-propagation stripped)
.Bind("SelectedTweet")                // string overload still available for edge cases
```

Performance is equivalent to string paths; there is no runtime reflection.

### 4.3 Children composition, conditional children, Spread

Layouts take a `children` list. Two patterns make composition concise:

- **Conditional children**: `null` values in a `children` list are ignored. Use a ternary `cond ? view : null` to include/exclude a child.
- **`Spread(...)`**: inserts a variable-length child list at a specific position in a parent's `children`. Similar to Flutter's spread operator.

### 4.4 Attached property syntax

Attached properties are prefixed with the defining type plus underscore:

```csharp
Grid_Row(0)          // Grid.Row="0"
Grid_Column(2)       // Grid.Column="2"
Grid_RowSpan(2)      // Grid.RowSpan="2"
```

Multiple attached-property setters can be chained on the same element.

### 4.5 Partial class markup/logic split

Split each page across two partial class files:

| File | Content | Allowed usings |
|---|---|---|
| `<Name>Page.cs` | Markup only — `Build()` method returning the UI tree | `CSharpMarkup.*` namespaces only — **no** `Microsoft.UI.Xaml.*` / `Windows.UI.Xaml.*` usings |
| `<Name>Page.logic.cs` | Event handlers, code-behind logic, ViewModel wiring | Actual Uno / WinUI UI types — **no** `CSharpMarkup.*` usings |

The strict usings separation prevents IntelliSense pollution and keeps markup readable. Repo review rule: any `Microsoft.UI.Xaml` using in a `<Page>.cs` file is wrong; move that code to `<Page>.logic.cs`.

### 4.6 Assign() and Invoke() — bridging markup and logic

- **`.Assign(out var control)`** — capture a reference to a created control from within the markup chain, for later use in the `.logic.cs` file.
- **`.Invoke(control => { ... })`** — run imperative setup on a created control inline in the markup chain.

Both are the supported mechanism for hooking into controls that need imperative setup (event handlers, focus, etc.) without breaking the markup file structure.

## 5. Package ecosystem

The template wires up a subset of these depending on `--presentation` and `--renderer`; add the rest as features are needed:

| Package | Purpose |
|---|---|
| `CSharpMarkup.WinUI` | Core — WinUI 3 / Uno WinUI C# Markup 2 API |
| `CSharpMarkup.WinUI.Uno.Toolkit` | Markup for Uno.Toolkit.UI controls (Card, Chip, NavigationBar, …) |
| `CSharpMarkup.WinUI.Uno.Extensions.Reactive` | Markup + bindings for MVUX feeds/states |
| `CSharpMarkup.WinUI.Uno.Extensions.Navigation` | Markup for `uen:Region.*` / `uen:Navigation.*` attached properties |
| `CSharpMarkup.WinUI.Uno.Extensions.Navigation.Toolkit` | Markup for NavigationBar / TabBar / Drawer navigation |
| `CSharpMarkup.WinUI.LiveChartsCore.SkiaSharpView` | LiveCharts2 charts in C# Markup |
| `CSharpMarkup.WinUI.ScottPlot` | ScottPlot plots in C# Markup |
| `CSharpMarkup.WinUI.Mapsui` | Mapsui maps in C# Markup |

## 6. After setup — adding pages

The generated Presentation project includes a **`New-View.ps1`** helper script at its root. This is the supported path for adding new pages — do **not** invoke `dotnet new mcs-uno-view` directly.

### Running the script

From the Presentation project folder:

```powershell
.\New-View.ps1 Home                       # Use the Presentation project's default presentation pattern (mvvm / mvux / none)
.\New-View.ps1 Home mvvm                  # MVVM page
.\New-View.ps1 Home mvux                  # MVUX page
.\New-View.ps1 Home none                  # No model; logic lives in HomePage.logic.cs
.\New-View.ps1 Features.Home.Details      # Creates Features/Home/DetailsPage in sub-namespace Features.Home
```

### Parameters

| Parameter | Purpose | Default |
|---|---|---|
| `Name` (positional, required) | View identifier. `"Page"` suffix is added automatically. Dots split the name into **sub-folders and sub-namespaces** — `Features.Home.Details` produces `Features/Home/DetailsPage.cs` under namespace `<PresentationRoot>.Features.Home`. | — |
| `Presentation` (positional, optional) | Model pattern for the page. | `mvvm` (`none` / `mvvm` / `mvux`) |

### What the script produces

- `<Name>Page.cs` — markup file
- `<Name>Page.logic.cs` — code-behind partial class
- `<Name>Model.cs` — viewmodel (only when `Presentation` is `mvvm` or `mvux`)

Under the hood the script calls `dotnet new mcs-uno-view` with the correct `--name`, `--namespace` (prefixed with the Presentation project's root namespace), `--presentation`, and `--output` path. That wiring is non-trivial — always go through the script.

### Windows native (WinAppSDK) rebuild note

After adding a new page, the Windows native / WinAppSDK target may need a rebuild of the main Uno project first to regenerate `XamlTypeInfo` for the new view. Other targets (Desktop / Skia / Wasm) pick the new page up via C# Hot Reload without a rebuild.

## 7. Repo conventions to enforce

- Never `using Microsoft.UI.Xaml` (or any UI object-model namespace) inside a `<Page>.cs` markup file.
- Never put logic that directly uses the UI object-model in a `<Page>.cs` markup file — move it to `<Page>.logic.cs`.
- Prefer `.Bind(vm.X)` over `.Bind("X")`. String paths are only for rare cases where the C# expression form isn't possible.
- Use `.Assign(out _)` and `.Invoke(...)` instead of creating named fields for controls in the markup.
- Compose with `null` children for conditional UI and `Spread(...)` for dynamic-length lists — avoid building the tree imperatively in the code-behind.

## References

- [CSharpForMarkup repo](https://github.com/VincentH-Net/CSharpForMarkup) — API, examples, WinUI/Uno/WPF setups
- [Modern.CSharp.Templates repo](https://github.com/VincentH-Net/Modern.CSharp.Templates) — `mcs-uno-markup2` and the `mcs-uno-view` item template invoked by `New-View.ps1`
- [NuGet: CSharpMarkup.WinUI](https://www.nuget.org/packages/CSharpMarkup.WinUI)
