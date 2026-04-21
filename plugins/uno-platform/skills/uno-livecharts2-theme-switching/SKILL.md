---
name: uno-livecharts2-theme-switching
description: Extends dotnet-livecharts2 for Uno Platform apps that need reliable in-app dark/light/system theme switching with LiveCharts2, shared theme palettes, central refresh of already-loaded charts, and rendered-pixel verification of chart text colors after theme changes.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0"
  framework: uno-platform
  category: theming
---

# Uno LiveCharts2 Theme Switching

Use with `dotnet-livecharts2`, not instead of it.

Use when:
- app uses Uno Platform + LiveCharts2
- app supports in-app theme switching, not just startup theme selection
- chart colors/text must follow shared light/dark palettes from XAML resources
- already-visited pages keep stale axis/legend/gauge text after a theme switch

Do NOT assume LiveCharts2 will follow Uno in-app theme switching automatically.

Why:
- LiveCharts WinUI theme detection keys off `Application.Current.RequestedTheme`
- Uno apps often switch theme through `IThemeService`
- on some Uno paths, `IThemeService` updates app theme state but does not update `Application.Current.RequestedTheme`
- result: newly created charts can look correct, while already-loaded charts keep stale theme text/legend/axis paints

## Recommended pattern

Use one app-owned theme source of truth plus one central chart refresh.

Do:
1. Keep shared chart palette colors in XAML resources with Light/Dark theme dictionaries.
2. Build a reusable `ChartPalette` helper that resolves resource brushes/colors for a requested dark/light theme.
3. Build a reusable `ChartThemeConfig` with:
   - `Initialize()` for global `LiveCharts.Configure(...)`
   - `Create(requestedTheme)` for per-control reassignment
4. Publish the effective chart theme from the app theme toggle itself.
5. On that signal, walk the live visual tree from the current shell/root content and refresh visible charts.
6. Reapply theme to each chart by:
   - updating `LiveCharts.DefaultSettings.GetTheme().RequestedTheme`
   - assigning `chart.ChartTheme = ChartThemeConfig.Create(requestedTheme)` when needed
   - calling `chart.CoreChart.ApplyTheme()`

Prefer this over:
- per-chart registration lists
- wrapper control proliferation
- reflection into LiveCharts private listeners

## Shared palette

Keep chart colors in the same light/dark resource system as the rest of the app.

Pattern:
- XAML resources define brushes for chart text, axis separators, tooltip background, gauge track, and semantic series colors
- code resolves those brushes for either Light or Dark explicitly
- LiveCharts theme config consumes those resolved colors

Minimal shape:

```csharp
static class ChartPalette
{
    public static SKColor ThemeColor(string brushKey, bool isDark) { /* resolve Light/Dark brush */ }

    public static SolidColorPaint ThemeSolid(string brushKey, bool isDark, byte? alpha = null) { /* ... */ }

    public static SolidColorPaint ThemeStroke(string brushKey, bool isDark, float thickness = 2) { /* ... */ }
}
```

Keep semantic series tags/tokens separate from theme brushes:
- theme brushes: text, separators, tooltip, gauge track, card/background
- series tokens: generation, consumption, storage, heat levels, etc.

Important:
- use the same themed XAML chart series brushes as the source of truth for the LiveCharts default palette too
- do NOT keep a second hardcoded dark palette in C# if those series brushes already exist in theme dictionaries
- otherwise explicit series paints and default/untagged series can drift apart

## Theme factory

Use a reusable theme factory instead of ad hoc per-chart paint mutation.

Required hookup:
- call `ChartThemeConfig.Initialize()` once during app startup, before charts render
- if app uses Uno `IThemeService`, make sure host setup includes `.UseThemeSwitching()`

Minimal shape:

