---
name: dotnet-livecharts2
description: LiveCharts2 development guide — installation, XAML source generator integration, theme config, gotchas, and sample index with exact repo file paths. Use when implementing any LiveCharts2 chart (line, area, bar, pie, gauge, heatmap, scatter, polar, financial). Covers all platforms (WinUI, Uno, Avalonia, MAUI, WPF, Blazor, WinForms, Eto).
metadata:
  author: https://github.com/VincentH-Net
  version: "1.4.1"
  library: LiveCharts2
  library-version: "2.0.0"
  category: charting
  sources:
    - LiveCharts2 GitHub repo
    - livecharts.dev documentation site
---

# LiveCharts2 Development Guide

Use when implementing charts, gauges, heatmaps, or any LiveCharts2 visualization.

## 1. Installation

NuGet package per platform:

| Platform | Package |
|---|---|
| Uno Platform | `LiveChartsCore.SkiaSharpView.Uno.WinUI` |
| WinUI 3 | `LiveChartsCore.SkiaSharpView.WinUI` |
| WPF | `LiveChartsCore.SkiaSharpView.WPF` |
| MAUI | `LiveChartsCore.SkiaSharpView.Maui` |
| Avalonia | `LiveChartsCore.SkiaSharpView.Avalonia` |
| Blazor | `LiveChartsCore.SkiaSharpView.Blazor` |

You MUST use the latest prerelease Nuget, E.g. for Uno Platform:
```bash
dotnet add package LiveChartsCore.SkiaSharpView.Uno.WinUI --prerelease
```

### XAML Namespace

**MUST use `LiveChartsCore.SkiaSharpView.WinUI`** — NOT `LiveChartsCore.SkiaSharpView.Uno.WinUI`:

```xml
xmlns:lvc="using:LiveChartsCore.SkiaSharpView.WinUI"
```

The Uno package re-exports from the WinUI namespace. Using the wrong namespace causes UXAML0001 build errors.

## 2. Series Approach: ViewModel-First vs XAML-First

Choose the right approach per chart type:

### ViewModel-first (preferred for most charts)

Build `ISeries[]` / `IReadOnlyList<ISeries>` in the ViewModel; XAML just binds `Series`, `XAxes`, `YAxes`:

```xml
<lvc:CartesianChart Series="{Binding Series}" XAxes="{Binding XAxes}" YAxes="{Binding YAxes}"
                    TooltipPosition="Top" AnimationsSpeed="00:00:00.400" ZoomMode="X"/>
```
```csharp
[ObservableProperty] public partial IReadOnlyList<ISeries> Series { get; set; }
public IReadOnlyList<Axis> XAxes { get; }
public IReadOnlyList<Axis> YAxes { get; }
```

Use ViewModel-first when:
- Data is computed/aggregated from injected services
- `Paint` objects need programmatic construction with helpers

### XAML-first (for gauges and simple static charts)

Use `SeriesCollection` / `AxesCollection` wrappers with `Xaml*` types:

```xml
<lvc:CartesianChart>
    <lvc:CartesianChart.Series>
        <lvc:SeriesCollection>
            <lvc:XamlLineSeries Values="{Binding Values}" Fill="{x:Null}" GeometrySize="20"/>
        </lvc:SeriesCollection>
    </lvc:CartesianChart.Series>
    <lvc:CartesianChart.XAxes>
        <lvc:AxesCollection>
            <lvc:XamlAxis Labels="{Binding Labels}" MinStep="1" ForceStepToMin="True"/>
        </lvc:AxesCollection>
    </lvc:CartesianChart.XAxes>
</lvc:CartesianChart>
```

Use XAML-first when:
- **Gauges** — layout is inherently visual (DrawMargin, DataLabelsPadding, InnerRadius)
- Simple static charts where colors don't need programmatic variants

Even in XAML-first gauges, Paint objects can be bound from ViewModel (`Fill="{Binding GaugeFill}"`) for dynamic colors.

**XAML series types**: `XamlLineSeries`, `XamlColumnSeries`, `XamlRowSeries`, `XamlStackedAreaSeries`, `XamlStackedColumnSeries`, `XamlStackedRowSeries`, `XamlStackedStepAreaSeries`, `XamlScatterSeries`, `XamlStepLineSeries`, `XamlPieSeries`, `XamlHeatSeries`, `XamlBoxSeries`, `XamlCandlesticksSeries`, `XamlPolarLineSeries`, `XamlGaugeSeries`, `XamlGaugeBackgroundSeries`, `XamlAngularGaugeSeries`, `XamlNeedle`, `XamlAngularTicks`.

