# Architecture

mdv is two small pieces that talk through `postMessage`:

```mermaid
flowchart LR
    subgraph Host["mdv.exe — C# / WinForms (.NET 10)"]
        P[Program.cs<br>arguments, --register]
        M[MainForm.cs<br>WebView2 host, file watching,<br>editor launch, dialogs]
        F[FolderScanner.cs<br>Markdown tree]
        S[ShellIntegration.cs<br>Explorer registry entries]
        C[Settings.cs<br>settings.json, window.json]
    end
    subgraph Page["web/ — HTML, CSS, JS (ES modules)"]
        A[app.js<br>state, commands, keyboard]
        R[render.js<br>markdown-it, DOMPurify,<br>highlight.js, Mermaid]
        K[marks.js<br>multi-color highlighting]
        T[toc.js / tree.js]
    end
    Explorer -->|"mdv.exe path"| P --> M
    M <-->|"JSON messages"| A
    M -->|"https://local.mdv/…"| Disk[(images on disk)]
    M -->|"Code.exe --new-window --goto file:line"| VSCode
```

## Why WebView2 + a thin native host

- **Startup.** No Electron: the browser engine is the WebView2 runtime that ships with Windows 11, shared with Edge.
  The exe is ~200 KB of IL (ReadyToRun-compiled) plus the WebView2 interop DLLs.
- **Rendering quality.** Markdown, code and diagrams render with the same mature JS libraries GitHub-style tools use.
- **Native integration where it matters.** File watching, the Explorer registry, the editor launch and file dialogs
  are plain .NET.

## Startup path

1. `Program.Main` parses arguments (`LaunchOptions`).
2. `MainForm` reads `settings.json`, restores the window and reads the document *before* WebView2 exists, so the
   content is ready as soon as the page is.
3. WebView2 starts (shared profile in `%LOCALAPPDATA%\mdv\WebView2`: a second mdv window reuses the browser process and
   opens much faster) and navigates to `https://app.mdv/index.html`, a virtual host mapped to the `web/` folder.
4. The page posts `ready`; the host answers with `init` and `doc`; the page renders and posts `rendered` (text visible)
   and then `settled` (Mermaid done). The folder tree is scanned on a background thread and sent afterwards.

Set `MDV_TRACE=1` to log the timing of each phase to `%LOCALAPPDATA%\mdv\startup.log`. Typical numbers on a laptop:
~0.5 s to first render when another mdv window is open, ~0.9 s fully cold (dominated by the WebView2 browser
process starting).

## Messages

Page → host (`chrome.webview.postMessage`):

| type | payload | effect |
| --- | --- | --- |
| `ready` | — | host sends `init`, the document and the tree |
| `open` | `path`, `hash?`, `newWindow?` | open a file or folder (tree clicks, history) |
| `link` | `href`, `newWindow?` | resolve a link relative to the current document |
| `external` | `url` | open in the default browser / app |
| `edit` | `line` | launch the editor at that line |
| `reload` | — | re-read the file and rescan the tree |
| `reveal`, `copy`, `openFileDialog`, `openFolderDialog`, `openSettings`, `close`, `devtools` | | as named |
| `theme` | `dark` | match the native title bar to the page theme |
| `drop` | + `File` objects | open what was dropped on the window |
| `rendered`, `settled` | — | startup timing, dev hooks |
| `jserror`, `log` | `message` | appended to `errors.log` / `mdv.log` in `%LOCALAPPDATA%\mdv` |

Host → page (`PostWebMessageAsJson`):

| type | payload |
| --- | --- |
| `init` | `version`, `theme?`, `highlights[]`, `settingsPath`, `folderMode`, `error?` |
| `doc` | `path`, `name`, `dir`, `root`, `content`, `reload`, `hash?` |
| `tree` | `root`, `tree` (`{name, path, dir, children}`), `truncated` |
| `missing`, `empty`, `toast`, `stats` | |

## Live reload

