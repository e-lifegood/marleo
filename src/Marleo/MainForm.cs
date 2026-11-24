using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Marleo;

/// <summary>
/// The viewer window: a WebView2 that hosts the web UI (web/index.html) plus the native glue —
/// reading files, watching them for changes, the sidebar tree, launching the editor, dialogs.
///
/// Host ⇄ page protocol (JSON via postMessage) is documented in docs/architecture.md.
/// </summary>
internal sealed class MainForm : Form
{
    private const string AppHost = "app.marleo";     // web/ folder (the UI itself)
    private const string FileHost = "local.marleo";  // images & media referenced by documents


    private readonly LaunchOptions _options;
    private readonly Settings _settings = Settings.Load();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _fileDebounce = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _treeDebounce = new() { Interval = 700 };

    private string _root = "";        // folder shown in the sidebar
    private string? _current;         // document on screen
    private string? _lastContent;
    private string? _startupError;
    private FileSystemWatcher? _fileWatcher;
    private FileSystemWatcher? _treeWatcher;
    private bool _dark;
    private bool _firstRenderDone;
    private bool _settledDone;
    private Mutex? _instance;
    private TaskCompletionSource<JsonObject?>? _legacyStorage; // set while importing v0.1.0 localStorage

    public MainForm(LaunchOptions options)
    {
        _options = options;
        Mark("ctor");
        Text = "Marleo";
        Icon = LoadAppIcon();
        Mark("icon");
        _dark = options.Theme switch { "dark" => true, "light" => false, _ => SystemUsesDarkTheme() };
        ApplyBackground();
        Controls.Add(_web);
        Mark("webctl");
        RestoreWindowState();
        Mark("placement");

        _fileDebounce.Tick += (_, _) => { _fileDebounce.Stop(); ReloadCurrent(force: false); };
        _treeDebounce.Tick += (_, _) => { _treeDebounce.Stop(); _ = SendTreeAsync(); };

        ResolveTarget(options.Target);
        Mark("doc");
    }

    // ---------------------------------------------------------------- startup

