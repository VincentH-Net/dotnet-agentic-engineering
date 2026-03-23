---
name: uno-hamburgermenu-databinding
description: This skill demonstrates how to use the Uno Navigation Extensions `NavigationView` and MVVM to create a data-bound, hierarchical hamburger menu with dynamic navigation. Use when implementing a data-bound hamburger menu.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0"
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
   - `uen:Navigation.Data="{x:Bind Data}"` to pass data to the destination viewmodel
   - `MenuItemsSource="{Binding Children}"` to enable nested child items (note: uses `{Binding}` not `{x:Bind}` due to known Uno Platform x:Bind source generator issues in nested DataTemplate contexts — see unoplatform/uno#7279, #18509, #8471)
4. **Content region** uses `uen:Region.Navigator="Visibility"` so pages are shown/hidden rather than recreated.

### Critical routing details
1. The routes for the Main page and the default page within Main - Home - must be registered as default routes: pass `IsDefault:true` into their `RouteMap` constructor. This ensures that on the initial navigate to MainPage, Home is displayed within it.

## ViewModel: MainViewModel

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

[Bindable]
public partial class MainViewModel : ObservableObject
{
    const int ReportsCount = 3;

    [ObservableProperty]
    ObservableCollection<NavMenuItem> menuItems = [];

    [ObservableProperty]
    ObservableCollection<NavMenuItem> footerMenuItems = [];

    [ObservableProperty]
    NavMenuItem? selectedMenuItem;

    partial void OnSelectedMenuItemChanged(NavMenuItem? value)
    {
        // React to menu selection changes, e.g. update page title
        // Skip parent items (items with children) if desired:
        if (value?.Children?.Any() == true)
            return;

        // Update title or perform other actions based on selected route
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

The region name in the route must match the `uen:Region.Name` on the content `Grid` in XAML.

## Dependencies

- `Uno.Extensions.Navigation.UI` (for `uen:Region.*` and `uen:Navigation.*` attached properties)
- `CommunityToolkit.Mvvm` (for `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`)
- `Microsoft.UI.Xaml` / WinUI 3 (for `NavigationView`, `NavigationViewItem`)
