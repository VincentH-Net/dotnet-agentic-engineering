---
name: uno-responsive-spanning-gridwrap-layout
description: A responsive, non-virtualizing wrapping grid layout for ItemsRepeater that supports column spans, proportional stretch-to-fill, and vertically aligned gaps. Use when building dashboard-style or card-based layouts where items have varying widths but uniform height.
metadata:
  author: vhnet
  version: "1.1"
  category: controls
  platform: cross-platform
---

# ResponsiveSpanningGridWrapLayout

A custom `NonVirtualizingLayout` for `ItemsRepeater` that arranges fixed-height items in a responsive wrapping grid with column span support.

## Reference implementation

Copy [ResponsiveSpanningGridWrapLayout.cs](ResponsiveSpanningGridWrapLayout.cs) into your project's controls folder and update the namespace.

## Features

- Flows items left-to-right, top-to-bottom with automatic row wrapping
- Per-item column spans via attached property
- Proportional stretch-to-fill eliminates remaining horizontal space per row
- Vertical gap alignment across rows (last item in each row grows to close any gap)
- Full-width header rows: items spanning >= maxColumns get auto-height measurement
- Responsive column count derived from available width and `MinColumnWidth`

## Layout properties

| Property | Type | Default | Description |
|---|---|---|---|
| `MinColumnWidth` | double | 200 | Minimum width per column; determines column count |
| `ColumnSpacing` | double | 8 | Horizontal gap between items |
| `RowSpacing` | double | 8 | Vertical gap between rows |
| `ItemHeight` | double | 200 | Fixed height for all non-header items |

## Attached property

| Property | Type | Default | Description |
|---|---|---|---|
| `ColumnSpan` | int | 1 | Number of columns an item spans. Values > maxColumns become full-width headers |

## XAML usage

```xml
<ItemsRepeater ItemsSource="{x:Bind ViewModel.Items}">
    <ItemsRepeater.Layout>
        <local:ResponsiveSpanningGridWrapLayout
            MinColumnWidth="250"
            ColumnSpacing="12"
            RowSpacing="12"
            ItemHeight="180" />
    </ItemsRepeater.Layout>
    <ItemsRepeater.ItemTemplate>
        <DataTemplate x:DataType="models:MyItem">
            <Border
                local:ResponsiveSpanningGridWrapLayout.ColumnSpan="{x:Bind Span}"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                CornerRadius="8"
                Padding="16">
                <!-- item content -->
            </Border>
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

## Heterogeneous items with DataTemplateSelector

Nested `ItemsRepeater` controls don't work on Uno Skia (inner repeater gets 0 width/height). For pages with different card types (metrics, charts, group headers), flatten all items into a single `List<object>` and use a `DataTemplateSelector`:

```csharp
// ViewModel builds a flat interleaved list of different item types
public List<object> PageItems { get; } = [
    new MetricViewModel { Title = "Power", Value = "342 kW", Span = 1 },
    new MetricViewModel { Title = "Energy", Value = "1.2 MWh", Span = 1 },
    new ChartViewModel { Title = "Power Flow", ChartType = "area" },   // span 2
    new GroupHeaderViewModel { Title = "Devices" },                     // full-width
    new DeviceViewModel { Name = "Inverter 1" },
    new DeviceViewModel { Name = "Battery 1" },
];
```

```csharp
// DataTemplateSelector routes to the right template by item type
public class CardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Metric { get; set; }
    public DataTemplate? Chart { get; set; }
    public DataTemplate? GroupHeader { get; set; }
    public DataTemplate? Device { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        MetricViewModel => Metric,
        ChartViewModel => Chart,
        GroupHeaderViewModel => GroupHeader,
        DeviceViewModel => Device,
        _ => null,
    };
}
```

```xml
<Page.Resources>
    <DataTemplate x:Key="MetricTemplate">
        <Border local:ResponsiveSpanningGridWrapLayout.ColumnSpan="{Binding Span}">
            <ContentControl Style="{StaticResource AppCardContainerStyle}">
                <cards:MetricCard/>
            </ContentControl>
        </Border>
    </DataTemplate>

    <DataTemplate x:Key="ChartTemplate">
        <Border local:ResponsiveSpanningGridWrapLayout.ColumnSpan="2">
            <ContentControl Style="{StaticResource AppCardContainerStyle}">
                <cards:ChartCard/>
            </ContentControl>
        </Border>
    </DataTemplate>

    <!-- Full-width header: span >= maxColumns triggers auto-height -->
    <DataTemplate x:Key="GroupHeaderTemplate">
        <Border local:ResponsiveSpanningGridWrapLayout.ColumnSpan="99">
            <TextBlock Text="{Binding Title}"
                       Style="{StaticResource SubtitleTextBlockStyle}"
                       Margin="0,8,0,0"/>
        </Border>
    </DataTemplate>

    <DataTemplate x:Key="DeviceTemplate">
        <ContentControl Style="{StaticResource AppCardContainerStyle}">
            <cards:DeviceCard/>
        </ContentControl>
    </DataTemplate>
</Page.Resources>

<ItemsRepeater ItemsSource="{Binding PageItems}">
    <ItemsRepeater.Layout>
        <local:ResponsiveSpanningGridWrapLayout
            MinColumnWidth="220" ColumnSpacing="12" RowSpacing="12" ItemHeight="180"/>
    </ItemsRepeater.Layout>
    <ItemsRepeater.ItemTemplate>
        <cards:CardTemplateSelector
            Metric="{StaticResource MetricTemplate}"
            Chart="{StaticResource ChartTemplate}"
            GroupHeader="{StaticResource GroupHeaderTemplate}"
            Device="{StaticResource DeviceTemplate}"/>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

### Key details

1. **`ColumnSpan` must go on the direct child of `ItemsRepeater`** (typically the outer `Border`). `ItemsRepeater` only reads attached properties on direct children — putting it on an inner element has no effect.
2. **Header rows**: Items with `ColumnSpan` >= maxColumns become full-width and get auto-height measurement instead of the fixed `ItemHeight`. Use a large value like `99` for group headers.
3. **Card container wrapping**: Wrap card UserControls in a shared card container style (see `uno-fluent2` skill §9) for consistent elevation, border, corner radius, and padding.

## DataContext scoping gotcha

When using `ContentControl` inside templates, do NOT set `DataContext` on the `ContentControl` itself if it also has a `Visibility` binding:

```xml
<!-- WRONG: Visibility binds to Child.IsVisible (wrong scope) -->
<ContentControl DataContext="{Binding Child}" Visibility="{Binding IsVisible}">

<!-- CORRECT: Set DataContext on the UserControl inside -->
<ContentControl Visibility="{Binding IsVisible}">
    <cards:MyCard DataContext="{Binding Child}"/>
</ContentControl>
```

WHY: `DataContext` changes the binding scope BEFORE `Visibility` evaluates. The Visibility property looks up `IsVisible` on `Child` (wrong object) instead of the parent item, causing ghost content from silently failed bindings that default to `Visible`.

## Constraints

- Do NOT enable horizontal scrolling on the parent `ScrollViewer` — the layout needs a finite available width to calculate columns
- Non-virtualizing: suitable for moderate item counts (tens to low hundreds), not thousands
- All non-header items share the same `ItemHeight`
- If available width is infinite (unconstrained), falls back to 800px