**XAML axis types**: `XamlAxis`, `XamlDateTimeAxis`, `XamlTimeSpanAxis`, `XamlLogarithmicAxis`, `XamlPolarAxis`.

**XAML collection wrappers**: `SeriesCollection` (for `Chart.Series`), `AxesCollection` (for `Chart.XAxes`/`Chart.YAxes`).

### Markup extensions

- `{lvc:SolidColorPaint Color='#AARRGGBB'}` — solid fill/stroke paint
- `{lvc:Float Value='25'}` — float literal
- `{lvc:Padding Value='L,T,R,B'}` or `{lvc:Padding Value='15'}` — padding
- `{lvc:Margin Value='L,T,R,B'}` — chart DrawMargin override
- `{lvc:ColorArray Values='#FF4FC3F7, #FFFFF176, #FFFF5722'}` — color gradient (for `HeatMap` property)

**CRITICAL: `{lvc:SolidColorPaint}` only accepts the `Color` parameter.** Do NOT add `StrokeThickness` or other properties inside the markup extension (e.g. `Color='FF107C10', StrokeThickness=2`). The comma causes UXAML0001 parse errors. Set stroke thickness separately on the series or in C#.

### Anti-pattern: `AddDefaultTheme` overwrites explicit colors

`AddDefaultTheme` adds `HasRuleForLineSeries`/`HasRuleForBarSeries` etc. that unconditionally overwrite `Fill`/`Stroke` with palette colors, breaking explicit XAML `{lvc:SolidColorPaint}` values and ViewModel-set semantic colors. Use `HasTheme` directly with `HasDefaultTooltip`/`HasDefaultLegend` + selective `HasRuleFor*` to preserve explicit colors.

## 2b. Data Types

Choose the right data type for your series:

| Type | When to use | Example chart |
|---|---|---|
| `double` / `int` | Simple categorical data, gauge values, sparklines | Column chart, gauge `GaugeValue`, sparkline |
| `ObservableValue` | Single value needing live update via `.Value` setter | Row bar with real-time noise (use with `ObservableCollection<ObservableValue>`) |
| `ObservablePoint` | Explicit X/Y coordinates | Scatter, custom-positioned points |
| `DateTimePoint` | Time-series data (X = DateTime) | Power flow, energy balance, range forecast |
| `WeightedPoint` | X/Y/weight (3 dimensions) | Heatmap (`HeatSeries`), bubble scatter |
| `FinancialPoint` | OHLC candle data | Candlestick chart |

**CRITICAL: `XamlGaugeSeries.GaugeValue` expects `double`, NOT `ObservableValue`.** Binding to an `ObservableValue` object shows "0". Use `[ObservableProperty] double GaugeValue` with PropertyChanged notification. `ObservableValue` is a LiveCharts data container for `ISeries`-level data collections, not for XAML DependencyProperty bindings.



## 3. Theme Configuration


Call `LiveCharts.Configure()` once in `App.xaml.cs` (or a static method called from there) **before any chart control renders**. Typically in `OnLaunched` before `InitializeComponent`.

### Built-in palette

Built-in palettes e.g. `.FluentDesign` are defined in `LiveChartsCore.Themes.ColorPalletes` (note double 'l' in `Palletes`).

### Custom theme styling

Use `theme.OnInitialized(() => { ... })` inside `HasTheme` callback (fluent API). Use it to populate palette colors, animation defaults, and axis/legend/tooltip styling for the theme instance.


### Tooltips with custom themes

If you replace the default theme with `HasTheme(...)`, keep the tooltip/legend factories and register the `Hover` visual state yourself.

Why:
- `HasTheme(...)` starts from an empty `Theme`
- without `HasDefaultTooltip(() => new SKDefaultTooltip())`, hover can produce no tooltip
- without `series.HasState("Hover", ...)`, `Series.OnPointerEnter()` can throw `KeyNotFoundException: 'Hover'` before tooltips render

Safe pattern:

```csharp
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.VisualStates;

LiveCharts.Configure(config => config
    .HasTheme(theme =>
        theme
            .OnInitialized(() =>
            {
                // axis/tooltip styling here
            })
            .HasDefaultTooltip(() => new SKDefaultTooltip())
            .HasDefaultLegend(() => new SKDefaultLegend())
            .HasRuleForAnySeries(series =>
            {
                _ = series.HasState("Hover",
                [
                    (nameof(DrawnGeometry.Opacity), 0.8f)
                ]);
            })
    )
);
```