```csharp
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.Themes;
using LiveChartsCore.VisualStates;

static class ChartThemeConfig
{
    public static void Initialize() =>
        LiveCharts.Configure(config => config.HasTheme(Apply));

    public static Theme Create(LvcThemeKind requestedTheme)
    {
        var theme = new Theme { RequestedTheme = requestedTheme };
        Apply(theme);
        return theme;
    }

    static void Apply(Theme theme) =>
        theme
            .OnInitialized(() =>
            {
                theme.Colors = ChartPalette.DefaultSeriesPalette(theme.IsDark);
                theme.VirtualBackroundColor = ToLvcColor("ChartSurfaceBrush", theme.IsDark);
                theme.TooltipTextPaint = ChartPalette.ThemeSolid("ChartTextPrimaryBrush", theme.IsDark);
                theme.TooltipBackgroundPaint = ChartPalette.ThemeSolid("ChartTooltipBackgroundBrush", theme.IsDark);
                theme.LegendTextPaint = ChartPalette.ThemeSolid("ChartTextPrimaryBrush", theme.IsDark);
            })
            .HasDefaultTooltip(() => new SKDefaultTooltip())
            .HasDefaultLegend(() => new SKDefaultLegend())
            .HasRuleForAxes(axis =>
            {
                axis.NamePaint = ChartPalette.ThemeSolid("ChartTextPrimaryBrush", theme.IsDark);
                axis.LabelsPaint = ChartPalette.ThemeSolid("ChartTextSecondaryBrush", theme.IsDark);
                axis.SeparatorsPaint = ChartPalette.ThemeStroke("ChartAxisSeparatorBrush", theme.IsDark, 1);
            })
            .HasRuleForAnySeries(series =>
            {
                if (series.ShowDataLabels)
                    series.DataLabelsPaint = ChartPalette.ThemeSolid("ChartTextPrimaryBrush", theme.IsDark);

                _ = series.HasState("Hover",
                [
                    (nameof(DrawnGeometry.Opacity), 0.8f)
                ]);
            });
}
```

Notes:
- use `HasTheme(...)` directly for custom Uno theme control
- include hover state registration or pointer hover can fail before tooltip render
- use explicit theme brushes for text paints; do not depend on default WinUI detection
- derive `theme.Colors` from the same themed `ChartSeries...Brush` resources used elsewhere in the app

## App-owned theme signal

Publish the effective chart theme from the same code path that performs the app theme switch.

Why:
- this is the closest reliable source of truth
- framework theme events can disagree with what LiveCharts needs
- `RequestedTheme`, `ActualThemeChanged`, or `ThemeChanged` may not be sufficient alone in Uno

Minimal shape:

```csharp
using LiveChartsCore.Themes;

static class LiveChartsThemeState
{
    public static event EventHandler<LvcThemeKind>? Changed;

    public static LvcThemeKind CurrentRequestedTheme { get; private set; } = LvcThemeKind.Light;

    public static void Set(bool isDark) =>
        Set(isDark ? LvcThemeKind.Dark : LvcThemeKind.Light);

    public static void Set(LvcThemeKind requestedTheme)
    {
        if (CurrentRequestedTheme == requestedTheme)
            return;

        CurrentRequestedTheme = requestedTheme;
        Changed?.Invoke(null, requestedTheme);
    }
}
```

Publish from the theme toggle after `SetThemeAsync(...)` completes:

```csharp
[RelayCommand]
async Task ToggleTheme()
{
    _ = await themeService.SetThemeAsync(nextTheme);
    LiveChartsThemeState.Set(themeService.IsDark);
}
```

If app has `System`, `Light`, `Dark`:
- keep the 3-state UI logic in app code
- chart theme state should still publish the effective dark/light result used for chart paints

Also seed the initial chart theme state before the first toggle.

Minimal shape:

```csharp
void OnLoaded(object sender, RoutedEventArgs e)
{
    var themeService = this.GetThemeService();
    LiveChartsThemeState.Set(themeService.IsDark);
    // subscribe and refresh after this
}
```

If your app keeps a separate 3-state toggle index for `System/Light/Dark`, initialize that from `themeService.Theme` too, before handling toggle clicks.

