# AGENTS

This repository is a Unity package.

When adding, renaming, moving, or deleting Unity-tracked assets or folders in this package, always keep the matching `.meta` files in sync.

Rules for this repo:

- Create a `.meta` file for any new non-hidden file or folder that Unity would normally import and track.
- Do not create `.meta` files for hidden repo-management content such as `.git`, `.github`, `.vscode`, or similar dot-prefixed files and folders unless the user explicitly asks for that.
- When creating new visible documentation or source files at the package root or under package folders like `Editor`, `Runtime`, `WebGLTemplates`, `kiosk`, or `tools`, also create their `.meta` files.
- If a change would leave a Unity-tracked file without its matching `.meta`, fix that as part of the same task instead of waiting to be asked.
- Preserve existing GUIDs when updating or moving tracked files whenever practical.