If tooltips still do not appear:
- verify you did not remove `HasDefaultTooltip`
- verify your replacement theme still defines the `Hover` state
- only then inspect `TooltipPosition`

### Theme rule methods reference

Use selective rules with `HasTheme` to control which series types get themed:

| Method | Purpose |
|---|---|
| `HasRuleForAxes(Action<IPlane>)` | Style all axes (labels, separators, name paint) |
| `HasRuleForAnySeries(Action<ISeries>)` | Style all series (data labels, hover state) |
| `HasRuleForLineSeries(...)` | Style line series — **overwrites Fill/Stroke** |
| `HasRuleForBarSeries(...)` | Style bar/column series — **overwrites Fill/Stroke** |
| `HasRuleForGaugeSeries(...)` | Style gauge foreground (DataLabelsPosition, CornerRadius) |
| `HasRuleForGaugeFillSeries(...)` | Style gauge background arc (Fill) |
| `HasRuleFor<BaseLabelVisual>(...)` | Style label visual elements |

**Only include `HasRuleForLineSeries`/`HasRuleForBarSeries` etc. if you want palette auto-coloring.** Omit them to preserve explicit series colors set in XAML or ViewModel.

## 4. Chart Layout Model

LiveCharts2 charts render inside a XAML control. The control's pixel size determines everything.
The chart engine divides the control area into **DrawMargin** (the plot area) and surrounding space (title, legend, axes). All drawing happens in device-independent pixels on a SkiaSharp canvas.

### CartesianChart layout

```
┌─────────────────────────────── ControlSize ─────────────────────────────┐
│ ┌─Title──────────────────────────────────────────────────────────────┐  │
│ └────────────────────────────────────────────────────────────────────┘  │
│         ┌──────────────── DrawMargin ───────────────────┐               │
│  Y-Axis │                                               │               │
│  Labels │          Series are drawn here                │               │
│  ◄─ls─► │          (plot area)                          │               │
│         │                                               │               │
│         └───────────────────────────────────────────────┘               │
│                    X-Axis Labels ◄─bs─►                                 │
│ ┌─Legend─────────────────────────────────────────────────────────────┐  │
│ └────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

- **ControlSize** = the XAML control's actual rendered pixel size
- **DrawMargin** = `ControlSize - (title + legend + axis labels + axis names)`
- By default, DrawMargin is **auto-calculated** from measured axis/title/legend sizes
- Override with `DrawMargin="left, top, right, bottom"` on the chart (values in px; use `Auto` per side to keep auto-calculation for that side)
- **Axis.Padding** adds extra padding inside the axis (between labels and plot edge)
- Series render within DrawMargin; they fill it completely (no extra inset)

**Key properties affecting CartesianChart size/position:**

| Property | On | Effect |
|---|---|---|
| `DrawMargin` | Chart | Override auto margin (px). `"Auto"` per side = auto |
| `LegendPosition` | Chart | `Hidden` / `Top` / `Bottom` / `Left` / `Right` — consumes space |
| `Title` | Chart | Title visual element — consumes top space |
| `TextSize` | Axis | Larger text = more margin consumed by labels |
| `LabelsRotation` | Axis | Rotated labels change measured height/width |
| `ShowSeparatorLines` | Axis | Visual only; no layout impact |
| `Name` / `NamePaint` | Axis | Axis name label — consumes additional margin |
| `IsVisible` | Axis | Hidden axis consumes no space |
| `Padding` | Axis (Cartesian) | Inner padding between labels and plot edge |

### PieChart layout

```
┌──────────────── ControlSize ──────────────────┐
│ ┌─Title─────────────────────────────────────┐ │
│ └───────────────────────────────────────────┘ │
│                                               │
│         ┌── DrawMargin ──┐                    │
│         │   ╭─────────╮  │                    │
│         │  ╱           ╲ │                    │
│         │ │  (cx, cy)   ││                    │
│         │  ╲           ╱ │                    │
│         │   ╰─────────╯  │                    │
│         └────────────────┘                    │
│                                               │
│ ┌─Legend────────────────────────────────────┐ │
│ └───────────────────────────────────────────┘ │
└───────────────────────────────────────────────┘
```

The pie/gauge is always **centered** in DrawMargin at `(cx, cy)`:
- `cx = DrawMarginLocation.X + DrawMarginSize.Width * 0.5`
- `cy = DrawMarginLocation.Y + DrawMarginSize.Height * 0.5`
- `radius = min(DrawMarginSize.Width, DrawMarginSize.Height) / 2` (square-fitted)

The pie always fits inside a **square** inscribed in DrawMargin. If DrawMargin is rectangular, extra space appears on the longer axis. This is why a 180° gauge wastes the bottom half.

**Key properties affecting PieChart size/position:**

| Property | On | Effect |
|---|---|---|
| `DrawMargin` | Chart | Override auto margin (px) |
| `LegendPosition` | Chart | Consumes space → shrinks DrawMargin → smaller radius |
| `Title` | Chart | Consumes top space |
| `InitialRotation` | Chart | Rotation offset in degrees (0° = 3 o'clock) |
| `MaxAngle` | Chart | Total sweep angle (360 = full, 180 = half) |
| `MaxValue` / `MinValue` | Chart | Scale range for gauge series |
| `InnerRadius` | Series | Absolute inner radius (px). Creates donut hole |
| `RelativeInnerRadius` | Series | Additional inner offset between stacked rings (px) |
| `RelativeOuterRadius` | Series | Outer offset between stacked rings (px) |
| `MaxRadialColumnWidth` | Series | Max arc thickness (px). Excess → inner/outer offset per `RadialAlign` |
| `RadialAlign` | Series | `Outer` / `Center` / `Inner` — where to put excess when `MaxRadialColumnWidth` constrains |
| `OuterRadiusOffset` | Series | Shrink outer radius by this many px |
| `Pushout` | Series | Explode slice outward by this many px |
| `CornerRadius` | Series | Round the arc endpoints |

### DataLabels rendering model

DataLabels are text elements drawn on the SkiaSharp canvas (not XAML elements).
Each label has a position `(X, Y)` computed from `DataLabelsPosition`, and a bounding box sized from the text + `DataLabelsPadding`. The box is aligned on `(X, Y)` using `HorizontalAlign` and `VerticalAlign` (both default to `Middle`):

```
           X (label position)
           │
     ┌─────┼─────┐ ← box top = Y - box.Height/2
     │ pad │ pad │
     │  "74"     │ ← text at Padding.Top from box top
     │           │
     │  (padding │
     │   bottom) │ ← extra bottom padding pushes text UP
     └───────────┘ ← box bottom = Y + box.Height/2