## Startup hookup

Initialize the chart theme system once during app startup.

Minimal shape:

```csharp
public partial class App : Application
{
    public App()
    {
        ChartThemeConfig.Initialize();
        InitializeComponent();
    }
}
```

If using Uno host builder theme switching:

```csharp
var builder = this.CreateBuilder(args)
    .Configure(host => host
        .UseThemeSwitching()
        // other setup
    );
```

## Shell XAML hookup

The central refresh example assumes:
- a named shell/root visual such as `NavView`
- a named active content region such as `MainRegion`

Minimal shape:

```xml
<Page
    x:Class="MyApp.Presentation.MainPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:uen="using:Uno.Extensions.Navigation.UI">

    <Grid uen:Region.Attached="True">
        <NavigationView x:Name="NavView"
                        uen:Region.Attached="True">

            <!-- shell header/menu -->

            <Grid x:Name="MainRegion"
                  uen:Region.Attached="True"
                  uen:Region.Navigator="Visibility"/>
        </NavigationView>
    </Grid>
</Page>
```

## Central refresh

Refresh charts from one root location in the live tree.

Recommended:
- subscribe once from the shell/root page on `Loaded`
- refresh on app-owned chart theme state change
- also refresh when the active content region swaps to another cached page

Minimal shape:

```csharp
using LiveChartsCore.SkiaSharpView.WinUI;
using LiveChartsCore.Themes;
using Microsoft.UI.Xaml.Media;

public sealed partial class MainPage : Page
{
    DependencyObject? activeRegionChild;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        var themeService = this.GetThemeService();
        LiveChartsThemeState.Set(themeService.IsDark);
        LiveChartsThemeState.Changed += OnChartThemeChanged;
        MainRegion.LayoutUpdated += OnMainRegionLayoutUpdated;
        RefreshChartThemes();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LiveChartsThemeState.Changed -= OnChartThemeChanged;
        MainRegion.LayoutUpdated -= OnMainRegionLayoutUpdated;
        activeRegionChild = null;
    }

    void OnChartThemeChanged(object? sender, LvcThemeKind args) =>
        RefreshChartThemes();

    void OnMainRegionLayoutUpdated(object? sender, object args)
    {
        var currentRegionChild = VisualTreeHelper.GetChildrenCount(MainRegion) > 0
            ? VisualTreeHelper.GetChild(MainRegion, 0)
            : null;

        if (ReferenceEquals(currentRegionChild, activeRegionChild))
            return;

        activeRegionChild = currentRegionChild;
        RefreshChartThemes();
    }

    void RefreshChartThemes()
    {
        var requestedTheme = LiveChartsThemeState.CurrentRequestedTheme;
        LiveChartsCore.LiveCharts.DefaultSettings.GetTheme().RequestedTheme = requestedTheme;
        RefreshChartThemes(NavView, requestedTheme);
    }

    static void RefreshChartThemes(DependencyObject root, LvcThemeKind requestedTheme)
    {
        if (root is CartesianChart cartesianChart)
            ApplyTheme(cartesianChart, requestedTheme);
        else if (root is PieChart pieChart)
            ApplyTheme(pieChart, requestedTheme);

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
            RefreshChartThemes(VisualTreeHelper.GetChild(root, i), requestedTheme);
    }

    static void ApplyTheme(CartesianChart chart, LvcThemeKind requestedTheme)
    {
        if (chart.ChartTheme?.RequestedTheme != requestedTheme)
            chart.ChartTheme = ChartThemeConfig.Create(requestedTheme);

        chart.CoreChart.ApplyTheme();
    }

    static void ApplyTheme(PieChart chart, LvcThemeKind requestedTheme)
    {
        if (chart.ChartTheme?.RequestedTheme != requestedTheme)
            chart.ChartTheme = ChartThemeConfig.Create(requestedTheme);

        chart.CoreChart.ApplyTheme();
    }
}
```

