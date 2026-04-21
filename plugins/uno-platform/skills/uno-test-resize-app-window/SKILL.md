---
name: uno-test-resize-app-window
description: Resize a running Uno Platform desktop app window on macOS for visual testing. Use when you need to verify responsive layout reflow, test breakpoints, or validate UI at different window sizes. Requires macOS Accessibility API access.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.1"
  category: testing
  platform: macOS
---

# Resize App Window on macOS for Visual Testing

Resize a running Uno Platform desktop app window programmatically using the macOS Accessibility API. Useful for verifying responsive layout behavior at specific widths without manual interaction.

## Prerequisites

- macOS with Accessibility API access granted to the terminal/IDE
- A running Uno Platform desktop app (use `uno_app_start` / `uno_app_get_runtime_info` first)

## Usage

Run [resize-window.sh](resize-window.sh) with three arguments:

```bash
resize-window.sh APP_NAME WIDTH HEIGHT
```

- `APP_NAME` — the app's process name (from `uno_app_get_runtime_info` → `Process Name`)
- `WIDTH` — target window width in pixels
- `HEIGHT` — target window height in pixels

## Common Test Widths

| Width | Columns (typical) | Use case |
|-------|-------------------|----------|
| 500   | 1 | Mobile / narrow |
| 750   | 2 | Tablet / medium |
| 1024  | 3 | Desktop / wide |
| 1280  | 3-4 | Large desktop |

## Workflow

1. Start the app with `uno_app_start`
2. Confirm running with `uno_app_get_runtime_info` (note `Process Name`)
3. Take initial screenshot with `uno_app_get_screenshot`
4. Run `resize-window.sh "AppName" 500 800`
5. Take screenshot and verify layout reflow
6. Repeat at additional widths as needed
7. Restore original size when done

## Troubleshooting

- **"App not found"**: Verify the process name matches exactly (case-sensitive). Check with `uno_app_get_runtime_info`.
- **"Resize failed: -25211"**: Accessibility access not granted. The user must grant access in System Settings > Privacy & Security > Accessibility for the terminal app.
- **Xcode `objc` warnings in stderr**: Harmless; ignore. The resize still succeeds.

## Notes

- This technique uses the macOS Accessibility API (`AXUIElement`), not AppleScript. AppleScript `System Events` requires separate assistive access and often fails for non-scriptable apps.
- The script targets the first window of the process. If the app has multiple windows, only the first is resized.
- Window position can also be set by replacing `kAXSizeAttribute` with `kAXPositionAttribute` and using `CGPoint` instead of `CGSize`.