```

- `box.Height = textHeight + Padding.Top + Padding.Bottom`
- With `VerticalAlign=Middle`: box is centered on Y → `boxTop = Y - box.Height/2`
- Text renders at `boxTop + Padding.Top`
- **Asymmetric padding shifts text**: large `Padding.Bottom` → taller box → text moves up

**DataLabels properties (all series types):**

| Property | Type | Effect |
|---|---|---|
| `ShowDataLabels` | bool | Enable/disable labels |
| `DataLabelsPaint` | Paint | Color/style. Use `{lvc:SolidColorPaint}` in XAML (bindings may not work on XamlGaugeSeries) |
| `DataLabelsSize` | double | Font size in px |
| `DataLabelsRotation` | double | Rotation in degrees |
| `DataLabelsPadding` | Padding | Space around text. **Asymmetric values shift the visible text** relative to the anchor point. Use `{lvc:Padding Value='L,T,R,B'}` in XAML |
| `DataLabelsMaxWidth` | double | Max width before wrapping |
| `DataLabelsFormatter` | Func | Custom text formatting (C# only, not settable in XAML) |
| `DataLabelsPosition` | enum | Anchor point computation (see below) |

**DataLabelsPosition — Cartesian charts** (`DataLabelsPosition` enum):

| Value | Anchor point |
|---|---|
| `End` | End of bar/point in axis direction |
| `Start` | Start of bar/point in axis direction |
| `Middle` | Center of bar/point |
| `Top` / `Bottom` / `Left` / `Right` | Absolute direction |

**DataLabelsPosition — Pie/Gauge/Polar charts** (`PolarLabelsPosition` enum):

| Value | Anchor point |
|---|---|
| `ChartCenter` | `(cx, cy)` — geometric center of the chart |
| `Middle` | Midpoint of arc thickness, at mid-sweep angle |
| `Start` | Inner radius, at sweep start angle |
| `End` | Outer radius, at sweep end angle |
| `Outer` | Outside the arc, at mid-sweep angle |

### Combining DrawMargin + DataLabelsPadding for precise layout

When `DataLabelsPosition` doesn't place the label where you need it, combine:
1. **`DrawMargin`** with negative values to shift the chart geometry (cx, cy, radius)
2. **`DataLabelsPadding`** with asymmetric values to shift the visible text relative to the computed anchor

Example: 180° gauge filling a card with value text above the arc's flat edge:
- `DrawMargin="{lvc:Margin Value='-20,0,-20,-120'}"` — large arc, cy near bottom
- `DataLabelsPadding="{lvc:Padding Value='6,6,6,50'}"` — 50px bottom padding pushes text up from cy
- `DataLabelsPosition="ChartCenter"` — anchor at (cx, cy)
- Result: arc fills the card, value text sits inside the arc above the flat edge

```
┌─────── Card (200×130 chart area) ────┐
│  ╭━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╮     │ ← arc (DrawMargin widens it)
│  ┃  value arc    bg arc        ┃     │
│  ╰━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╯     │
│          "74"                        │ ← text shifted up by Padding
│ ···(cy + padding bottom: clipped)··· │
└──────────────────────────────────────┘
```

## 5. Gotchas & Patterns

### Gauges

**Anti-pattern: manual PieSeries gauge.** Do NOT create gauges with `new PieSeries<double> { Values = [75] }` + `new PieSeries<double> { Values = [25] }` (manually calculating the background remainder). This is fragile. Use `XamlGaugeSeries` + `XamlGaugeBackgroundSeries` instead — the background auto-fills the remaining arc. See sample **Pies/Gauge2**.

- **Top-half (speedometer) gauge**: `InitialRotation="180"`, `MaxAngle="180"`. NOT `-90` (that produces a right-side half). 0° = 3 o'clock in LiveCharts2.
- **Set `InnerRadius` on the series** (not the PieChart) to create the donut arc gap.
- **Fill a 180° gauge into its card**: use negative `DrawMargin` + `DataLabelsPadding` — see "Combining DrawMargin + DataLabelsPadding" in the Chart Layout Model section above.
- **Always set `LegendPosition="Hidden"` and `TooltipPosition="Hidden"`** on gauge PieCharts — default legend/tooltip are meaningless for gauges.
- **`AnimationsSpeed` as XAML TimeSpan string**: `AnimationsSpeed="00:00:00.500"` (500ms). Set on the PieChart control.
- **`GaugeValue` expects `double`** — NOT `ObservableValue`. Use `[ObservableProperty] double GaugeValue` in ViewModel.

```xml
<lvc:PieChart DrawMargin="{lvc:Margin Value='-20,0,-20,-120'}"
              InitialRotation="180" MaxAngle="180" MinValue="0" MaxValue="100"
              LegendPosition="Hidden" TooltipPosition="Hidden"
              AnimationsSpeed="00:00:00.500">
    <lvc:PieChart.Series>
        <lvc:SeriesCollection>
            <lvc:XamlGaugeSeries
                GaugeValue="{Binding GaugeValue}"
                InnerRadius="55"
                ShowDataLabels="True" DataLabelsSize="24"
                DataLabelsPadding="{lvc:Padding Value='6,6,6,50'}"
                DataLabelsPosition="ChartCenter"/>
            <lvc:XamlGaugeBackgroundSeries
                InnerRadius="55"/>
        </lvc:SeriesCollection>
    </lvc:PieChart.Series>
