---
name: uno-fluent2
description: Fluent 2 Design System for Uno Platform. Use when designing UI layouts, choosing colors, applying typography, setting elevation/shadows, using theme resources, applying lightweight styling, or implementing Fluent Design principles in WinUI/Uno XAML apps. Covers color, typography, geometry, materials, motion, iconography, spacing, elevation, and responsive breakpoints.
metadata:
  author: vhnet
  version: "1.1"
  framework: uno-platform
  category: design-system
  sources:
    - Microsoft Learn (windows/apps/design/)
    - Uno.Themes (GitHub)
    - uno.toolkit.ui (GitHub)
    - Uno Platform docs
---

# WinUI Fluent 2 Design System for Uno Platform

Authoritative design guidance for building WinUI apps with the Fluent 2 design language on Uno Platform. Distilled from Microsoft Learn, Uno.Themes, and uno.toolkit.ui source.

> **Scope**: This skill covers Fluent (WinUI native) theming. For Material Design theming, see the `uno-material-*` skills instead. Check `.csproj` `<UnoFeatures>` to determine which theme your project uses.

---

## 1. Color System

### Principles
- Color helps users focus by indicating visual hierarchy and structure
- Use color sparingly and meaningfully to emphasize important elements
- Respect user theme preferences (Light/Dark)
- Never hardcode hex colors in XAML; always use `{ThemeResource}`

### Color Modes
Windows supports **Light** and **Dark** themes. Darker colors = less important surfaces; lighter/brighter = more important.

### Accent Color
- `SystemAccentColor` — user-chosen or app-overridden accent
- Accent palette shades: `SystemAccentColorLight1` through `Light3`, `SystemAccentColorDark1` through `Dark3`
- Override in App.xaml: `<Color x:Key="SystemAccentColor">#107C10</Color>`

### Key Theme Brush Resources (WinUI)
Use `{ThemeResource BrushName}` in XAML:

**Text Brushes:**
| Key | Purpose |
|-----|---------|
| `TextFillColorPrimaryBrush` | Primary text |
| `TextFillColorSecondaryBrush` | Secondary text |
| `TextFillColorTertiaryBrush` | Tertiary/hint text |
| `TextFillColorDisabledBrush` | Disabled text |
| `AccentTextFillColorPrimaryBrush` | Accent-colored text |

**Surface/Background Brushes:**
| Key | Purpose |
|-----|---------|
| `SolidBackgroundFillColorBaseBrush` | Base background |
| `SolidBackgroundFillColorSecondaryBrush` | Secondary background |
| `SolidBackgroundFillColorTertiaryBrush` | Tertiary background |
| `LayerFillColorDefaultBrush` | Content layer fill |
| `CardBackgroundFillColorDefaultBrush` | Card backgrounds |
| `CardBackgroundFillColorSecondaryBrush` | Secondary card backgrounds |

**Control Brushes:**
| Key | Purpose |
|-----|---------|
| `ControlFillColorDefaultBrush` | Default control fill |
| `ControlFillColorSecondaryBrush` | Secondary control fill |
| `ControlFillColorDisabledBrush` | Disabled control fill |
| `ControlStrokeColorDefaultBrush` | Default control border |
| `ControlStrokeColorSecondaryBrush` | Secondary control border |
| `SubtleFillColorTransparentBrush` | Subtle/transparent fills |
| `SubtleFillColorSecondaryBrush` | Subtle hover state |

**Semantic/State Brushes:**
| Key | Purpose |
|-----|---------|
| `SystemFillColorSuccessBrush` | Success state |
| `SystemFillColorCautionBrush` | Warning state |
| `SystemFillColorCriticalBrush` | Error/critical state |
| `SystemFillColorAttentionBrush` | Attention-required state |

### Theme Dictionary Best Practices
```xaml
<!-- CORRECT: Use separate Light and Dark dictionaries -->
<ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Light">
        <SolidColorBrush x:Key="MyBrush" Color="{StaticResource TextFillColorPrimary}"/>
    </ResourceDictionary>
    <ResourceDictionary x:Key="Dark">
        <SolidColorBrush x:Key="MyBrush" Color="{StaticResource TextFillColorPrimary}"/>
    </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>

<!-- WRONG: Do NOT use "Default" key for Light/Dark; it causes theme-switching bugs -->
<!-- WRONG: Do NOT use {ThemeResource} inside ThemeDictionaries; use {StaticResource} -->
```

