---
name: uno-mvvm
description: "Uno Platform MVVM with CommunityToolkit.Mvvm: mutable ViewModels, ObservableObject, ObservableProperty on C# partial properties, RelayCommand, async commands, constructor dependency injection, x:Bind binding patterns, and Uno Navigation from ViewModels. Use when the user selected MVVM as the update model or asks for ViewModel, ICommand, INotifyPropertyChanged, ObservableObject, ObservableProperty, RelayCommand, or CommunityToolkit.Mvvm in an Uno Platform app. Do NOT use for MVUX models/feeds/states; use Studio MVUX skills instead. Do NOT use for C# Markup 2 binding syntax; combine with uno-csharpmarkup2 only when the selected markup type is C# Markup 2."
metadata:
  author: https://github.com/VincentH-Net
  version: "1.1.1"
  framework: uno-platform
  category: update-model
  sources:
    - https://github.com/mtmattei/UnoPlatformSkills
    - "Microsoft Learn: C# partial members and partial properties"
    - "Microsoft Learn: CommunityToolkit.Mvvm ObservableProperty partial-property generator diagnostics"
    - Uno Extensions Navigation and Hosting patterns
---

# Uno Platform MVVM

Use this skill only when the app's selected update model is **MVVM**.

MVVM in Uno Platform means mutable ViewModel classes, usually built with `CommunityToolkit.Mvvm`, bound from the UI through generated observable properties and generated commands. Do not drift into MVUX (`partial record`, `IFeed<T>`, `IState<T>`, generated `*ViewModel` from `*Model`) unless the user explicitly changes the update model.

## Scope Rules

- Respect the user's selected update model: MVVM stays MVVM.
- Respect the selected design system: this skill does not choose Material or Fluent resources.
- Respect the selected markup type:
  - XAML: use the XAML examples here.
  - Uno C# Markup: use the Uno C# Markup skill for syntax.
  - C# Markup 2: use `uno-csharpmarkup2` for syntax and only apply the ViewModel/command concepts from this skill.
- Prefer bindings to commands over code-behind event handlers.
- Keep ViewModels UI-framework-free where practical: no `Page`, `Button`, `TextBox`, `DispatcherQueue`, or direct visual tree access in ViewModels unless a platform boundary genuinely requires it.

## Project Checks

Before adding MVVM code, inspect the app project:

- If the project uses Uno SDK features, ensure `Mvvm` is present in `<UnoFeatures>` or that `CommunityToolkit.Mvvm` is otherwise referenced.
- For `[ObservableProperty]` on partial properties, use .NET 9 SDK or newer and set `<LangVersion>preview</LangVersion>`.
- If using Uno Extensions Navigation, ensure `Navigation` is present in `<UnoFeatures>` and `.UseNavigation(RegisterRoutes)` is configured.
- If resolving ViewModels/services through DI, ensure the host builder has `.ConfigureServices(...)`.

Do not add MVUX packages or `MVUX` features for an MVVM app.

## ViewModel Pattern

Use `ObservableObject` plus source-generator attributes on **partial properties**. Do not use the older field annotation pattern for new Uno MVVM code.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uno.Extensions.Navigation;

public sealed partial class ProfileViewModel : ObservableObject
{
    readonly INavigator navigator;
    readonly IProfileService profileService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string? Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Guest" : Name;
    public bool CanSave => !IsLoading;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ProfileViewModel(INavigator navigator, IProfileService profileService)
    {
        this.navigator = navigator;
        this.profileService = profileService;
    }