</lvc:PieChart>
```

### HeatSeries

- `HeatSeries` has **no `Stroke` property** — don't set it.
- Data: `WeightedPoint(x, y, weight)` where weight maps to color gradient.
- Gradient: `HeatMap = [coldColor.AsLvcColor(), ..., hotColor.AsLvcColor()]`.

### Real-time Charts

Use `PeriodicTimer` with `CancellationToken` and `IDisposable` for proper lifecycle:

```csharp
public sealed partial class PowerFlowChartViewModel : ObservableObject, IDisposable
{
    const int WindowMinutes = 240;
    readonly PeriodicTimer timer = new(TimeSpan.FromMilliseconds(5000));
    readonly CancellationTokenSource cts = new();

    public ObservableCollection<DateTimePoint> GenerationValues { get; } = [];
    [ObservableProperty] public partial IReadOnlyList<ISeries> Series { get; set; }
    public object Sync { get; } = new();  // thread-safety lock

    public PowerFlowChartViewModel()
    {
        SeedHistoricData();
        Series = BuildSeries();
        _ = TickLoop();
    }

    async Task TickLoop()
    {
        try { while (await timer.WaitForNextTickAsync(cts.Token)) AddDataPoint(); }
        catch (OperationCanceledException) { }
    }

    void AddDataPoint()
    {
        var now = DateTime.Now;
        GenerationValues.Add(new DateTimePoint(now, ComputeValue()));

        // Sliding window: trim old points
        var cutoff = now.AddMinutes(-WindowMinutes);
        while (GenerationValues.Count > 0 && GenerationValues[0].DateTime < cutoff)
            GenerationValues.RemoveAt(0);
    }