### Usability
- Ensure 4.5:1 contrast ratio for body text, 3:1 for large text
- **Non-text contrast**: 3:1 minimum for UI component boundaries (borders, strokes, icons). Custom border brushes must be contrast-checked against both the card background AND the page background in both Light and Dark themes — colors that look acceptable often fail WCAG 2.2 non-text contrast.
- Consider colorblindness (8% of men are red-green colorblind)
- Avoid using color as the sole differentiator between elements

---

## 2. Typography

### Font
**Segoe UI Variable** is the system font. It uses variable font technology with two axes:
- **Weight**: Thin (100) to Bold (700)
- **Optical size**: automatic, optimizes shape from 8pt to 36pt

### Type Ramp (TextBlock Styles)
All sizes are in effective pixels (epx). Use `Style="{StaticResource StyleName}"`:

| Style Key | Weight | Size/Line Height | Use For |
|-----------|--------|-----------------|---------|
| `CaptionTextBlockStyle` | Regular | 12/16 epx | Labels, footnotes |
| `BodyTextBlockStyle` | Regular | 14/20 epx | Body text (default) |
| `BodyStrongTextBlockStyle` | Semibold | 14/20 epx | Emphasized body |
| `BodyLargeTextBlockStyle` | Regular | 18/24 epx | Large body text |
| `SubtitleTextBlockStyle` | Semibold | 20/28 epx | Section headings |
| `TitleTextBlockStyle` | Semibold | 28/36 epx | Page/card titles |
| `TitleLargeTextBlockStyle` | Semibold | 40/52 epx | Hero text |
| `DisplayTextBlockStyle` | Semibold | 68/92 epx | Display/splash |

### Typography Best Practices
- Use **Regular** weight for body, **Semibold** for titles
- Left-align by default; center only for text below icons
- Minimum: 14px Semibold or 12px Regular
- Sentence case for all UI text including titles (never ALL CAPS for body)
- Line length: 50-60 characters (max 120 on desktop)
- Line height: ~140% of font size, always a multiple of 4
- Use ellipses (`TextTrimming="CharacterEllipsis"`) when text overflows

### XAML Example
```xaml
<TextBlock Text="Page Title" Style="{StaticResource TitleTextBlockStyle}"
           Foreground="{ThemeResource TextFillColorPrimaryBrush}"/>
<TextBlock Text="Supporting text" Style="{StaticResource BodyTextBlockStyle}"
           Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
<TextBlock Text="Caption" Style="{StaticResource CaptionTextBlockStyle}"
           Foreground="{ThemeResource TextFillColorTertiaryBrush}"/>
```

---

## 3. Geometry (Shapes & Corner Radius)

### Rounded Corners
| Corner Radius | Usage |
|---------------|-------|
| **8px** | Top-level containers: app windows, flyouts, dialogs |
| **4px** | In-page elements: buttons, list backplates, cards |
| **0px** | Edges intersecting other straight edges |
| **0px** | Maximized/snapped windows |

### XAML
```xaml
<!-- Dialog/flyout level -->
<Border CornerRadius="8"/>

<!-- Control level -->
<Border CornerRadius="4"/>
```

---

## 4. Elevation & Layering

### Elevation Values
| Surface | Elevation | Stroke Width | Use Case |
|---------|-----------|-------------|----------|
| Window | 128 | 1px | App window |
| Dialog | 128 | 1px | Modal dialogs |
| Flyout | 32 | 1px | Menus, popups |
| Tooltip | 16 | 1px | Tooltips |
| Card | 8 | 1px | Content cards |
| Control | 2 | 1px | Buttons (rest) |
| Layer | 1 | 1px | Base layers |

### Control States
- **Rest**: Elevation 2, Stroke 1
- **Hover**: Elevation 2, Stroke 1
- **Pressed**: Elevation 1, Stroke 1