    [RelayCommand]
    async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await profileService.SaveAsync(Name, cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    Task OpenDetailsAsync() =>
        navigator.NavigateViewModelAsync<ProfileDetailsViewModel>(this);
}
```

Rules:

- Use `partial` ViewModel classes so CommunityToolkit generators can emit properties and commands.
- Use `[ObservableProperty] public partial T Name { get; set; }` for mutable UI state.
- Partial observable properties must be definition-only declarations with semicolon accessors. Let the Toolkit generator provide the implementation; do not write a second partial implementation manually.
- Partial observable properties need both `get` and `set`; do not use `init`.
- Use `[NotifyPropertyChangedFor]` for dependent computed properties.
- Use `[RelayCommand]` / `[AsyncRelayCommand]` patterns instead of hand-written `ICommand` unless the app already has a command convention.
- Include `CancellationToken` parameters on async relay-command methods when the operation can be cancelled.
- Keep side effects in commands, services, or partial property-change hooks; keep property setters simple.

If existing code uses `[ObservableProperty]` on fields, convert it to partial properties. Microsoft Toolkit diagnostics recommend this for developer experience, analyzer/source-generator visibility, and WinRT/WinUI AOT compatibility.

## Property Change Hooks

Generated observable properties support partial hooks:

```csharp
partial void OnNameChanged(string? value)
{
    ErrorMessage = null;
}
```

Use hooks for local state updates only. Avoid calling network APIs or navigation from property-change hooks; use commands for those actions.

## XAML Binding

Prefer `x:Bind` when the page exposes a strongly typed `ViewModel` property.

```xml
<TextBox Text="{x:Bind ViewModel.Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
<TextBlock Text="{x:Bind ViewModel.DisplayName, Mode=OneWay}" />
<ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
<Button Content="Save"
        Command="{x:Bind ViewModel.SaveCommand}"
        IsEnabled="{x:Bind ViewModel.CanSave, Mode=OneWay}" />
<Button Content="Details"
        Command="{x:Bind ViewModel.OpenDetailsCommand}" />
```

Uno-specific binding rules:

- `x:Bind` defaults to `OneTime`; set `Mode=OneWay` for updating display state and `Mode=TwoWay` for editable input.
- For text input that should update the ViewModel while typing, set `UpdateSourceTrigger=PropertyChanged`.
- Bool-to-`Visibility` is supported implicitly in Uno bindings.
- Do not use WPF-only binding features such as `StringFormat`, `x:Static`, or `{x:Reference}`.
- For string composition in XAML, use multiple `Run` elements or expose a computed ViewModel property.
- In `DataTemplate`, set `x:DataType` when using `x:Bind`; otherwise use ordinary `{Binding}` where the template scope requires it.

Prefer a computed ViewModel property over converter stacks:

```csharp
public string StatusText => IsLoading ? "Saving..." : "Ready";
```

```xml
<TextBlock Text="{x:Bind ViewModel.StatusText, Mode=OneWay}" />
```

## DataTemplate Command Binding

When an item template needs to call a page-level ViewModel command, do not put click handlers in code-behind.

Use the app's established pattern:

- `x:Bind` to the page ViewModel when the page is in scope.
- Uno Toolkit `AncestorBinding` / `ItemsControlBinding` when a template needs an ancestor DataContext.
- `CommandParameter` for the item.

XAML shape:

```xml
<ListView ItemsSource="{x:Bind ViewModel.Items, Mode=OneWay}">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="models:Customer">
            <Button Content="{x:Bind Name}"
                    Command="{Binding DataContext.OpenCustomerCommand, ElementName=CustomersList}"
                    CommandParameter="{x:Bind}" />
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

If `ElementName` binding is unreliable in the current Uno target, use Uno Toolkit ancestor-binding helpers instead of code-behind handlers.

## Dependency Injection

Use constructor injection for ViewModels and services.

```csharp
.ConfigureServices((context, services) =>
{
    services.AddSingleton<IProfileService, ProfileService>();
    services.AddTransient<ProfileViewModel>();
    services.AddTransient<ProfileDetailsViewModel>();
})
```

Rules:

- ViewModel dependencies should appear in the constructor.
- Avoid `App.Host.Services.GetService<T>()` inside ViewModels.
- Use `TryAdd*` only for reusable libraries that provide overridable defaults; app-level registrations should usually use `Add*`.
- Register long-lived stateless services as singleton and page-specific ViewModels as transient unless the app has an explicit lifetime model.

## Navigation

With Uno Extensions Navigation, inject `INavigator` into the ViewModel and navigate from commands.

```csharp
[RelayCommand]
Task ShowOrderAsync(Order order) =>
    navigator.NavigateViewModelAsync<OrderDetailsViewModel>(this, data: order);
```

Route registration must include the ViewModel and data shape:

```csharp
views.Register(
    new ViewMap<OrdersPage, OrdersViewModel>(),
    new DataViewMap<OrderDetailsPage, OrderDetailsViewModel, Order>());
```

Rules:

- Use `DataViewMap<TView, TViewModel, TData>` when passing typed data.
- Receive navigation data through the target ViewModel constructor.
- Use `NavigateRouteAsync(this, "-/Login")` for sibling-route navigation when `NavigateViewModelAsync` resolves only within the current child scope.
- Prefer XAML navigation attached properties for pure UI navigation requests when no ViewModel decision is involved.

## Loading, Errors, And Validation

Use explicit state properties so UI can bind without inspecting task internals.

```csharp
[ObservableProperty]
public partial bool IsLoading { get; set; }

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(HasError))]
public partial string? ErrorMessage { get; set; }

public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
```

For validation:

- Keep business validation in services or ViewModel methods.
- Expose user-facing error strings or field-level state properties.
- Avoid throwing exceptions from property setters for normal validation failures.

## Collections

For mutable lists, use `ObservableCollection<T>` or an existing app-specific observable collection pattern.

```csharp
public ObservableCollection<Customer> Customers { get; } = [];
```

Rules:

- Replace the whole collection only when the control fails to refresh or the app pattern expects replacement.
- Mutate `ObservableCollection<T>` on the UI thread.
- For large lists, use `ListView`/`GridView` virtualization or `ItemsRepeater` with an appropriate layout; do not wrap `ListView` in `ScrollViewer`.

## MVVM vs MVUX Guardrail

Use MVVM when:

- The user selected MVVM.
- The app already uses CommunityToolkit.Mvvm.
- State is naturally mutable and form-oriented.
- The team wants familiar ViewModels and commands.

Do not introduce:

- `partial record *Model` as the page state owner.
- `IFeed<T>`, `IState<T>`, `IListFeed<T>`, or `IListState<T>`.
- MVUX generated command assumptions.

Those belong to the Studio MVUX skills and only apply when the update model is MVUX.

## Review Checklist

- ViewModels inherit `ObservableObject` or the app's established MVVM base.
- Generated properties use `[ObservableProperty]` on partial properties, not fields.
- Partial observable properties have `get; set;`, no implementation body, and no `init`.
- The project uses a compatible SDK/language setup for partial-property generation.
- Generated commands come from `CommunityToolkit.Mvvm` attributes.
- Commands are bound from UI; no new Click/Tapped handlers for ViewModel actions.
- Async commands manage loading/error state and do not leave `IsLoading` stuck.
- Services and `INavigator` are constructor-injected.
- Navigation data uses `DataViewMap` and constructor injection.
- Bindings use the selected markup technology correctly.
- No MVUX feeds/states/records were introduced in an MVVM feature.