    public void Dispose() { cts.Cancel(); cts.Dispose(); timer.Dispose(); }
}
```

```xml
<lvc:CartesianChart Series="{Binding Series}" SyncContext="{Binding Sync}" ZoomMode="X"/>
```

Key points:
- **`ObservableCollection<DateTimePoint>`** for auto-updating time series
- **`SyncContext="{Binding Sync}"`** on the chart, where `Sync` is a shared `object`
- **`PeriodicTimer`** is preferred over `Task.Delay` loop (proper cancellation semantics)
- **Sliding window**: trim old points from index 0 to maintain bounded memory

### Axes

**DateTimeAxis in C#** (ViewModel-first approach):
```csharp
new DateTimeAxis(TimeSpan.FromHours(2), date => date.ToString("HH:mm", CultureInfo.InvariantCulture))
{
    TextSize = 11,
}
```

**DateTimeAxis in XAML** (`XamlDateTimeAxis`):
```xml
<lvc:XamlDateTimeAxis Interval="0:0:1"
                      DateFormatter="{Binding TimeFormatter}"
                      AnimationsSpeed="0"
                      CustomSeparators="{Binding Separators}"/>
```
- `AnimationsSpeed="0"` on axis: disables axis label animation for real-time charts (prevents jitter)
- `CustomSeparators`: bind to `double[]` of tick values

**Categorical axis** (named labels):
```xml
<lvc:XamlAxis Labels="{Binding CategoryLabels}" MinStep="1" ForceStepToMin="True"/>
```
- `MinStep="1"` + `ForceStepToMin="True"`: ensures one label per category (no interpolation)

**Common axis properties:**

| Property | Effect |
|---|---|
| `MinLimit` / `MaxLimit` | Clamp axis range |
| `MinStep` | Minimum step between labels |
| `ForceStepToMin` | Force step to exactly `MinStep` (no auto-scaling) |
| `IsVisible` | Hide axis (consumes no space) |
| `ShowSeparatorLines` | Toggle gridlines |
| `LabelsRotation` | Rotate labels in degrees (e.g. `-45`) |
| `AnimationsSpeed` | Axis animation speed (`"0"` to disable) |

- Axis separator paint for X-axis is typically `null` (no vertical gridlines); Y-axis gets horizontal gridlines.

## 6. Sample Index

Scan this index for keywords matching your chart type, then read the sample files using the paths in section 7.

### Lines (CartesianChart + LineSeries)
- **Lines/Basic** — Simple line chart, XamlLineSeries in XAML, custom geometry
- **Lines/Area** — Line with Fill (area chart), SolidColorPaint fill
- **Lines/Straight** — LineSmoothness=0 for straight segments
- **Lines/Custom** — Custom visual geometry per point
- **Lines/CustomPoints** — Custom drawn point shapes via SolidColorPaint
- **Lines/AutoUpdate** — ObservableCollection + ObservableValue for live updates
- **Lines/XY** — Explicit X/Y values via ObservablePoint
- **Lines/Zoom** — ZoomMode="X" or "Y" for pan/zoom
- **Lines/Padding** — Series padding configuration
- **Lines/Properties** — All LineSeries visual properties demo

### Stacked Area (CartesianChart + StackedAreaSeries)
- **StackedArea/Basic** — XamlStackedAreaSeries, multiple stacked fills
- **StackedArea/StepArea** — XamlStackedStepAreaSeries

### Bars / Columns (CartesianChart + ColumnSeries/RowSeries)
- **Bars/Basic** — XamlColumnSeries with axis labels, SolidColorPaint
- **Bars/RowsWithLabels** — Horizontal bars (XamlRowSeries) with data labels
- **Bars/Custom** — Custom bar geometry
- **Bars/Spacing** — Padding, MaxBarWidth
- **Bars/Layered** — Overlapping bars (not stacked)
- **Bars/WithBackground** — Background column behind data
- **Bars/AutoUpdate** — Live-updating bars with ObservableCollection
- **Bars/DelayedAnimation** — Per-series animation delay
- **Bars/Race** — Animated bar race chart with ObservableValue

### Stacked Bars (CartesianChart + StackedColumnSeries)
- **StackedBars/Basic** — XamlStackedColumnSeries
- **StackedBars/Groups** — Grouped stacked bars with axis labels

### Pie / Doughnut (PieChart + PieSeries)
- **Pies/Basic** — XamlPieSeries, SolidColorPaint, title
- **Pies/Doughnut** — InnerRadius for doughnut shape
- **Pies/Pushout** — Exploded slice
- **Pies/OutLabels** — Labels outside slices
- **Pies/Custom** — Custom slice geometry
- **Pies/Icons** — SVG label geometry on slices
- **Pies/Nested** — Multiple concentric rings
- **Pies/NightingaleRose** — Variable-radius slices
- **Pies/AutoUpdate** — Live-updating pie with ObservableCollection

### Gauges (PieChart + XamlGaugeSeries)
- **Pies/Gauge** — Basic gauge via ViewModel (PieSeries isGauge:true)
- **Pies/Gauge1** — XAML gauge: XamlGaugeSeries + XamlGaugeBackgroundSeries, basic
- **Pies/Gauge2** — XAML gauge: InnerRadius, ShowDataLabels, DataLabelsPosition=ChartCenter
- **Pies/Gauge3** — XAML gauge: multiple gauge series (multi-value)
- **Pies/Gauge4** — XAML gauge: slim arc, custom colors
- **Pies/Gauge5** — XAML gauge: no background, value-only
- **Pies/AngularGauge** — Full angular gauge: XamlAngularGaugeSeries, XamlNeedle, XamlAngularTicks

### Heat Map (CartesianChart + HeatSeries)
- **Heat/Basic** — XamlHeatSeries, WeightedPoint(x,y,weight), HeatMap gradient, axis labels

### Scatter (CartesianChart + ScatterSeries)
- **Scatter/Basic** — XamlScatterSeries
- **Scatter/Bubbles** — WeightedPoint for variable-size bubbles
- **Scatter/Custom** — Custom scatter geometry
- **Scatter/AutoUpdate** — Live-updating scatter

### Step Lines (CartesianChart + StepLineSeries)
- **StepLines/Basic** — XamlStepLineSeries with title
- **StepLines/Area** — Step line with fill
- **StepLines/AutoUpdate** — Live-updating step line

### Polar (PolarChart + PolarLineSeries)
- **Polar/Basic** — XamlPolarLineSeries, XamlPolarAxis, angle/radius axes
- **Polar/Coordinates** — Explicit polar coordinates
- **Polar/RadialArea** — Filled radar/spider chart

### Financial (CartesianChart + CandlesticksSeries)
- **Financial/BasicCandlesticks** — XamlCandlesticksSeries, FinancialPoint, DateTimeAxis

### Box Plot (CartesianChart + BoxSeries)
- **Box/Basic** — XamlBoxSeries with axis styling

### Error Bars
- **Error/Basic** — Error bars on line/column/scatter series

### Axes
- **Axes/DateTimeScaled** — XamlDateTimeAxis with DateTimePoint data
- **Axes/TimeSpanScaled** — XamlTimeSpanAxis
- **Axes/NamedLabels** — Custom string labels on axis
- **Axes/Logarithmic** — XamlLogarithmicAxis
- **Axes/Crosshairs** — Crosshair on hover
- **Axes/Multiple** — Multiple Y axes
- **Axes/Paging** — Scrollable axis window (MinLimit/MaxLimit)
- **Axes/Style** — Axis paint, separator paint, text size
- **Axes/LabelsRotation** — Rotated axis labels

### Design / Paint
- **Design/LinearGradients** — LinearGradientPaint on series
- **Design/RadialGradients** — RadialGradientPaint on pie slices
- **Design/StrokeDashArray** — Dashed stroke lines

### General Patterns
- **General/RealTime** — Real-time line chart with DateTimePoint, ObservableCollection, timer
- **General/Scrollable** — Scrollable chart with sections
- **General/Sections** — RectangularSection overlays
- **General/Tooltips** — Tooltip configuration
- **General/TemplatedTooltips** — Custom tooltip templates
- **General/Legends** — Legend position and styling
- **General/TemplatedLegends** — Custom legend templates
- **General/Visibility** — Show/hide series
- **General/VisualElements** — Drawn visual elements overlay
- **General/MultiThreading** — Thread-safe chart updates

### Maps
- **Maps/World** — GeoMap with HeatLandSeries

## 7. Reading Samples from the Repo

### Prerequisites

Clone once per machine (reuse across sessions):

```bash
[ -d /tmp/LiveCharts2 ] || git clone --depth 1 https://github.com/Live-Charts/LiveCharts2.git /tmp/LiveCharts2
```

### Workflow

1. **Find sample** in the index above
2. **Read ViewModel**: `cat /tmp/LiveCharts2/samples/ViewModelsSamples/{Category}/{Sample}/ViewModel.cs`
3. **Read View** (for your platform — see table below)
4. **Read helpers** if any: `ls /tmp/LiveCharts2/samples/WinUISample/WinUISample/Samples/{Category}/{Sample}/`
5. **Read docs** for property reference: `cat /tmp/LiveCharts2/docs/{topic}/{page}.md`

### ViewModel (shared across all platforms)

```
/tmp/LiveCharts2/samples/ViewModelsSamples/{Category}/{Sample}/ViewModel.cs
```

### Platform-specific View

| Platform | Path (under `/tmp/LiveCharts2/`) | Extension |
|---|---|---|
| WinUI / Uno | `samples/WinUISample/WinUISample/Samples/{Category}/{Sample}/View.xaml` | `.xaml` |
| Avalonia | `samples/AvaloniaSample/{Category}/{Sample}/View.axaml` | `.axaml` |
| MAUI | `samples/MauiSample/{Category}/{Sample}/View.xaml` | `.xaml` |
| WPF | `samples/WPFSample/{Category}/{Sample}/View.xaml` | `.xaml` |
| WinForms | `samples/WinFormsSample/{Category}/{Sample}/View.cs` | `.cs` |
| Blazor | `samples/BlazorSample/Pages/{Category}/{Sample}.razor` | `.razor` |
| Eto | `samples/EtoFormsSample/{Category}/{Sample}/View.cs` | `.cs` |

**Note**: Uno Platform reuses the WinUI sample XAML (same namespace `WinUISample.*`).

### Documentation files

Key doc files in `/tmp/LiveCharts2/docs/`:
- `overview/1.2.install.md` — Installation, first chart, SeriesSource/SeriesTemplate
- `overview/1.12.themes.md` — Theme configuration
- `overview/1.6.paint tasks.md` — SolidColorPaint, LinearGradient, RadialGradient
- `overview/1.4.automatic updates.md` — ObservableCollection auto-update
- `overview/1.9.animations.md` — Animation speed, easing
- `cartesianChart/overview.md` — CartesianChart control properties
- `cartesianChart/lineseries.md` — LineSeries properties
- `cartesianChart/columnseries.md` — ColumnSeries/RowSeries properties
- `cartesianChart/heatseries.md` — HeatSeries, HeatMap gradient, WeightedPoint
- `cartesianChart/axes.md` — Axis config, DateTimeAxis, labels, separators
- `piechart/overview.md` — PieChart control properties
- `piechart/pieseries.md` — PieSeries properties
- `piechart/gauges.md` — Gauge patterns (XamlGaugeSeries, angular gauge, needle)
- `polarchart/overview.md` — PolarChart, PolarAxis

### Source code (for unstuck)

- `src/skiasharp/_Shared.WinUI/` — CartesianChart.cs, PieChart.cs, ThemeListener.cs
- `src/skiasharp/_Shared.Xaml/Series.cs` — All Xaml series types
- `src/skiasharp/_Shared/` — Source generators (SourceGenCartesianChart, SourceGenPieChart)
- `src/LiveChartsCore/Themes/` — ColorPalletes.cs, Theme.cs
- `src/skiasharp/LiveChartsCore.SkiaSharp/ThemesExtensions.cs` — AddDefaultTheme, HasRuleFor*

## 8. Fallback: Website Access

If you need content only available on the website (e.g. rendered screenshots, API browser):

```bash
# Fetch any page:
curl -s "https://livecharts.dev/docs/UnoWinUi/{library-version}/{page}"
```

Website platform URL prefixes: `UnoWinUi`, `avalonia`, `blazor`, `maui`, `wpf`, `winforms`, `winui`, `eto`
