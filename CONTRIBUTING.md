# Contributing to mdv

Thanks for helping! mdv is meant to stay **small, fast and focused on reading**. Ideas that make reading Markdown on
Windows nicer are welcome; turning mdv into an editor is out of scope (that's what the *Edit* button is for).

## Ways to help

- **Report a bug** — open an issue with the steps, the Markdown that shows the problem and your Windows version.
  `%LOCALAPPDATA%\mdv\errors.log` contains JavaScript errors, if any.
- **Suggest a feature** — describe the reading problem first, then your idea.
- **Send a pull request** — small, focused PRs are easiest to review. For bigger changes, open an issue first.

## Development setup

You need Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js 20+](https://nodejs.org).

```powershell
git clone https://github.com/e-lifegood/mdv.git
cd mdv
npm ci                 # front-end libraries (markdown-it, highlight.js, DOMPurify, Mermaid)
npm run vendor         # copy them to web/vendor (the build also does this automatically if missing)
dotnet run --project src/Mdv -- samples
```

Handy flags while developing:

| Flag / variable | What it does |
| --- | --- |
| `--theme light\|dark` | force a theme |
| `--highlight "a,b"` | start with highlights |
| `--window 1600x1000` | initial window size |
| `--screenshot out.png` | render, save a PNG of the page and exit |
| `--run-script file.js` | run JavaScript in the page after the first render (used by the tests) |
| `MDV_TRACE=1` | log startup phases to `%LOCALAPPDATA%\mdv\startup.log` |
| <kbd>F12</kbd> | DevTools for the page |

The `web/` folder is copied next to the exe on build. After changing HTML/CSS/JS, run `dotnet build` and open a new
mdv window (the page is loaded once per window; <kbd>F5</kbd> only re-reads the document).

## Tests

```powershell
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

This builds mdv and drives the real app: a UI smoke test (highlighting, navigation, find, outline, syntax
highlighting, alerts, task lists, images, Mermaid, theme) and live-reload tests (direct write, temp file + rename,
delete + recreate). A screenshot is saved to `tests\out\smoke.png`. Please run it before opening a PR, and add a check
to `tests/ui-smoke.js` when you add a feature.

## Code guidelines

- **C#**: nullable enabled, file-scoped namespaces, keep the host thin. Anything visual belongs in `web/`.
- **JavaScript**: ES modules, no framework, no build step. Keep modules focused (`render`, `marks`, `toc`, `tree`).
- **CSS**: use the color tokens in `:root[data-theme=…]`; every new color needs a light *and* a dark value.
- **Startup is a feature**: don't add work to the startup path; load heavy things lazily (see how Mermaid is loaded).
- **Security**: documents are untrusted input. Keep DOMPurify in the pipeline, don't widen the CSP, and don't let the
  `local.mdv` handler serve non-media files.
- `.editorconfig` describes formatting. PowerShell scripts must stay ASCII-only (Windows PowerShell 5.1 reads files
  without a BOM as ANSI).

### Adding a highlight.js language

Add the file to the list in `scripts/vendor.mjs`, a `<script>` tag in `web/index.html`, run `npm run vendor`, and
mention it in the changelog.

### Updating front-end libraries

`npm install --save-exact <package>@<version>`, `npm run vendor`, run the tests, commit `package*.json` and
`THIRD_PARTY_NOTICES.md` (`web/vendor` is generated and not committed).

## Releasing (maintainers)

1. Update the version in `src/Mdv/Mdv.csproj` and `CHANGELOG.md`.
2. Commit, then tag: `git tag v0.2.0 && git push origin v0.2.0`.
3. GitHub Actions builds both zips and the `install.ps1` one-line installer and publishes the release.

## Code of conduct

Be kind and constructive. Assume good intent, focus on the problem, and help newcomers.
