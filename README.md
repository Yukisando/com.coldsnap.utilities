# ColdSnap Utilities

ColdSnap Utilities is a small Unity package that bundles a few reusable editor tools and a custom WebGL kiosk template.

It is intended for projects that want lightweight workflow helpers inside the Unity Editor without pulling in a larger framework.

## What is included

- `Platform Builder`: an editor window for building Windows, macOS, Android, App Bundle, and WebGL targets with saved scene selections and build settings.
- `Scene Quick Open`: an editor window that lists project scenes and opens them without digging through the Project view.
- `Git Commands`: a quick editor window for commit and push actions.
- `Auto Group`: groups the current top-level selection under a new parent object.
- `Center Pivot To Mesh CoM`: recenters a mesh pivot to its area-weighted center of mass while keeping the object in place.
- `Toggle Teleport Player On Play`: moves a `Player` object to the current Scene view camera when entering Play Mode, then restores it afterward.
- `WebGLTemplates/Kiosk`: a simple fullscreen-friendly WebGL template for kiosk-style deployments.

## How to use

After adding the package to a Unity project, the tools are available from the `ColdSnap` menu in the Unity Editor.

Use `ColdSnap/Scenes/Quick Open` to bring up a searchable list of scenes found under the project's `Assets` folder and load one directly.

Most utilities are editor-only and live under the `Editor` folder, so they are meant to improve workflow during development rather than ship in runtime builds.

## Package details

- Package name: `com.coldsnap.utilities`
- Display name: `ColdSnap Utilities`
- Supported Unity version: `2020.3` or newer

## Versioning

Unity Package Manager only treats this package as an update when the package source changes revision and exposes a newer package version.

This package now uses a date-based semver scheme:

- `20260409.0.0`: first release on April 9, 2026
- `20260409.1.0`: second release on the same day

The repository also includes a GitHub Actions workflow that automatically bumps `package.json` after each push to `main`.

Use `tools/Bump-PackageVersion.ps1` if you want to bump locally before pushing:

```powershell
./tools/Bump-PackageVersion.ps1
./tools/Bump-PackageVersion.ps1 -PreReleaseLabel preview
```

Automatic flow:

1. Push your package changes to `main`.
2. GitHub Actions updates `package.json` to the next date-based version and pushes that commit.
3. In the consuming Unity project, refresh or update the package source.

Manual flow:

1. Run the version bump script.
2. Commit the updated `package.json`.
3. Push `main`.
4. In the consuming Unity project, refresh or update the package source.

If the consuming project references this package by Git URL on the `main` branch, Unity can pick up the new commit when the package refreshes. If the project references a fixed Git tag or a registry version, you still need to publish a new tag or package version there.

## Repository layout

- `Editor/`: Unity editor scripts and menu items
- `WebGLTemplates/`: custom WebGL template files
- `kiosk/`: helper batch files related to kiosk setup
- `tools/`: package authoring helpers such as version bump scripts