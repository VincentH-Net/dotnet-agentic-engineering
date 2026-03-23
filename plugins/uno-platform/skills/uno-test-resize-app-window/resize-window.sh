#!/usr/bin/env bash
# Resize a running macOS app window using the Accessibility API.
# Usage: resize-window.sh APP_NAME WIDTH HEIGHT

set -euo pipefail

APP_NAME="${1:?Usage: resize-window.sh APP_NAME WIDTH HEIGHT}"
WIDTH="${2:?Usage: resize-window.sh APP_NAME WIDTH HEIGHT}"
HEIGHT="${3:?Usage: resize-window.sh APP_NAME WIDTH HEIGHT}"

cat << SWIFT | swift -
import Cocoa

let appName = "${APP_NAME}"
let targetWidth: CGFloat = ${WIDTH}
let targetHeight: CGFloat = ${HEIGHT}

let apps = NSWorkspace.shared.runningApplications.filter { \$0.localizedName == appName }
guard let app = apps.first else { print("App '\(appName)' not found"); exit(1) }

let appRef = AXUIElementCreateApplication(app.processIdentifier)
var windowValue: CFTypeRef?
let err = AXUIElementCopyAttributeValue(appRef, kAXWindowsAttribute as CFString, &windowValue)
guard err == .success, let windows = windowValue as? [AXUIElement], let win = windows.first else {
    print("No windows found (error: \(err.rawValue))")
    exit(1)
}

var size = CGSize(width: targetWidth, height: targetHeight)
let sizeValue = AXValueCreate(.cgSize, &size)!
let setErr = AXUIElementSetAttributeValue(win, kAXSizeAttribute as CFString, sizeValue)
print(setErr == .success ? "Resized to \(Int(targetWidth))x\(Int(targetHeight))" : "Resize failed: \(setErr.rawValue)")
SWIFT
