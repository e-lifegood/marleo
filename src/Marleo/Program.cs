using System.Diagnostics;

namespace Marleo;

/// <summary>
/// Entry point.
///
///   marleo                      open the current directory
///   marleo &lt;file.md | folder&gt;  open a document or a folder of documents
///   marleo --highlight "a,b,c"  start with these terms highlighted
///   marleo --theme light|dark   force a theme for this window
///   marleo --register           add Marleo to Explorer (HKCU, no admin needed)
///   marleo --unregister         remove everything --register added
///   marleo --screenshot out.png render, save a PNG of the page and exit (used for docs)
///   marleo --run-script test.js dev hook: run JavaScript in the page after the first render
///   marleo --window 1600x1000   initial window size in pixels (handy for screenshots)
/// </summary>
internal static class Program
{
    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    [STAThread]
    private static int Main(string[] args)
    {
        Clock.Restart(); // static fields initialise lazily; make sure the startup clock starts here
        var options = LaunchOptions.Parse(args, Environment.CurrentDirectory);

        if (options.Command is "register" or "unregister")
        {
            try
            {
                if (options.Command == "register") ShellIntegration.Register(Environment.ProcessPath!);
                else ShellIntegration.Unregister();
                if (!options.Quiet)
                    MessageBox.Show(options.Command == "register"
                        ? "Marleo was added to Explorer.\n\n• Right-click a folder → Show more options → Open with marleo\n• Right-click a .md file → Open with → Marleo (tick \"Always\" to make it the default)\n• Type marleo (or marleo) in Explorer's address bar to open the current folder"
                        : "Marleo was removed from Explorer.", "Marleo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!options.Quiet) MessageBox.Show(ex.Message, "Marleo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        if (options.Command == "version")
        {
            MessageBox.Show($"Marleo {AppInfo.Version}", "Marleo");
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(options));
        return 0;
    }
}

internal sealed class LaunchOptions
{
    public string? Command { get; private set; }
    public string Target { get; private set; } = "";
    public List<string> Highlights { get; } = [];
    public string? Theme { get; private set; }
    public string? ScreenshotPath { get; private set; }
    public string? ScriptPath { get; private set; }
    public Size? WindowSize { get; private set; }
    public bool Quiet { get; private set; }

    public static LaunchOptions Parse(string[] args, string cwd)
    {
        var o = new LaunchOptions();
        string? target = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--register": o.Command = "register"; break;
                case "--unregister": o.Command = "unregister"; break;
                case "--version": case "-v": o.Command = "version"; break;
                case "--quiet": case "-q": o.Quiet = true; break;
                case "--theme" when i + 1 < args.Length: o.Theme = args[++i]; break;
                case "--screenshot" when i + 1 < args.Length: o.ScreenshotPath = Path.GetFullPath(args[++i], cwd); break;
                case "--run-script" when i + 1 < args.Length: o.ScriptPath = Path.GetFullPath(args[++i], cwd); break;
                case "--window" when i + 1 < args.Length:
                    var wh = args[++i].ToLowerInvariant().Split('x');
                    if (wh.Length == 2 && int.TryParse(wh[0], out var w) && int.TryParse(wh[1], out var h)) o.WindowSize = new Size(w, h);
                    break;
                case "--highlight" when i + 1 < args.Length:
                    o.Highlights.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                default: target ??= a; break;
            }
        }

        // Explorer passes "%V" for folder backgrounds; for a drive root that arrives as `C:"`.
        target = target?.Trim().Trim('"');
        if (string.IsNullOrEmpty(target) || target == ".") target = cwd;
        if (target.Length == 2 && target[1] == ':') target += "\\";
        try { o.Target = Path.GetFullPath(target, cwd); } catch { o.Target = cwd; }
        return o;
    }
}

internal static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>%LOCALAPPDATA%\marleo — WebView2 profile, window state.</summary>
    public static string LocalDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "marleo");

    /// <summary>%APPDATA%\marleo — user settings (settings.json).</summary>
    public static string RoamingDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "marleo");
}
