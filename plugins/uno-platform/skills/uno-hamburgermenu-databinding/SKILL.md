---
name: uno-hamburgermenu-databinding
description: This skill demonstrates how to use the Uno Navigation Extensions `NavigationView` and MVVM to create a data-bound, hierarchical hamburger menu with dynamic navigation. Use when implementing a data-bound hamburger menu.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.1"
  framework: uno-platform
  category: navigation
---

# Uno Navigation Extensions: NavigationView with data-bound hierarchical hamburger menu

This skill demonstrates how to use the Uno Navigation Extensions with a `NavigationView` to create a data-bound, hierarchical hamburger menu with dynamic navigation.
Author: https://github.com/VincentH-Net

## Overview

The pattern uses:
- **`NavigationView`** with `uen:Region.Attached="true"` for Uno Navigation integration
- **Data-bound menu items** via `MenuItemsSource` bound to an `ObservableCollection<NavMenuItem>`
- **Hierarchical (nested) menus** using a `Children` property on menu items
- **Footer menu items** via `FooterMenuItemsSource` for settings/utility links
- **Two-way `SelectedItem` binding** to track and control the selected menu item
- **`uen:Region.Name` and `uen:Navigation.Data`** on each `NavigationViewItem` for route-based navigation with data passing

## NavMenuItem Model

Define a simple model to represent each menu entry, including support for child items:

```csharp
using System.Collections.ObjectModel;

public sealed class NavMenuItem
{
    public string Route { get; init; } = "";
    public object? Data { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string IconGlyph { get; init; } = "";
    public string? ToolTip { get; init; }
    public ObservableCollection<NavMenuItem>? Children { get; init; }
}
```

Key points:
- `Route` maps to the Uno Navigation region name (e.g. `"Main/Home"`)
- `Data` carries navigation data passed to the target page's viewmodel
- `Children` enables nested/expandable menu items (set to `[]` for a parent that has children, `null` for a leaf item)

## XAML: MainPage

```xml
<Page x:Class="MyApp.Presentation.MainPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:local="using:MyApp.Presentation"
      xmlns:uen="using:Uno.Extensions.Navigation.UI">

    <Grid uen:Region.Attached="True">
        <NavigationView PaneDisplayMode="LeftCompact"
                        IsSettingsVisible="False"
                        uen:Region.Attached="true"
                        x:Name="NavView"
                        IsBackEnabled="False"
                        IsBackButtonVisible="Collapsed"
                        MenuItemsSource="{Binding MenuItems}"
                        FooterMenuItemsSource="{Binding FooterMenuItems}"
                        SelectedItem="{Binding SelectedMenuItem, Mode=TwoWay}">

            <!-- Optional: Pane header branding -->
            <NavigationView.PaneHeader>
                <StackPanel>
                    <TextBlock Text="App Name" />
                    <TextBlock Text="App Subtitle" />
                </StackPanel>
            </NavigationView.PaneHeader>

            <!-- Optional: Header with page top bar for e.g. page title and icons for flyouts -->
            <NavigationView.Header>
            </NavigationView.Header>

            <!-- Template for data-bound menu items -->
            <NavigationView.MenuItemTemplate>
                <DataTemplate x:DataType="local:NavMenuItem">
                    <NavigationViewItem uen:Region.Name="{x:Bind Route}"
                                        uen:Navigation.Data="{x:Bind Data}"
                                        Tag="{x:Bind Title}"
                                        ToolTipService.ToolTip="{x:Bind ToolTip}"
                                        MenuItemsSource="{Binding Children}">
                        <NavigationViewItem.Icon>
                            <FontIcon Glyph="{x:Bind IconGlyph}"/>
                        </NavigationViewItem.Icon>
                        <NavigationViewItem.Content>
                            <StackPanel>
                                <TextBlock Text="{x:Bind Title}"/>
                                <TextBlock Text="{x:Bind Subtitle}"/>
                            </StackPanel>
                        </NavigationViewItem.Content>
                    </NavigationViewItem>
                </DataTemplate>
            </NavigationView.MenuItemTemplate>

            <!-- Content area where navigated pages appear -->
            <Grid uen:Region.Attached="True"
                  uen:Region.Name="Main"
                  uen:Region.Navigator="Visibility" />
        </NavigationView>
    </Grid>
</Page>
```

### Critical XAML details