### Two-Layer System
- **Base layer**: App foundation — menus, commands, navigation
- **Content layer**: Central experience — content cards, detail views

### XAML (Uno Platform ThemeShadow)
```xaml
<!-- Card elevation -->
<Border CornerRadius="4" Translation="0,0,8">
    <Border.Shadow>
        <ThemeShadow/>
    </Border.Shadow>
    <!-- Card content -->
</Border>

<!-- Flyout elevation -->
<Border CornerRadius="8" Translation="0,0,32">
    <Border.Shadow>
        <ThemeShadow/>
    </Border.Shadow>
</Border>
```

---

## 5. Materials

### Material Types
| Material | Type | Use Case | Theme-Aware |
|----------|------|----------|-------------|
| **Mica** | Opaque | App base layer; tinted with desktop wallpaper | Yes (Light + Dark + focus state) |
| **Acrylic** | Semi-transparent | Transient surfaces: flyouts, context menus | Yes (Light + Dark) |
| **Smoke** | Transparent | Dimming behind modal dialogs | No (always dark translucent) |

### Uno Platform Notes
- Mica and Acrylic have limited/no support on Skia targets
- Use solid fallback colors when targeting cross-platform
- Prefer `SolidBackgroundFillColorBaseBrush` as fallback

---

## 6. Iconography

### Segoe Fluent Icons
- Monoline style (1 epx stroke)
- Design principles: Minimal, Harmonious, Evolved
- Icon sizing: Font glyph footprint = square em (16px font = 16x16 icon)

### Icon Usage
```xaml
<!-- Symbol icon -->
<SymbolIcon Symbol="Home"/>

<!-- Font icon (Segoe Fluent Icons) -->
<FontIcon Glyph="&#xE80F;" FontFamily="{ThemeResource SymbolThemeFontFamily}"/>

<!-- Emoji fallback when asset missing -->
<FontIcon Glyph="&#x1F4CA;"/>
```

### Best Practices
- Use base + modifier pattern for compound meanings
- Validate cultural connotations of symbols
- Layer two glyphs for active/selected states

---

## 7. Motion

### Animation Properties
| Purpose | Ease | Timing | Used For |
|---------|------|--------|----------|
| Direct Entrance | cubic-bezier(0,0,0,1) | 167/250/333ms | Position, Scale, Rotation |
| Point to Point | cubic-bezier(0.55,0.55,0,1) | 167/250/333ms | Existing element movement |
| Direct Exit | cubic-bezier(0,0,0,1) | 167ms | Exit (combine with fade out) |
| Gentle Exit | cubic-bezier(1,0,1,1) | 167ms | Soft dismissal |
| Fade In/Out | Linear | 83ms | Opacity changes |

### Principles
- **Connected**: Elements visually connect between states
- **Consistent**: Shared entry points invoke/dismiss the same way
- **Responsive**: System adapts to input method
- **Delightful**: Brief moments of personality
- **Resourceful**: Use built-in WinUI controls (page transitions, connected animations)

---

## 8. Layout & Spacing

### Spacing Scale (multiples of 4)
Use these values for Margin, Padding, Spacing: **4, 8, 12, 16, 24, 32, 48, 64**

### Sizing Grid
- **Standard**: 40x40 epx grid — touch + pointer
- **Compact**: Available via `ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml` — pointer only
- All sizes in **multiples of 4 epx** for clean scaling

### Responsive Breakpoints
| Size Class | Width | Typical Devices | Columns | Gutter |
|------------|-------|-----------------|---------|--------|
| Small | < 640px | Phones, TVs (10ft) | 1-4 | 16px |
| Medium | 641-1007px | Tablets | 8 | 16px |
| Large | >= 1008px | PC, Laptop | 12 | 20-24px |

### Alignment
- Text/content never touches screen edge (Margin >= 16px)
- Inner padding for text/icon surfaces; images may be edge-to-edge
- Use `HorizontalContentAlignment` and `VerticalContentAlignment` on ContentControls

---

## 9. Lightweight Styling (Uno Platform)

