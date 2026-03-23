---
name: uno-responsive-spanning-gridwrap-layout
description: A responsive, non-virtualizing wrapping grid layout for ItemsRepeater that supports column spans, proportional stretch-to-fill, and vertically aligned gaps. Use when building dashboard-style or card-based layouts where items have varying widths but uniform height.
metadata:
  author: vhnet
  version: "1.0"
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

## Constraints

- Do NOT enable horizontal scrolling on the parent `ScrollViewer` — the layout needs a finite available width to calculate columns
- Non-virtualizing: suitable for moderate item counts (tens to low hundreds), not thousands
- All non-header items share the same `ItemHeight`
- If available width is infinite (unconstrained), falls back to 800px