    private void ResolveTarget(string target)
    {
        if (Directory.Exists(target))
        {
            _root = target;
            _current = FolderScanner.PickDefaultDocument(target, null, _settings);
        }
        else if (File.Exists(target))
        {
            _root = Path.GetDirectoryName(target)!;
            _current = target;
        }
        else
        {
            // Show the error next to the closest folder that does exist; don't open anything by surprise.
            var dir = Path.GetDirectoryName(target);
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            _root = string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir;
            _startupError = $"Not found: {target}";
        }
        if (_current != null) _lastContent = ReadDocument(_current);
        UpdateTitle();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_current != null) WatchFile(_current); // needs the window handle (events are marshalled to the UI thread)
        try
        {
            await InitWebViewAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Marleo needs the Microsoft Edge WebView2 Runtime.\nIt ships with Windows 11; on older systems install it from:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "Marleo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task InitWebViewAsync()
    {
        // One shared profile folder: a second Marleo window reuses the running browser process → opens faster.
        Mark("load");
        // A viewer needs none of the browser's background services: skipping them shortens cold start.
        // (Every Marleo window must pass identical options, otherwise WebView2 refuses to share the profile.)
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-background-networking --disable-component-update --disable-sync " +
                                         "--disable-features=msSmartScreenProtection,msWebOOUI,msPdfOOUI,Translate,AutofillServerCommunication",
        };
        var env = await CoreWebView2Environment.CreateAsync(null, AppInfo.WebViewProfileDir, options);
        Mark("env");
        await _web.EnsureCoreWebView2Async(env);
        Mark("controller");
        var core = _web.CoreWebView2;

        var s = core.Settings;
        s.AreBrowserAcceleratorKeysEnabled = false; // F5, Ctrl+F, Ctrl+P… are handled by the app
        s.IsZoomControlEnabled = false;             // zoom is handled by the app (Ctrl +/-/0, Ctrl+wheel)
        s.IsStatusBarEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.AreDevToolsEnabled = true;                // F12, handy for contributors

        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        core.SetVirtualHostNameToFolderMapping(AppHost, webDir, CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter($"https://{FileHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += (_, a) => { a.Handled = true; OpenExternal(a.Uri); };

        if (LegacyMigration.StorageImportPending) await ImportLegacyStorageAsync(core, webDir);
        core.Navigate($"https://{AppHost}/index.html");

        // Screenshot/test runs must always finish, even if the page never reports back.
        if (_options.ScreenshotPath != null)
        {
            await Task.Delay(20000);
            OnSettled();
        }
    }

    /// <summary>
    /// One-time upgrade from v0.1.0 (mdv), whose UI kept theme, zoom, reading positions and highlight sets in
    /// localStorage under https://app.mdv. <see cref="LegacyMigration"/> copied that storage into this profile; here
    /// the old origin is mapped for a moment to load web/migrate.html, which posts every "mdv.*" key back. The keys
    /// are then replayed as "marleo.*" by a script that runs on https://app.marleo before the UI's own scripts.
    /// Gives up after 3 s: the UI then simply starts with its defaults.
    /// </summary>
    private async Task ImportLegacyStorageAsync(CoreWebView2 core, string webDir)
    {
        try
        {
            _legacyStorage = new TaskCompletionSource<JsonObject?>();
            core.SetVirtualHostNameToFolderMapping(LegacyMigration.AppHost, webDir, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate($"https://{LegacyMigration.AppHost}/migrate.html");
            var done = await Task.WhenAny(_legacyStorage.Task, Task.Delay(3000));
            var items = done == _legacyStorage.Task ? _legacyStorage.Task.Result : null;
            core.ClearVirtualHostNameToFolderMapping(LegacyMigration.AppHost);

            if (items is { Count: > 0 })
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    $$"""
                    (() => {
                      if (location.host !== "{{AppHost}}") return;
                      try {
                        if (localStorage.getItem("marleo.migrated")) return;
                        for (const [k, v] of Object.entries({{items.ToJsonString()}}))
                          if (typeof v === "string" && localStorage.getItem("marleo." + k) === null) localStorage.setItem("marleo." + k, v);
                        localStorage.setItem("marleo.migrated", "mdv");
                      } catch { }
                    })();
                    """);
            }
            AppendLog("migration.log", $"localStorage from https://{LegacyMigration.AppHost}: {(items is null ? "no answer" : $"{items.Count} keys")}");
        }
        catch (Exception ex) { AppendLog("migration.log", $"localStorage import failed: {ex.Message}"); }
        finally
        {
            _legacyStorage = null;
            LegacyMigration.CompleteStorageImport();
        }
    }

    // ---------------------------------------------------------------- page → host

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? m;
        try { m = JsonNode.Parse(e.WebMessageAsJson); } catch { return; }
        if (m is null) return;

        // The only message accepted from the v0.1.0 origin, and only while its storage is being imported.
        if (e.Source.StartsWith($"https://{LegacyMigration.AppHost}/", StringComparison.OrdinalIgnoreCase))
        {
            if ((string?)m["type"] == "migrate") _legacyStorage?.TrySetResult(m["items"] as JsonObject);
            return;
        }

        switch ((string?)m["type"])
        {
            case "ready":
                Mark("ready");
                Post(new JsonObject
                {
                    ["type"] = "init",
                    ["version"] = AppInfo.Version,
                    ["theme"] = _options.Theme,
                    ["highlights"] = new JsonArray(_options.Highlights.Select(h => (JsonNode?)h).ToArray()),
                    ["settingsPath"] = Settings.FilePath,
                    ["folderMode"] = Directory.Exists(_options.Target),
                    ["error"] = _startupError,
                });
                if (_current != null) SendDocument(reload: false, hash: null);
                _ = SendTreeAsync();
                break;
            case "close":
                Close();
                break;
            case "jserror":
                AppendLog("errors.log", (string?)m["message"]);
                break;
            case "log": // dev/test channel used by tests/*.js
                AppendLog("marleo.log", (string?)m["message"]);
                break;
            case "open":
                OpenPath((string?)m["path"], (string?)m["hash"], (bool?)m["newWindow"] ?? false);
                break;
            case "link":
                OpenLink((string?)m["href"] ?? "", (bool?)m["newWindow"] ?? false);
                break;
            case "external":
                OpenExternal((string?)m["url"]);
                break;
            case "edit":
                OpenInEditor((int?)m["line"] ?? 1);
                break;
            case "reload":
                ReloadCurrent(force: true);
                _ = SendTreeAsync();
                break;
            case "reveal":
                if (_current != null) Process.Start("explorer.exe", $"/select,\"{_current}\"");
                else Process.Start("explorer.exe", $"\"{_root}\"");
                break;
            case "copy":
                var text = (string?)m["text"] ?? (m["what"]?.ToString() == "path" ? _current : null);
                if (!string.IsNullOrEmpty(text)) { Clipboard.SetText(text); Toast("Copied"); }
                break;
            case "openFileDialog": ShowOpenFileDialog(); break;
            case "openFolderDialog": ShowOpenFolderDialog(); break;
            case "openSettings": OpenExternal(Settings.FilePath); break;
            case "theme":
                _dark = (bool?)m["dark"] ?? _dark;
                ApplyBackground();
                ApplyTitleBarTheme();
                break;
            case "drop":
                var dropped = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).FirstOrDefault();
                if (dropped != null) OpenPath(dropped, null, false);
                break;
            case "devtools":
                _web.CoreWebView2.OpenDevToolsWindow();
                break;
            case "rendered":
                OnRendered();
                break;
            case "settled":
                OnSettled();
                break;
        }
    }

    // MARLEO_TRACE=1 logs startup phases (ms since Main) to %LOCALAPPDATA%\marleo\startup.log
    // (MDV_TRACE, the pre-0.2.0 name, is still accepted).
    private static readonly bool Trace =
        Environment.GetEnvironmentVariable("MARLEO_TRACE") == "1" || Environment.GetEnvironmentVariable("MDV_TRACE") == "1";
    private readonly List<string> _marks = [];
    private void Mark(string phase) { if (Trace) _marks.Add($"{phase}={Program.Clock.ElapsedMilliseconds}"); }

    private static void AppendLog(string file, string? message)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.LocalDataDir);
            File.AppendAllText(Path.Combine(AppInfo.LocalDataDir, file), $"{DateTime.Now:s}\t{message}\n");
        }
        catch { /* logging must never break the viewer */ }
    }

    private void OnRendered()
    {
        if (_firstRenderDone) return;
        _firstRenderDone = true;
        Mark("rendered");
        var startupMs = (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        Post(new JsonObject { ["type"] = "stats", ["startupMs"] = startupMs });
        if (Trace)
            AppendLog("startup.log", $"{startupMs} ms total | {string.Join(" ", _marks)} | {_current}");
    }

    /// <summary>The first document is fully rendered (diagrams included): run dev hooks.</summary>
    private async void OnSettled()
    {
        if (_settledDone) return;
        _settledDone = true;

        // Dev hook (see CONTRIBUTING.md): run a script against the page, e.g. to exercise UI features in tests.
        if (_options.ScriptPath is { } script && File.Exists(script))
        {
            await _web.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(script));
            await Task.Delay(700);
        }

        if (_options.ScreenshotPath is { } shot)
        {
            await Task.Delay(400);
            await using (var fs = File.Create(shot))
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            Close();
        }
    }

    // ---------------------------------------------------------------- host → page

    // Messages are built as JsonObject (no reflection) — the first message is on the startup path.
    private void Post(JsonObject message) => _web.CoreWebView2?.PostWebMessageAsJson(message.ToJsonString());

    private void Toast(string text) => Post(new JsonObject { ["type"] = "toast", ["text"] = text });

    private void SendDocument(bool reload, string? hash)
    {
        if (_current is null) return;
        if (_lastContent is null) { Post(new JsonObject { ["type"] = "missing", ["path"] = _current }); return; }
        Post(new JsonObject
        {
            ["type"] = "doc",
            ["path"] = _current,
            ["name"] = Path.GetFileName(_current),
            ["dir"] = Path.GetDirectoryName(_current),
            ["root"] = _root,
            ["content"] = _lastContent,
            ["reload"] = reload,
            ["hash"] = hash,
        });
    }

    private async Task SendTreeAsync()
    {
        string root = _root;
        bool truncated = false;
        var tree = await Task.Run(() => FolderScanner.Scan(root, _settings, out truncated));
        if (root != _root || IsDisposed) return; // the user moved on meanwhile

        Post(new JsonObject
        {
            ["type"] = "tree",
            ["root"] = root,
            ["tree"] = JsonSerializer.SerializeToNode(tree, MarleoJson.Default.TreeNode),
            ["truncated"] = truncated,
        });
        WatchTree(truncated);

        if (_current is null && _startupError is null)
        {
            var first = FolderScanner.PickDefaultDocument(root, tree, _settings);
            if (first != null) OpenDocument(first, null);
            else Post(new JsonObject { ["type"] = "empty", ["root"] = root });
        }
    }

    // ---------------------------------------------------------------- opening things

    private void OpenPath(string? path, string? hash, bool newWindow)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (newWindow)
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"\"{path}\"") { UseShellExecute = false });
            return;
        }
        if (Directory.Exists(path))
        {
            SetRoot(path);
            _current = null;
            _lastContent = null;
            _fileWatcher?.Dispose();
            var doc = FolderScanner.PickDefaultDocument(path, null, _settings);
            if (doc != null) OpenDocument(doc, null);
            UpdateTitle();
            _ = SendTreeAsync();
        }
        else if (File.Exists(path))
        {
            if (!_settings.IsMarkdown(path)) { OpenExternal(path); return; }
            var dir = Path.GetDirectoryName(path)!;
            if (!IsUnder(path, _root)) { SetRoot(dir); _ = SendTreeAsync(); }
            OpenDocument(path, hash);
        }
        else Toast($"Not found: {path}");
    }

    private void OpenDocument(string path, string? hash)
    {
        _current = path;
        _lastContent = ReadDocument(path);
        WatchFile(path);
        UpdateTitle();
        SendDocument(reload: false, hash);
    }

    /// <summary>Resolves a link from the current document: relative path, absolute path or file:// URL.</summary>
    private void OpenLink(string href, bool newWindow)
    {
        string pathPart = href, hash = "";
        int h = href.IndexOf('#');
        if (h >= 0) { pathPart = href[..h]; hash = href[(h + 1)..]; }
        int q = pathPart.IndexOf('?');
        if (q >= 0) pathPart = pathPart[..q];
        if (pathPart.Length == 0) return; // pure #anchor: handled by the page

        string path;
        try
        {
            if (pathPart.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) path = new Uri(pathPart).LocalPath;
            else
            {
                var baseDir = _current != null ? Path.GetDirectoryName(_current)! : _root;
                path = Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(pathPart).Replace('/', '\\')));
            }
        }
        catch { Toast($"Bad link: {href}"); return; }

        // Wiki-style links without extension: [[notes]] → notes.md
        if (!File.Exists(path) && !Directory.Exists(path) && File.Exists(path + ".md")) path += ".md";
        OpenPath(path, hash.Length > 0 ? Uri.UnescapeDataString(hash) : null, newWindow);
    }

    private void OpenExternal(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Toast(ex.Message); }
    }

    private void SetRoot(string dir)
    {
        _root = dir;
        _treeWatcher?.Dispose();
        _treeWatcher = null;
    }

    // ---------------------------------------------------------------- editor

    private void OpenInEditor(int line)
    {
        if (_current is null) return;
        var exe = ResolveEditor();
        if (exe is null)
        {
            Toast("VS Code was not found. Set \"editorPath\" in settings.json (Ctrl+,).");
            return;
        }

        var folder = IsUnder(_current, _root) ? _root : Path.GetDirectoryName(_current)!;
        var args = _settings.EditorArgs
            .Replace("{file}", _current)
            .Replace("{line}", Math.Max(1, line).ToString())
            .Replace("{folder}", folder.TrimEnd('\\'));

        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/d /c \"\"{exe}\" {args}\"";
        }
        psi.Environment.Remove("ELECTRON_RUN_AS_NODE");
        try { Process.Start(psi); }
        catch (Exception ex) { Toast($"Could not start the editor: {ex.Message}"); }
    }

    private string? ResolveEditor()
    {
        if (!string.IsNullOrWhiteSpace(_settings.EditorPath))
        {
            var p = Environment.ExpandEnvironmentVariables(_settings.EditorPath);
            return File.Exists(p) ? p : FindOnPath(p);
        }

        // VS Code's CLI shim lives in <install>\bin\code.cmd; the GUI exe next to it starts faster and needs no console.
        foreach (var (shim, exeName) in new[] { ("code.cmd", "Code.exe"), ("code-insiders.cmd", "Code - Insiders.exe"), ("codium.cmd", "VSCodium.exe"), ("cursor.cmd", "Cursor.exe") })
        {
            if (FindOnPath(shim) is { } cmd)
            {
                var gui = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cmd)!, "..", exeName));
                return File.Exists(gui) ? gui : cmd;
            }
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(pf, "Microsoft VS Code", "Code.exe"),
            Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
            Path.Combine(local, "Programs", "VSCodium", "VSCodium.exe"),
            Path.Combine(local, "Programs", "cursor", "Cursor.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(p)) return p;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    // ---------------------------------------------------------------- reading & watching

    private static string? ReadDocument(string path)
    {
        // Editors and agents often write via "temp file + rename": retry briefly if the file is locked.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) when (attempt < 6) { Thread.Sleep(40); }
            catch { return null; }
        }
    }

    private void ReloadCurrent(bool force)
    {
        if (_current is null) return;
        var content = ReadDocument(_current);
        if (!force && content == _lastContent) return;
        _lastContent = content;
        SendDocument(reload: true, hash: null);
    }

    private void WatchFile(string path)
    {
        _fileWatcher?.Dispose();
        try
        {
            var w = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                SynchronizingObject = this,
            };
            void Bump() { _fileDebounce.Stop(); _fileDebounce.Start(); }
            w.Changed += (_, _) => Bump();
            w.Created += (_, _) => Bump();
            w.Deleted += (_, _) => Bump();
            w.Renamed += (_, _) => Bump();
            w.EnableRaisingEvents = true;
            _fileWatcher = w;
        }
        catch { _fileWatcher = null; } // network shares without change notifications, etc.
    }

    private void WatchTree(bool truncated)
    {
        if (_treeWatcher != null || truncated) return; // huge trees: don't watch, F5 refreshes
        try
        {
            var w = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024,
                SynchronizingObject = this,
            };
            void Maybe(string name)
            {
                if (_settings.IsMarkdown(name) || !Path.HasExtension(name)) { _treeDebounce.Stop(); _treeDebounce.Start(); }
            }
            w.Created += (_, a) => Maybe(a.Name ?? "");
            w.Deleted += (_, a) => Maybe(a.Name ?? "");
            w.Renamed += (_, a) => { Maybe(a.Name ?? ""); Maybe(a.OldName ?? ""); };
            w.EnableRaisingEvents = true;
            _treeWatcher = w;
        }
        catch { _treeWatcher = null; }
    }

    // ---------------------------------------------------------------- local files for <img> etc.

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml", [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon",
        [".avif"] = "image/avif", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav", [".ogg"] = "audio/ogg",
    };

    /// <summary>
    /// Serves https://local.marleo/C:/path/to/image.png from disk. Only media types are served,
    /// so a document can show its images but cannot read arbitrary files.
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web.CoreWebView2.Environment;
        try
        {
            var raw = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath).TrimStart('/');
            var path = raw.StartsWith("UNC/", StringComparison.OrdinalIgnoreCase)
                ? @"\\" + raw[4..].Replace('/', '\\')
                : raw.Replace('/', '\\');
            path = Path.GetFullPath(path);

            if (!Mime.TryGetValue(Path.GetExtension(path), out var mime) || !File.Exists(path))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var bytes = File.ReadAllBytes(path); // read fully: never keep a lock on the user's files
            e.Response = env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                $"Content-Type: {mime}\r\nCache-Control: no-cache");
        }
        catch
        {
            e.Response = env.CreateWebResourceResponse(null, 500, "Error", "");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith($"https://{AppHost}/", StringComparison.OrdinalIgnoreCase)) return;
        if (_legacyStorage != null && e.Uri.Equals($"https://{LegacyMigration.AppHost}/migrate.html", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true; // the viewer never navigates away from its UI
        if (e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            OpenExternal(e.Uri);
    }

    // ---------------------------------------------------------------- dialogs

    private void ShowOpenFileDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open Markdown file",
            Filter = "Markdown|" + string.Join(';', _settings.MarkdownExtensions.Select(x => "*" + x)) + "|All files|*.*",
            InitialDirectory = _root,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenPath(dlg.FileName, null, false);
    }

    private void ShowOpenFolderDialog()
    {
        using var dlg = new FolderBrowserDialog { Description = "Open folder", UseDescriptionForTitle = true, InitialDirectory = _root };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenPath(dlg.SelectedPath, null, false);
    }

    // ---------------------------------------------------------------- window chrome

    private void UpdateTitle()
    {
        var name = _current != null ? Path.GetFileName(_current) : Path.GetFileName(_root.TrimEnd('\\'));
        Text = string.IsNullOrEmpty(name) ? "Marleo" : $"{name} — Marleo";
    }

    private void ApplyBackground()
    {
        BackColor = _dark ? Color.FromArgb(0x16, 0x18, 0x1c) : Color.FromArgb(0xfb, 0xfb, 0xfa);
        _web.DefaultBackgroundColor = BackColor; // no white flash while the page loads
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyTitleBarTheme()
    {
        int on = _dark ? 1 : 0;
        _ = DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "marleo.ico");
            return File.Exists(ico) ? new Icon(ico) : Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch { return null; }
    }

    private void RestoreWindowState()
    {
        if (_options.WindowSize is { } forced)
        {
            Size = forced;
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }
        var st = WindowPlacement.Load();
        if (st is null)
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 1000);
            Size = new Size(Math.Min(1400, (int)(wa.Width * 0.85)), Math.Min(940, (int)(wa.Height * 0.9)));
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }

        // Cascade when another Marleo window is already open (a named mutex is much cheaper than listing processes).
        _instance = new Mutex(false, @"Local\marleo-window", out bool first);
        int offset = first ? 0 : 28 * Random.Shared.Next(1, 6);
        var bounds = new Rectangle(st.X + offset, st.Y + offset, st.Width, st.Height);
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else
        {
            Size = new Size(st.Width, st.Height);
            StartPosition = FormStartPosition.CenterScreen;
        }
        if (st.Maximized) base.WindowState = FormWindowState.Maximized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_options.ScreenshotPath is null)
        {
            var b = base.WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            new WindowPlacement { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, Maximized = base.WindowState == FormWindowState.Maximized }.Save();
        }
        _fileWatcher?.Dispose();
        _treeWatcher?.Dispose();
        _instance?.Dispose();
        base.OnFormClosing(e);
    }

    private static bool IsUnder(string path, string folder) =>
        !string.IsNullOrEmpty(folder) &&
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