Lightweight styling overrides control appearance by providing alternate resources with the same key, without redefining the full style.

### Override Levels
1. **App level**: `AppResources.xaml` — affects all instances globally
2. **Page level**: `Page.Resources` — scoped to one page
3. **Control level**: `Control.Resources` — one specific instance

### Resource Key Pattern
Keys follow: `{Style}{Property}{VisualState}`

Visual states: (none)=Normal, `PointerOver`, `Pressed`, `Disabled`, `Focused`, `Checked`, `CheckedPointerOver`, etc.

### Example
```xaml
<!-- Override Button appearance per-control -->
<Button Content="Custom" Style="{StaticResource FilledButtonStyle}">
    <Button.Resources>
        <SolidColorBrush x:Key="FilledButtonForeground" Color="DarkGreen"/>
        <SolidColorBrush x:Key="FilledButtonBackground" Color="LightGreen"/>
        <SolidColorBrush x:Key="FilledButtonBorderBrush" Color="DarkGreen"/>
    </Button.Resources>
</Button>
```

### Fluent Theme Setup (Uno)
When using the Uno SDK (`dotnet new unoapp`), App.xaml is **auto-generated** based on `<UnoFeatures>` in your `.csproj`. You do NOT need to manually add these entries — the template handles it:

```xaml
<!-- Auto-generated in App.xaml by Uno SDK template -->
<ResourceDictionary.MergedDictionaries>
    <!-- Always present for WinUI Fluent controls -->
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls"/>
    <!-- Present only when Toolkit is in <UnoFeatures> -->
    <ToolkitResources xmlns="using:Uno.Toolkit.UI"/>
</ResourceDictionary.MergedDictionaries>
```

> **Note**: Only add these manually if upgrading a legacy Uno project (pre-SDK) or if the template didn't generate them.

### WinUI Controls — Lightweight Styling Keys
Controls with documented lightweight styling: Button, CalendarDatePicker, CheckBox, ComboBox, DatePicker, HyperlinkButton, NavigationView, PasswordBox, PipsPager, ProgressBar, ProgressRing, RadioButton, RatingControl, Slider, TextBlock, TextBox, ToggleButton, ToggleSwitch.

Common key patterns for **Button**:
- `ButtonForeground`, `ButtonBackground`, `ButtonBorderBrush`
- `ButtonForegroundPointerOver`, `ButtonBackgroundPointerOver`
- `ButtonForegroundPressed`, `ButtonBackgroundPressed`
- `ButtonForegroundDisabled`, `ButtonBackgroundDisabled`

For styled variants (Filled, Outlined, Text): prefix with style name:
- `FilledButtonForeground`, `FilledButtonBackground`
- `OutlinedButtonForeground`, `OutlinedButtonBackground`
- `TextButtonForeground`, `TextButtonBackground`

### NavigationView — Full-Bleed Header Overrides

When using `NavigationView` as the app shell with a custom top bar in `NavigationView.Header`, the default chrome adds padding and rounded corners that conflict with a full-bleed header design. Override these three keys at app level:

```xaml
<!-- In App.xaml resources -->
<Thickness x:Key="NavigationViewHeaderMargin">0</Thickness>
<Thickness x:Key="NavigationViewContentGridBorderThickness">0</Thickness>
<CornerRadius x:Key="NavigationViewContentGridCornerRadius">0</CornerRadius>
```

### Reusable Card Container Style

A shared card style combining border, elevation, theme-aware background, and content alignment. Define in `App.xaml` resources:

```xaml
<Style x:Key="AppCardContainerStyle" TargetType="ContentControl">
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="VerticalContentAlignment" Value="Stretch"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ContentControl">
                <Border CornerRadius="4"
                        Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}"
                        BorderThickness="1" Padding="16" Translation="0,0,8">
                    <Border.Shadow><ThemeShadow/></Border.Shadow>
                    <ContentPresenter Content="{TemplateBinding Content}"
                                      HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                      VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

Usage: wrap card content in `<ContentControl Style="{StaticResource AppCardContainerStyle}">`.

### Transparent Icon Button Style (Header Bar)

For icon buttons (notifications, profile, settings) in a top bar:

```xaml
<Style x:Key="TopBarIconButtonStyle" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderBrush" Value="Transparent"/>
    <Setter Property="Padding" Value="8"/>
    <Setter Property="MinWidth" Value="36"/>
    <Setter Property="MinHeight" Value="36"/>
