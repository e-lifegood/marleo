# Changelog

All notable changes to mdv are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/)
and the project uses [Semantic Versioning](https://semver.org/).

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

[0.1.0]: https://github.com/e-lifegood/mdv/releases/tag/v0.1.0
