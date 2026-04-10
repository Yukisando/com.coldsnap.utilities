# ColdSnap Utilities

ColdSnap Utilities is a Unity package that collects small, practical tools used across ColdSnap projects. It currently includes editor workflow utilities, a few runtime helpers, a WebGL kiosk template, and package-maintenance scripts.

The package is aimed at teams that want focused utilities without adopting a larger framework or a heavily opinionated toolchain.

## What the package can do

### Editor tools

- `Platform Builder`: build Windows, macOS, Android APK, Android App Bundle, and WebGL targets from one editor window with saved scene selections and build preferences.
- `Scene Quick Open`: search and open scenes found under `Assets` without digging through the Project view.
- `Git Commands`: open a simple commit-and-push window from inside the editor.
- `Auto Group`: wrap the current top-level selection in a new parent object.
- `Center Pivot To Mesh CoM`: move a mesh pivot to its area-weighted center of mass while keeping the object visually in place.
- `Toggle Teleport Player On Play`: move a `Player` object to the Scene view camera when entering Play Mode, then restore it when returning to Edit Mode.
- `Auto Apply Android Keystore Passwords`: optionally fill Android signing passwords at editor startup for local workflows that always use the same keystore.

### Runtime helpers

- `FakeKeyboarder`: emit a configured string one character at a time to simulate typing.
- `FakeKeyboardTextTarget`: receive characters from `FakeKeyboarder` and write them into compatible UI text or input components.

### Templates and package helpers

- `WebGLTemplates/Kiosk`: a fullscreen-friendly WebGL template for kiosk-style deployments.
- `kiosk/`: helper batch scripts for kiosk setup.
- `tools/Bump-PackageVersion.ps1`: bump the package version locally using the repo's date-based versioning scheme.

## Typical usage

After adding the package to a Unity project, editor utilities appear under the `ColdSnap` menu.

Use `ColdSnap/Platform Builder` when you want repeatable build settings and fast target switching.

Use `ColdSnap/Scenes/Quick Open` when you need to jump between scenes quickly during development.

Use `ColdSnap/Tools/Auto Apply Android Keystore Passwords` only if your local setup consistently uses the same signing credentials. It defaults to off because shared packages should not assume a single keystore workflow.

For runtime typing simulations, add `FakeKeyboarder` to a GameObject and either subscribe to its `OnType` event in code or hook its inspector event to another component. If you want a ready-made bridge, add `FakeKeyboardTextTarget`, assign the source `FakeKeyboarder`, and point it at a compatible text-bearing component. The bridge is reflection-based so it can work with common Unity UI and TextMeshPro-style text targets without taking a hard TMP package dependency.

## Package scope

Most of the package is editor-only and lives under `Editor/`, which means those tools are meant to speed up development workflows rather than ship in runtime builds. The `Runtime/` folder contains the smaller set of reusable runtime components included by this package.

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
- `Runtime/`: reusable runtime components
- `WebGLTemplates/`: custom WebGL template files
- `kiosk/`: helper batch files related to kiosk setup
- `tools/`: package authoring helpers such as version bump scripts