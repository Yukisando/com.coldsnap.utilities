# ColdSnap Utilities

ColdSnap Utilities is a Unity package that collects small, practical tools used across ColdSnap projects. It currently includes editor workflow utilities, a few runtime helpers, a WebGL kiosk template, and package-maintenance scripts.

The package is aimed at teams that want focused utilities without adopting a larger framework or a heavily opinionated toolchain.

## What the package can do

### Editor tools

- `Platform Builder`: build Windows, macOS, Android APK, Android App Bundle, and WebGL targets from one editor window with saved scene selections and build preferences.
- `Scene Quick Open`: search and open scenes found under `Assets` without digging through the Project view.
- `Git Commands`: open a simple commit-and-push window from inside the editor.
- `Check for Updates`: compare the installed package commit/version with the latest on GitHub and update in one click.
- `Auto Group`: wrap the current top-level selection in a new parent object.
- `Center Pivot To Mesh CoM`: move a mesh pivot to its area-weighted center of mass while keeping the object visually in place.
- `Toggle Teleport Player On Play`: move a `Player` object to the Scene view camera when entering Play Mode, then restore it when returning to Edit Mode.
- `Auto Apply Android Keystore Passwords`: optionally fill Android signing passwords at editor startup for local workflows that always use the same keystore.

### Runtime helpers

- `FakeKeyboarder`: emit a configured string one character at a time to simulate typing.
- `FakeKeyboardTextTarget`: receive characters from `FakeKeyboarder` and write them into compatible UI text or input components.
- `SliderValueTextBinder`: drop on the GameObject with your `TMP_Text`, then wire it into a `Slider`'s `On Value Changed` event (Dynamic float) in the Inspector to display the slider's value with no code.

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

To show a slider's live value on a label without writing any code, add `SliderValueTextBinder` to the GameObject that has the `TMP_Text` (it will auto-fill the `text` field from the same object, or you can assign a different one). Then on the `Slider`, open the `On Value Changed (Single)` list in the Inspector, add an element, drag in that GameObject, and pick `SliderValueTextBinder.SetValue` under the `Dynamic float` group. The label updates immediately whenever the slider changes; the `Format`, `Prefix`, and `Suffix` fields on the binder control how the number is displayed.

## Package scope

Most of the package is editor-only and lives under `Editor/`, which means those tools are meant to speed up development workflows rather than ship in runtime builds. The `Runtime/` folder contains the smaller set of reusable runtime components included by this package.

## Package details

- Package name: `com.coldsnap.utilities`
- Display name: `ColdSnap Utilities`
- Supported Unity version: `2020.3` or newer

## Versioning

Unity Package Manager only treats this package as an update when the package source changes revision and exposes a newer package version.

The package uses standard semantic versioning (`MAJOR.MINOR.PATCH`), bumped automatically on every commit from the commit title ([conventional commits](https://www.conventionalcommits.org)):

| Commit title | Bump | Example |
|---|---|---|
| `fix:`, `chore:`, `docs:`, anything else | patch | 1.4.2 -> 1.4.3 |
| `feat:` (or `feat(scope):`) | minor | 1.4.2 -> 1.5.0 |
| `feat!:`, `fix!:`, or `BREAKING CHANGE` in the message | major | 1.4.2 -> 2.0.0 |

The versioned hook `.githooks/post-commit` writes the new version to `package.json`, adds the commit title to `CHANGELOG.md` (opened by the **Changelog** link in Package Manager), and folds both into the same commit. It skips amends, merges, rebases and cherry-picks.

Enable it once per clone (git never pushes hook settings):

```bash
git config core.hooksPath .githooks
```

To set a version by hand (a specific part or a pre-release label), run the script, then commit with the hook disabled for that commit:

```powershell
./tools/Bump-PackageVersion.ps1 -Part minor -PreReleaseLabel preview
```

```bash
COLDSNAP_SKIP_BUMP=1 git commit -am "chore: release 1.5.0-preview"
```

### Checking you're on the latest version

Package Manager's Version History only ever lists the installed entry for Git packages. Use **ColdSnap > Package > Check for Updates** instead: it shows the installed version and commit, the latest on GitHub with its title and changelog, and an **Update to latest** button. It also checks once per editor session and logs a warning when the project is behind.

If the consuming project references this package by Git URL on the `main` branch, Unity can pick up the new commit when the package refreshes. If the project references a fixed Git tag or a registry version, you still need to publish a new tag or package version there.

## Repository layout

- `Editor/`: Unity editor scripts and menu items
- `Runtime/`: reusable runtime components
- `WebGLTemplates/`: custom WebGL template files
- `kiosk/`: helper batch files related to kiosk setup
- `tools/`: package authoring helpers such as version bump scripts