Root selection rules:
- start from shell content, page content, or active content region
- do not start from a `Page` instance if its content is elsewhere
- include the currently visible region tree, not just newly navigated pages
- if your shell root is a named `NavigationView`, walk that root directly instead of an unnamed placeholder object

Required hookups in this pattern:
- constructor hooks `Loaded` / `Unloaded`
- XAML defines the named shell root and named active content region used by the code-behind
- `OnLoaded` subscribes `LiveChartsThemeState.Changed` and region/layout change signal
- `OnLoaded` seeds `LiveChartsThemeState` from the current effective theme before refresh
- `OnUnloaded` unsubscribes them
- app startup calls `ChartThemeConfig.Initialize()`
- app theme toggle calls `LiveChartsThemeState.Set(...)`

## Why this pattern works

It fixes both cases:
- newly created charts: pick up correct theme from `ChartThemeConfig.Create(...)`
- already-loaded charts: get explicit theme reassignment plus `ApplyTheme()`

It also avoids coupling to LiveCharts private listeners.

## Anti-patterns

Avoid these unless forced by a framework constraint:

- relying on `Application.Current.RequestedTheme` to prove Uno theme switch propagation
- assuming first-visit-after-switch success means cached pages are fixed
- assigning `ChartTheme` once at load time and expecting it to survive later theme switches
- mixing a central tree-walk refresh with per-chart `ActualThemeChanged` sync logic
- per-chart registration/unregistration registries when one root refresh is enough
- reflection into private LiveCharts theme listeners
- verifying theme success from shell chrome, card backgrounds, or non-chart controls
- taking screenshots immediately after the click if the app shows a brief correct flash before stale paints return

## Verification

Required verification: rendered chart text pixels, not state variables.

Test matrix:
1. start app in one theme
2. visit at least one chart page
3. switch theme while that chart page is visible
4. verify current page updates without navigation
5. revisit a previously visited chart page
6. if app has `System/Light/Dark`, exercise the full cycle needed to reach the intended effective theme

Screenshot guidance:
- after each switch, wait until the UI settles before capture
- if there is any delayed repaint risk, use a delayed screenshot around 1 second after the switch
- capture the chart region large enough to include axis labels, legend text, gauge labels, or data labels

What to inspect:
- in light theme, chart text pixels should be dark enough against light surfaces
- in dark theme, chart text pixels should be light enough against dark surfaces
- inspect actual glyph pixels from axis labels, legend labels, gauge center labels, or visible data labels

Reliable verification methods:
- visually compare delayed screenshots of the same chart before/after switch
- crop the chart text area and inspect whether glyph pixels move from mostly light to mostly dark, or vice versa
- if automating, sample pixels from known text-glyph rows/columns inside the chart text region, not from margins

Unreliable verification methods to avoid:
- checking only `themeService.IsDark`
- checking only `Application.Current.RequestedTheme`
- checking only `ActualTheme`
- checking only the app background/card color
- checking only newly visited pages after the switch
- checking only an immediate post-click screenshot when a later repaint can overwrite it
- inferring success because chart lines/bars changed while text stayed stale

## Troubleshooting

Symptom: pages first visited after switch are correct, visited pages stale.

Meaning:
- initial chart creation path is fine
- live refresh path is missing or keyed off the wrong theme signal

Symptom: chart text flips correctly for a moment, then reverts to the opposite theme.

Meaning:
- a later repaint is still using stale global theme state
- ensure both are updated:
  - app-owned requested chart theme
  - `LiveCharts.DefaultSettings.GetTheme().RequestedTheme`

Symptom: current page updates, but previously visited cached pages revert when revisited.

Meaning:
- refresh is only hitting current chart instances
- also refresh when the active content region swaps to another live child

## Decision rule

For Uno in-app theme switching with shared palettes:
- prefer shared palette + theme factory + app-owned theme signal + central live-tree refresh
- use rendered-pixel verification as the acceptance test
