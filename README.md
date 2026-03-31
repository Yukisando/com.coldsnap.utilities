# ColdSnap Utilities

ColdSnap Utilities is a small Unity package that bundles a few reusable editor tools and a custom WebGL kiosk template.

It is intended for projects that want lightweight workflow helpers inside the Unity Editor without pulling in a larger framework.

## What is included

- `Platform Builder`: an editor window for building Windows, macOS, Android, App Bundle, and WebGL targets with saved scene selections and build settings.
- `Git Commands`: a quick editor window for commit and push actions.
- `Auto Group`: groups the current top-level selection under a new parent object.
- `Center Pivot To Mesh CoM`: recenters a mesh pivot to its area-weighted center of mass while keeping the object in place.
- `Toggle Teleport Player On Play`: moves a `Player` object to the current Scene view camera when entering Play Mode, then restores it afterward.
- `WebGLTemplates/Kiosk`: a simple fullscreen-friendly WebGL template for kiosk-style deployments.

## How to use

After adding the package to a Unity project, the tools are available from the `ColdSnap` menu in the Unity Editor.

Most utilities are editor-only and live under the `Editor` folder, so they are meant to improve workflow during development rather than ship in runtime builds.

## Package details

- Package name: `com.coldsnap.utilities`
- Display name: `ColdSnap Utilities`
- Supported Unity version: `2020.3` or newer

## Repository layout

- `Editor/`: Unity editor scripts and menu items
- `WebGLTemplates/`: custom WebGL template files
- `kiosk/`: helper batch files related to kiosk setup