`FileSystemWatcher` watches the document's folder filtered to its file name, listening to *changed, created, deleted
and renamed* — so it also catches editors and agents that save by writing a temp file and renaming it over the
original. Events are debounced (150 ms), the file is read with `FileShare.ReadWrite | Delete` and a short retry if it
is locked, and the page re-renders only when the text actually changed. The page keeps the scroll position, or sticks
to the bottom if you were there (handy when an agent is appending).

A second watcher on the folder (subdirectories included) refreshes the sidebar tree when Markdown files or folders
appear, disappear or are renamed. Very large trees (over 20 000 entries) are truncated and not watched.

## Rendering pipeline (`web/js/render.js`)

1. Strip YAML front matter (shown as a collapsible block) and remember how many lines it took.
2. `markdown-it` with `html: true` and `linkify`, plus small core rules: `data-line` on every block (offset by the front
   matter), GitHub-style heading ids, task lists, GitHub alerts, code fences with highlight.js, Mermaid placeholders,
   tables wrapped for horizontal scrolling.
3. `DOMPurify` sanitizes the HTML (scripts, event handlers, iframes and `<style>` are removed).
4. Relative `img`/`video`/`audio` sources are rewritten to `https://local.mdv/<absolute path>`.
5. Highlights are re-applied, the outline rebuilt, and Mermaid runs if the document has diagrams (its 5 MB script is
   loaded only then).

## Highlighting (`web/js/marks.js`)

Each term becomes a Unicode-aware regular expression (optionally case-sensitive and/or whole-word using
`\p{L}\p{N}` boundaries). A `TreeWalker` visits text nodes of the rendered document, skipping SVG, Mermaid, existing
marks and UI chrome, and wraps matches in `<mark class="hl hl-N">`. Earlier terms win on overlap. The find bar uses
the same engine with its own class and may nest inside term marks. The overview ruler draws one tick per occurrence,
de-duplicated per pixel row.

## Security model

- The page can only navigate inside `https://app.mdv/`; every other navigation is cancelled and http(s)/mailto links
  open in the default browser.
- Documents are sanitized; the page has a strict Content-Security-Policy (`script-src 'self'`).
- `https://local.mdv/` serves **only media types** (images, audio, video) from disk, so a document can show its images
  but cannot read other files.
- mdv never writes to your documents. It writes only `%APPDATA%\mdv\settings.json` and files under `%LOCALAPPDATA%\mdv`.

## Explorer integration (`ShellIntegration.cs`)

All keys live under `HKCU` (no admin rights):

| Key | Purpose |
| --- | --- |
| `Software\Classes\mdv.md` | ProgID used by "Open with" and default-app selection |
| `Software\Classes\.md\OpenWithProgids`, `.markdown\…` | list mdv for Markdown files |
| `Software\Classes\SystemFileAssociations\.md\shell\mdv` | "Open with mdv" verb on `.md` files |
| `Software\Classes\Applications\mdv.exe` | friendly name and supported types |
| `Software\Classes\Directory\shell\mdv`, `Directory\Background\shell\mdv`, `Drive\shell\mdv` | folder context menus |
| `Software\mdv\Capabilities` + `Software\RegisteredApplications` | entry in Settings › Default apps |
| `Software\Microsoft\Windows\CurrentVersion\App Paths\mdv.exe` | `mdv` in the Explorer address bar and Win+R |
| `Environment\Path` | `mdv` in terminals |

On Windows 11 the classic verbs appear under **Show more options**. Showing them in the compact menu requires an
`IExplorerCommand` handler in a signed sparse package — a good future contribution.

## Project layout

```text
src/Mdv/            native host (C#)
web/                UI: index.html, app.css, js/*.js, vendor/ (generated)
scripts/            vendor.mjs (copies libraries), make-icon.ps1, get-mdv.ps1 (release installer)
tests/              run-tests.ps1 + scripts that drive the real app
samples/            documents used for manual checks, tests and screenshots
docs/               this file and README images
```
