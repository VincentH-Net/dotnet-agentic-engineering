---
name: uno-xaml
description: "Uno Platform XAML correctness and performance guidance for XAML markup profiles: deferred loading with x:Load, virtualized item-template mechanics, UI-bound lifecycle cleanup, UI-thread safety at the Page/control boundary, input scopes, keyboard accelerators, focus, and drag/drop caveats. Use when the selected markup type is XAML or when existing XAML needs performance, lifecycle, input, or template fixes. Do NOT use for MVVM property/command patterns, Uno Navigation, Fluent or Material theming, visual design/layout composition, Uno C# Markup, or C# Markup 2; combine with the selected update-model, design-system, navigation, and markup skills instead."
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0"
  framework: uno-platform
  category: xaml
  sources:
    - https://github.com/mtmattei/UnoPlatformSkills/tree/main/winui-xaml
    - Microsoft Learn WinUI XAML guidance
    - Uno Platform compatibility caveats
---

# Uno Platform XAML

Use this skill for XAML-only correctness and performance details that are not owned by the selected update model, design system, navigation, or layout skill.

## Composition Rules

- If the user selected C# Markup or C# Markup 2 for a view, do not apply this skill to that view's markup syntax.
- For MVVM bindings, generated properties, commands, and ViewModel structure, use `uno-mvvm`.
- For MVUX feeds, states, records, commands, and generated ViewModels, use the selected MVUX skills.
- For Uno Extensions Navigation, do not introduce `Frame.Navigate()` patterns. Use `INavigator`, route maps, `Region.*`, and `Navigation.*` guidance from the selected navigation/update-model skill.
- For Fluent or Material resource keys, theme dictionaries, typography, colors, shadows, and lightweight styling, use the selected design-system skill. Do not copy generic WinUI theming snippets into a themed Uno app.
- For responsive visual composition, spacing, margins, accessibility naming, and localization naming, follow the Uno MCP platform usage rules and the selected design-system skill.

## Binding Boundaries

- Prefer `x:Bind` only where the source is strongly typed and stable, such as a Page-level `ViewModel` property or a `DataTemplate` with a valid `x:DataType`.
- Use ordinary `{Binding}` where Uno template scope requires it, such as nested template item sources, ancestor bindings, or command bindings that need a control `DataContext`.
- Do not use `{Binding StringFormat=...}`. Use multiple `Run` elements or expose a computed property from the selected update model.
- Avoid converter stacks for view state that belongs in the ViewModel/model. Use a converter only for view-only type conversion that cannot reasonably live in the update model.

## Deferred Loading

Use `x:Load` for expensive optional XAML that is not needed at startup, such as advanced settings panels, secondary tab content, help overlays, or infrequently opened details regions.

```xml
<Grid x:Name="AdvancedPanel"
      x:Load="{x:Bind ViewModel.ShowAdvancedPanel, Mode=OneWay}">
    <!-- Expensive optional content -->
</Grid>
```

Rules:

- Prefer `Visibility` for cheap, frequently toggled state such as validation messages, simple placeholders, and selected visual states.
- Prefer `x:Load` when delaying construction is the goal; unloaded content releases the element tree and its state.
- `x:Load` requires a named element and cannot be used on the root element or inside a `ResourceDictionary`.
- Be careful with `ElementName` bindings to unloaded elements. If another element binds to a deferred element, load order can break the binding.
- Do not use legacy `x:DeferLoadStrategy`; use `x:Load`.
- Verify `x:Load` behavior on all target Uno platforms when the loaded subtree contains platform-specific controls, focus targets, popups, WebView, media, or third-party controls.

## Virtualized Templates

For large linear lists, use virtualized controls such as `ListView` or `GridView`. For custom dashboard grids, wrapping cards, or mixed-width tiles, use the existing responsive ItemsRepeater skill instead of generic WinUI list patterns.

Rules:

- Do not wrap a virtualized `ListView` or `GridView` in a `ScrollViewer`; it disables or harms virtualization.
- Give virtualized items stable dimensions where practical. Variable-height templates make measuring expensive and scrolling less predictable.
- Keep item templates shallow. Move repeated chrome into shared styles/templates and avoid nested panels that do not add layout value.
- Avoid `x:Name` on elements inside virtualized `DataTemplate`s unless there is no alternative. Named template elements are per-container and easy to misuse.
- Prefer binding and commands inside templates. Use code-behind template events only for view-only mechanics that cannot be expressed as a command or behavior.
- Use `x:Phase` only for complex `ListView`/`GridView` item templates after confirming support on the target Uno platforms. Do not apply it to `ItemsRepeater` dashboard layouts by default.
- Use `DataTemplateSelector` for heterogeneous item types, but keep selector logic type-based and cheap. Do not perform service calls, visual tree inspection, or layout measurements in selector logic.

