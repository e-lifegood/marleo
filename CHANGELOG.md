# Changelog

All notable changes to Marleo (called mdv up to 0.1.0) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-11-23

**mdv is now Marleo** — *mark* (highlight) + *leo* (Spanish for "I read"). Same app, same logo, new name.

### Changed
- Renamed everything user-visible: the app is **Marleo**, the command and exe are `marleo` / `marleo.exe`, the
  Explorer verb is "Open with Marleo", and the repository moved to
  [e-lifegood/marleo](https://github.com/e-lifegood/marleo) (old links redirect).
- New locations: install folder `%LOCALAPPDATA%\Programs\marleo`, settings `%APPDATA%\marleo\settings.json`, data
  (WebView2 profile, logs, window state) `%LOCALAPPDATA%\marleo`.
- Release assets are `marleo-win-x64.zip` and `marleo-win-x64-standalone.zip`; the one-line installer is
  `https://github.com/e-lifegood/marleo/releases/latest/download/install.ps1`.
- `MARLEO_TRACE=1` enables startup tracing (`MDV_TRACE=1` still works).

### Kept for existing users
- `mdv` still works as a command: an App Paths alias for Win+R and the Explorer address bar, and an `mdv.cmd` shim
  next to `marleo.exe` for terminals.
- Automatic upgrade from mdv 0.1.0, on `--register` and on the first start:
  - `settings.json` and `window.json` are copied to the new folders;
  - view preferences, reading positions and saved highlight sets are carried over from the old WebView2 profile
    (if that cannot be read, they start from the defaults once);
  - the Explorer entries, App Paths entry and PATH entry written by mdv are removed (on a plain start only if Marleo
    is registered); `--unregister` removes both the new and the old entries.
- The one-line installer replaces an existing `%LOCALAPPDATA%\Programs\mdv` install (unregisters it, deletes it and
  takes it off PATH).

### Notes
- If you had made mdv the default app for `.md` files, choose Marleo once: right-click a `.md` file → **Open with** →
  **Choose another app** → **Marleo** → **Always**. Windows does not let apps set this themselves.
- The old `%APPDATA%\mdv` and `%LOCALAPPDATA%\mdv` folders are left in place; delete them when you no longer need them.

## [0.1.0] - 2026-11-21

First public release.

### Added
- Native Windows viewer (.NET 10 + WebView2) for Markdown files and folders.
- Explorer integration for the current user: "Open with mdv" on files, folders and folder backgrounds, default-app
  registration, `mdv` in the Explorer address bar / Win+R, `mdv` on PATH (`--register` / `--unregister`).
- Clean reading view with light/dark themes, outline with scroll-spy, folder tree with quick filter, history, zoom,
  per-file reading position, print.
- Multi-color highlighting: 10 colors, occurrence counts, next/previous per term (Alt+1..9), scrollbar markers,
  match-case / whole-word, saved highlight sets.
- Edit in VS Code (Ctrl+E) at the line being read; configurable editor command.
- Live reload that handles direct writes, temp-file-and-rename saves, and delete/recreate.
- GitHub-flavored Markdown: tables, task lists, alerts, front matter, syntax highlighting, Mermaid diagrams, local images.
- End-to-end tests that drive the real app (`tests/run-tests.ps1`).

[0.2.0]: https://github.com/e-lifegood/marleo/releases/tag/v0.2.0
[0.1.0]: https://github.com/e-lifegood/marleo/releases/tag/v0.1.0