</Style>
```

### Custom App Theme Color Dictionary

Apps typically need custom theme-aware brushes beyond WinUI defaults. Pattern:

1. Create `Themes/AppColors.xaml` with `ThemeDictionaries` containing Light and Dark `ResourceDictionary` entries
2. **Must have a code-behind** (`.xaml.cs`) — required by Uno to load the XAML resource
3. Include in `App.xaml` via typed reference: `<themes:AppColors/>` in `MergedDictionaries`
4. Naming convention: `{App}{Surface}{Purpose}Brush` (e.g. `MyAppCardBorderBrush`, `MyAppTopBarBackgroundBrush`)

```xaml
<ResourceDictionary x:Class="MyApp.Themes.AppColors" ...>
    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
            <!-- Override accent color for brand identity -->
            <Color x:Key="SystemAccentColor">#0C54B0</Color>
            <Color x:Key="SystemAccentColorLight1">#285EE4</Color>
            <!-- App surfaces -->
            <SolidColorBrush x:Key="MyAppPageBackgroundBrush" Color="#F3F3F3"/>
            <SolidColorBrush x:Key="MyAppCardBorderBrush" Color="#E0E0E0"/>
            <SolidColorBrush x:Key="MyAppTopBarBackgroundBrush" Color="#FFFFFF"/>
            <!-- Semantic status -->
            <SolidColorBrush x:Key="MyAppStatusOnlineBrush" Color="#107C10"/>
            <SolidColorBrush x:Key="MyAppStatusWarningBrush" Color="#CA5010"/>
            <SolidColorBrush x:Key="MyAppStatusCriticalBrush" Color="#D13438"/>
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
            <!-- Lighter accent for dark backgrounds -->
            <Color x:Key="SystemAccentColor">#5A8AF0</Color>
            <Color x:Key="SystemAccentColorLight1">#94B4F5</Color>
            <!-- App surfaces -->
            <SolidColorBrush x:Key="MyAppPageBackgroundBrush" Color="#1C1C28"/>
            <SolidColorBrush x:Key="MyAppCardBorderBrush" Color="#3E3E50"/>
            <SolidColorBrush x:Key="MyAppTopBarBackgroundBrush" Color="#1E1E2E"/>
            <!-- Semantic status (lighter for contrast on dark) -->
            <SolidColorBrush x:Key="MyAppStatusOnlineBrush" Color="#6CCB5F"/>
            <SolidColorBrush x:Key="MyAppStatusWarningBrush" Color="#F7A94B"/>
            <SolidColorBrush x:Key="MyAppStatusCriticalBrush" Color="#FF6B6B"/>
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

---

## 10. Uno Toolkit — Lightweight Styling Keys

### Card / CardContentControl
Variants: Filled, Outlined, Elevated (also Avatar and SmallMedia sub-variants)

| Key Pattern | States |
|-------------|--------|
| `FilledCardBackground` | (none), PointerOver, Focused |
| `FilledCardBorderBrush` | (none), PointerOver, Focused |
| `OutlinedCardBackground` / `OutlinedCardBorderBrush` | same |
| `ElevatedCardBackground` / `ElevatedCardBorderBrush` | same |
| `FilledCardContentBackground` / `...BorderBrush` | (none), PointerOver, Focused, Pressed |
| `OutlinedCardContentBackground` / `...BorderBrush` | same |
| `ElevatedCardContentBackground` / `...BorderBrush` | same |
| `ContentTemplateForeground`, `ContentTemplateBorderBrush` | — |