1. **Outer `Grid` needs `uen:Region.Attached="True"`** to establish the navigation region hierarchy.
2. **`NavigationView` also needs `uen:Region.Attached="true"`** so it participates in routing.
3. **`MenuItemTemplate`** must use a `NavigationViewItem` (not just content) with:
   - `uen:Region.Name="{x:Bind Route}"` for navigation routing
   - `uen:Navigation.Data="{x:Bind Data}"` — **REQUIRED** when passing typed data. Without it, the clicked `NavigationViewItem` container becomes the navigation data instead of the intended payload.
   - `MenuItemsSource="{Binding Children}"` to enable nested child items (note: uses `{Binding}` not `{x:Bind}` due to known Uno Platform x:Bind source generator issues in nested DataTemplate contexts — see unoplatform/uno#7279, #18509, #8471)
4. **Content region** uses `uen:Region.Navigator="Visibility"` so pages are shown/hidden rather than recreated.

### Critical routing details

1. **Default routes**: The shell page and the default page within it must be registered with `IsDefault: true` in their `RouteMap` constructor. This ensures the landing page displays on initial navigation.
2. **Fully qualified route names**: All leaf menu item routes MUST include the region prefix (e.g. `"Main/Home"`, `"Main/Settings"`). Do NOT use relative names. Mixed/relative names cause context-sensitive failures — submenu children can navigate at shell scope instead of within the content region, or header selection changes while content stays on the prior page.
3. **`NavigationViewNavigator` enumerates top-level only**: Route-to-selected-item synchronization only walks `MenuItems` + `FooterMenuItems` — it does NOT recursively descend into nested submenu children. Keep nesting to **one level maximum** or accept that selection highlight may desync from the actual navigated page.

### Flyout command bindings in NavigationView.Header (Uno Skia)

When adding flyouts (notifications, profile, org switcher) in `NavigationView.Header`, **command bindings fail silently on Uno Skia**. Flyout content renders in `PopupRoot` where DataContext resolves to the shell's ViewModel, not the expected scope. Data bindings work (read-only) but commands resolve to null.

**Fix**: Wrap the command in a data model so it travels with the data through the Flyout boundary:

```csharp
public sealed class ProfileInfo
{
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public IRelayCommand LogoutCommand { get; init; } = null!;
}
```

In the ViewModel, create the model with the command embedded:
```csharp
ProfileInfo = new ProfileInfo { Name = user.Name, LogoutCommand = LogoutCommand };
```

In the Flyout XAML, bind through the data model:
```xml
<Button Content="Log out" Command="{Binding ProfileInfo.LogoutCommand}"/>
```

Do NOT use Click handlers as a workaround.

### Sibling route navigation

`NavigateViewModelAsync` resolves routes within the current navigator scope (child routes only). To navigate to a **sibling route** (e.g. from the main shell to a login page), use `NavigateRouteAsync` with the `-/` qualifier:

```csharp
await navigator.NavigateRouteAsync(this, "-/Login");
```

WHY: `NavigateViewModelAsync` to a sibling ViewModel fails silently.

### `.UseThemeSwitching()` host builder requirement

The `dotnet new unoapp` template does NOT include `.UseThemeSwitching()`. Add it to the host builder chain for `IThemeService` to resolve:

```csharp
.Configure(host => host
    .UseThemeSwitching()  // required for IThemeService injection
    .UseNavigation(RegisterRoutes)
)
```

## ViewModel: MainViewModel

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

[Bindable]
public partial class MainViewModel : ObservableObject
{
    const int ReportsCount = 3;

    [ObservableProperty]
    string pageTitle = "";

    [ObservableProperty]
    ObservableCollection<NavMenuItem> menuItems = [];

    [ObservableProperty]
    ObservableCollection<NavMenuItem> footerMenuItems = [];

    // CRITICAL: Must be object?, NOT NavMenuItem? — Uno Navigation's SelectorNavigator
    // sets SelectedItem to a NavigationViewItem container, not your NavMenuItem model.
    // Using NavMenuItem? causes runtime TypeConverter errors.
    [ObservableProperty]
    object? selectedMenuItem;

    partial void OnSelectedMenuItemChanged(object? value)
    {
        // Must handle BOTH NavMenuItem (from programmatic selection) and
        // NavigationViewItem (from Uno Navigation's SelectorNavigator.Show)
        if (value is NavMenuItem { Children: null or { Count: 0 } } menuItem)
            PageTitle = menuItem.Title;
        else if (value is NavigationViewItem { Tag: string tag })
            PageTitle = tag;
    }

    public MainViewModel()
    {
        BuildMenuItems();
        BuildFooterMenuItems();
    }

    void BuildMenuItems()
    {
        ObservableCollection<NavMenuItem> items = [];

        // Simple leaf menu item (no children)
        items.Add(new NavMenuItem
        {
            Route = "Main/Home",
            Title = "Home",
            Subtitle = "Default app startup page",
            IconGlyph = "\u2302",
            ToolTip = "Home - Default app startup page"
        });

        // Parent menu item with children (multi-level)
        // Add children dynamically (e.g. from API data)
        // Smart collapsing: if only one child, add it directly instead of nesting
        if (ReportsCount > 0)
        {
            var reports = new NavMenuItem
            {
                Title = "Reports",
                Subtitle = "All available reports",
                IconGlyph = "\u2399",
                ToolTip = "Reports - All available reports",
                Children = []
            };
            var peers = ReportsCount > 1 ? reports.Children : items;
            for (int reportId = 0; reportId < ReportsCount; reportId++)
            {
                string description = "Report {reportid} description";
                peers.Add(new NavMenuItem
                {
                    Route = "Main/Report",
                    Data = reportId, // passed to destination viewmodel
                    Title = "Report {reportid}",
                    Subtitle = description,
                    IconGlyph = "\u2399",
                    ToolTip = $"Report {reportid} - {description}"
                });
            }
            if (reports.Children.Count > 0) items.Add(reports);
        }

        MenuItems = items;
        SelectedMenuItem ??= MenuItems.FirstOrDefault();
    }

    void BuildFooterMenuItems()
    {
        FooterMenuItems = [
            new () {
                Route = "Main/Settings",
                Title = "Settings",
                Subtitle = "User preferences",
                IconGlyph = "\uE713",
                ToolTip = "Settings - User preferences"
            }
        ];
    }
}
```

### Key ViewModel patterns

1. **Replace the entire `ObservableCollection`** (assign a new one to `MenuItems`) rather than clearing and re-adding items. This ensures the `NavigationView` properly re-renders.
2. **Set `SelectedMenuItem`** after building the menu to auto-navigate to the default page.
3. **Smart collapsing**: When a category has only one child, add it directly to the top-level items instead of nesting under a parent. This avoids unnecessary expand/collapse for single items:
   ```csharp
   var peers = children.Count > 1 ? parent.Children : items;
   foreach (var child in dataSource)
       peers.Add(new NavMenuItem { ... });
   if (parent.Children.Count > 0) items.Add(parent);
   ```
4. **`OnSelectedMenuItemChanged`**: Use the generated partial method from `[ObservableProperty]` to react to selection changes (e.g. update a page title, skip parent items).
5. **Passing data**: Set `Data` on `NavMenuItem` to pass objects (e.g. an entity from an API) to the destination page's viewmodel via Uno Navigation.

## Route naming convention

Routes follow the pattern `"RegionName/PageName"`, e.g.:
- `"Main/Home"` navigates to `HomePage` in the region named `"Main"`
- `"Main/Monitoring"` navigates to `MonitoringPage` in the `"Main"` region

The region name in the route must match the `uen:Region.Name` on the content `Grid` in XAML. Always use the fully qualified form — see "Critical routing details" above.

## Route registration for login + hamburger shell

The `dotnet new unoapp` template shows flat sibling routes. The hamburger menu pattern typically requires nested routes under a shell page, with a login page as a sibling:

```csharp
static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
{
    _ = views.Register(
        new ViewMap(ViewModel: typeof(ShellViewModel)),
        new ViewMap<LoginPage, LoginViewModel>(),
        new ViewMap<MainPage, MainViewModel>(),
        new ViewMap<HomePage, HomeViewModel>(),
        new ViewMap<SettingsPage, SettingsViewModel>(),
        new DataViewMap<DetailPage, DetailViewModel, DetailInfo>()  // typed navigation data
    );

    _ = routes.Register(
        new RouteMap("", View: views.FindByViewModel<ShellViewModel>(),
            Nested: [
                new("Login", View: views.FindByViewModel<LoginViewModel>(), IsDefault: true),
                new("Main", View: views.FindByViewModel<MainViewModel>(),
                    Nested: [
                        new("Home", View: views.FindByViewModel<HomeViewModel>(), IsDefault: true),
                        new("Settings", View: views.FindByViewModel<SettingsViewModel>()),
                        new("Detail", View: views.FindByViewModel<DetailViewModel>()),
                    ]
                ),
            ]
        )
    );
}
```

Key differences from template:
- Login as `IsDefault: true` sibling — shown first on app start
- Main has its own nested children with `IsDefault: true` on the landing page
- `DataViewMap` for pages receiving typed navigation data

## Dependencies

- `Uno.Extensions.Navigation.UI` (for `uen:Region.*` and `uen:Navigation.*` attached properties)
- `CommunityToolkit.Mvvm` (for `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`)
- `Microsoft.UI.Xaml` / WinUI 3 (for `NavigationView`, `NavigationViewItem`)