## UI Thread and Async Safety

XAML views and controls are UI-thread-bound. Keep async work non-blocking and keep dispatcher usage at the view/control boundary.

Rules:

- Never block the UI thread with `.Result`, `.Wait()`, `Thread.Sleep`, or synchronous I/O.
- Use `await` all the way through UI-triggered work.
- Use `Task.WhenAll` for independent asynchronous operations; sequence only when one result depends on another.
- Use `DispatcherQueue.TryEnqueue` only when a background callback must update a UI element directly.
- Do not put `DispatcherQueue`, `Page`, `Button`, `TextBox`, or visual tree access in ViewModels. Route state through commands, services, or observable properties instead.
- In non-UI library code, `ConfigureAwait(false)` is acceptable. Do not use it in UI code before touching XAML controls.
- Avoid `async void` except for required UI event handlers. In those handlers, catch/report failures and delegate real work to commands or services.

## UI Lifecycle Cleanup

Use code-behind only for UI-only lifecycle mechanics, platform interop, or control events that cannot be represented cleanly as commands.

Rules:

- Unsubscribe UI event handlers in `Unloaded` when the publisher can outlive the view.
- Stop and detach `DispatcherQueueTimer` instances in `Unloaded`.
- Cancel in-flight UI-owned operations when the view unloads.
- Dispose UI-owned `IDisposable` resources such as streams, subscriptions, and temporary media objects.
- Avoid lambdas that capture `this` for long-lived events. Prefer named handlers that can be removed, or weak subscriptions when the publisher is application-scoped.
- Page cleanup must not dispose shared services from dependency injection; only clean resources owned by the view instance.

```csharp
public sealed partial class DetailsPage : Page
{
    CancellationTokenSource? loadCts;

    public DetailsPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        loadCts?.Cancel();
        loadCts?.Dispose();
        loadCts = null;
    }
}
```

## Input Affordances

Add input details that improve correctness without changing the selected design system.

Rules:

- Set `InputScope` on text inputs where the expected value is known.

```xml
<TextBox Text="{x:Bind ViewModel.Email, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         InputScope="EmailSmtpAddress" />

<TextBox Text="{x:Bind ViewModel.Phone, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         InputScope="TelephoneNumber" />

<TextBox Text="{x:Bind ViewModel.Quantity, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         InputScope="Number" />
```

- Use `KeyboardAccelerator` for common page or menu actions on desktop-class experiences. Route the invoked handler to a command or service; keep the handler thin.
- Preserve focus order. When deferred content loads, explicitly set focus only when it matches the user's workflow, such as opening a search panel or dialog.
- Use pointer events for custom pointer-specific UI only. Prefer built-in controls, commands, and gestures before custom pointer handling.
- Drag/drop usually requires UI events. Keep drag/drop code in the view boundary, pass IDs or small DTOs, and call commands/services for the actual mutation. Test drag/drop on every target platform before relying on it.

## Rendering And Visibility

- Use `Visibility="Collapsed"` instead of `Opacity="0"` when hidden content should not participate in hit testing, accessibility, or layout.
- Use `Opacity` only for visual fades where the element should remain present during the animation.
- Use `x:Load` rather than `Visibility` when expensive optional UI should not be created until needed.
- Do not add blur, Acrylic, Mica, custom shadows, or theme resource guidance from generic WinUI examples. Follow the selected design-system skill and Uno platform compatibility notes.

## Review Checklist

- XAML view uses the selected update model's binding and command pattern.
- XAML view does not introduce `Frame.Navigate()` in an Uno Extensions Navigation app.
- Theme dictionaries and resource keys come only from the selected design-system skill.
- Deferred UI uses `x:Load` only where construction cost justifies it.
- Virtualized controls are not wrapped in `ScrollViewer`.
- Template code avoids unnecessary `x:Name`, deep trees, and expensive selector logic.
- UI-owned events, timers, and cancellation tokens are cleaned up on unload.
- UI thread is never blocked.
- Text inputs have appropriate `InputScope`.
- Keyboard, focus, pointer, and drag/drop logic remains at the view boundary and delegates business state changes.