### Chip
| Key Pattern | States |
|-------------|--------|
| `ChipForeground` | (none), PointerOver, Focused, Pressed, Disabled, Checked, Checked+state |
| `ChipBackground` | same |
| `ChipBorderBrush` | same |
| `ChipIconForeground` | same + Checked variants |
| `ChipDeleteIconBackground`, `ChipDeleteIconForeground` | — |
| `ChipStateOverlay` | all states |
| Sizing: `ChipHeight`, `ChipIconSize`, `ChipCornerRadius`, `ChipPadding`, `ChipBorderThickness` | — |

### Divider
`DividerForeground`, `DividerSubHeaderForeground`, `DividerSubHeaderFontFamily`, `DividerSubHeaderFontWeight`, `DividerSubHeaderFontSize`, `DividerSubHeaderCharacterSpacing`, `DividerSubHeaderMargin`, `DividerHeight`

### NavigationBar
`NavigationBarForeground`, `NavigationBarBackground`, `NavigationBarMainCommandForeground`, `NavigationBarPadding`, `NavigationBarFontFamily`, `NavigationBarFontWeight`, `NavigationBarFontSize`

### TabBar
Key pattern: `TabBarItem{Property}{State}` — use the Uno Toolkit docs or `uno-toolkit-tabbar` skill for the full list.

---

## 11. Quick Reference — Choosing the Right Resource

| Need | Resource/Approach |
|------|-------------------|
| Page background | `SolidBackgroundFillColorBaseBrush` |
| Card background | `CardBackgroundFillColorDefaultBrush` |
| Primary text | `TextFillColorPrimaryBrush` + `BodyTextBlockStyle` |
| Secondary text | `TextFillColorSecondaryBrush` + `CaptionTextBlockStyle` |
| Section heading | `TextFillColorPrimaryBrush` + `SubtitleTextBlockStyle` |
| Page title | `TextFillColorPrimaryBrush` + `TitleTextBlockStyle` |
| Interactive accent | `SystemAccentColor` (via `AccentTextFillColorPrimaryBrush`) |
| Success/Warning/Error | `SystemFillColorSuccessBrush/CautionBrush/CriticalBrush` |
| Button | Default implicit style or `FilledButtonStyle` |
| Card elevation | `ThemeShadow` + `Translation="0,0,8"` |
| Divider line | `DividerForeground` or `ControlStrokeColorDefaultBrush` |
| Corner radius (control) | `CornerRadius="4"` |
| Corner radius (dialog) | `CornerRadius="8"` |
| Spacing | Multiples of 4: 4, 8, 12, 16, 24, 32 |

---

## 12. Uno Platform Compatibility Notes

| Feature | Uno/Skia Support |
|---------|-----------------|
| ThemeShadow | Full cross-platform support |
| Mica material | Not supported on Skia; use solid fallback |
| Acrylic material | Limited on Skia; use solid fallback |
| Segoe UI Variable | Available on Windows; other platforms use system font |
| Segoe Fluent Icons | Provided via `Uno.Fonts.Fluent` NuGet package |
| Light/Dark theme switching | Full support |
| HighContrast theme | Windows only |
| TextBlock type ramp styles | Full support |
| ThemeResource brushes | Full support |
| Lightweight styling | Full support (both Uno.Themes and Uno.Toolkit) |
| Compact density | Supported |
| `AutomationProperties.HeadingLevel` | **Not supported** on Skia (Uno0001 build error). Use `AutomationProperties.Name` instead. |
| `WrapGrid.Orientation` | **Not supported** on Skia (Uno0001 build error). Use default orientation or a different panel. |
| Nested `ItemsRepeater` | Inner repeater gets 0 width/height on Skia — layout collapses. Flatten to single repeater with `DataTemplateSelector`. |

---

## References
- [MS Learn: Design Windows apps](https://learn.microsoft.com/en-us/windows/apps/design/)
- [MS Learn: XAML Theme Resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources)
- [Fluent 2 Design Language](https://fluent2.microsoft.design/)
- [Uno.Themes Lightweight Styling](https://platform.uno/docs/articles/external/uno.themes/doc/lightweight-styling.html)
- [Uno.Toolkit Lightweight Styling](https://platform.uno/docs/articles/external/uno.toolkit.ui/doc/lightweight-styling.html)
