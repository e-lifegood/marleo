using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Marleo;

/// <summary>
/// Registers Marleo with Windows Explorer for the current user only (HKCU — no admin rights needed).
///
/// What gets added:
///  • ProgID "marleo.md" + OpenWithProgids for .md/.markdown → Marleo appears in "Open with".
///  • Default Apps capabilities → Marleo appears in Settings › Apps › Default apps.
///  • "Open with Marleo" on .md files, on folders and on folder backgrounds.
///  • App Paths entries → typing `marleo` (or the pre-0.2.0 name `mdv`) in Explorer's address bar or Win+R works.
///  • The install folder on the user PATH → `marleo` (and `mdv`, via the mdv.cmd shim) works in any terminal.
///
/// Windows 11 does not let an app make itself the default handler. The user picks Marleo once
/// (Open with → Choose another app → Marleo → Always) and from then on double-click opens Marleo.
///
/// Up to v0.1.0 the app was called mdv; <see cref="RemoveLegacyEntries"/> removes what that version registered.
/// </summary>
internal static class ShellIntegration
{
    private const string ProgId = "marleo.md";
    private const string VerbKey = "marleo";
    private const string VerbLabel = "Open with Marleo";
    private const string AppKey = @"Software\Marleo";
    private const string RegisteredName = "Marleo";
    private const string ExeName = "marleo.exe";
    private static readonly string[] Extensions = [".md", ".markdown"];
    private static readonly string[] FolderTargets = [@"Directory\shell", @"Directory\Background\shell", @"Drive\shell"];
    private const string AppPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

    // What v0.1.0 (mdv) registered.
    private const string LegacyProgId = "mdv.md";
    private const string LegacyVerbKey = "mdv";
    private const string LegacyAppKey = @"Software\mdv";
    private const string LegacyRegisteredName = "mdv";
    private const string LegacyExeName = "mdv.exe";

    /// <summary>The command alias kept for v0.1.0 habits: `mdv` in Win+R / the address bar runs marleo.exe.</summary>
    private const string AliasExeName = LegacyExeName;

    public static bool IsRegistered
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
            return k != null;
        }
    }

    public static void Register(string exePath)
    {
        RemoveLegacyEntries(Path.GetDirectoryName(exePath));

        string cmdFile = $"\"{exePath}\" \"%1\"";
        string cmdDir = $"\"{exePath}\" \"%V\"";
        string icon = $"\"{exePath}\",0";

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        // ProgID
        using (var k = classes.CreateSubKey(ProgId))
        {
            k.SetValue("", "Markdown document");
            k.SetValue("FriendlyTypeName", "Markdown document");
            k.CreateSubKey("DefaultIcon").SetValue("", icon);
            k.CreateSubKey(@"shell\open").SetValue("FriendlyAppName", "Marleo");
            k.CreateSubKey(@"shell\open\command").SetValue("", cmdFile);
        }

        foreach (var ext in Extensions)
        {
            using var k = classes.CreateSubKey($@"{ext}\OpenWithProgids");
            k.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

            // Explicit "Open with Marleo" verb on the file, independent of the default app.
            using var verb = classes.CreateSubKey($@"SystemFileAssociations\{ext}\shell\{VerbKey}");
            verb.SetValue("", VerbLabel);
            verb.SetValue("Icon", icon);
            verb.CreateSubKey("command").SetValue("", cmdFile);
        }

        // Applications\marleo.exe — how "Open with" lists the app.
        using (var k = classes.CreateSubKey($@"Applications\{ExeName}"))
        {
            k.SetValue("FriendlyAppName", "Marleo — Markdown viewer");
            k.CreateSubKey(@"shell\open\command").SetValue("", cmdFile);
            using var types = k.CreateSubKey("SupportedTypes");
            foreach (var ext in Extensions) types.SetValue(ext, "");
        }

        // Folder and folder-background context menus.
        foreach (var target in FolderTargets)
        {
            using var k = classes.CreateSubKey($@"{target}\{VerbKey}");
            k.SetValue("", VerbLabel);
            k.SetValue("Icon", icon);
            k.CreateSubKey("command").SetValue("", cmdDir);
        }

        // Default Apps (Settings) capabilities.
        using (var cap = Registry.CurrentUser.CreateSubKey($@"{AppKey}\Capabilities"))
        {
            cap.SetValue("ApplicationName", "Marleo");
            cap.SetValue("ApplicationDescription", "Fast, clean Markdown viewer");
            cap.SetValue("ApplicationIcon", icon);
            using var fa = cap.CreateSubKey("FileAssociations");
            foreach (var ext in Extensions) fa.SetValue(ext, ProgId);
        }
        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue(RegisteredName, $@"{AppKey}\Capabilities");

        // App Paths: `marleo` — and the alias `mdv` — from Win+R / Explorer address bar.
        foreach (var name in new[] { ExeName, AliasExeName })
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{AppPaths}\{name}");
            k.SetValue("", exePath);
            k.SetValue("Path", Path.GetDirectoryName(exePath)!);
        }

        AddToUserPath(Path.GetDirectoryName(exePath)!);
        NotifyShell();
    }

    /// <summary>Removes everything <see cref="Register"/> added, plus anything left by v0.1.0 (mdv).</summary>
    public static void Unregister()
    {
        string? dir = Environment.ProcessPath is { } exe ? Path.GetDirectoryName(exe) : null;
        RemoveEntries(ProgId, VerbKey, ExeName, AppKey, RegisteredName);
        Registry.CurrentUser.DeleteSubKeyTree($@"{AppPaths}\{ExeName}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{AppPaths}\{AliasExeName}", false);
        if (dir != null) RemoveFromUserPath(dir);
        RemoveLegacyEntries(dir);
        NotifyShell();
    }

    /// <summary>
    /// Removes what v0.1.0 (when the app was called mdv) registered: ProgID mdv.md, its OpenWithProgids values, the
    /// "Open with mdv" verbs, Applications\mdv.exe, Software\mdv + its RegisteredApplications value, the App Paths
    /// entry when it still points to the old mdv.exe (from 0.2.0 on it is the alias for marleo.exe) and the old
    /// install folder on the user PATH. Idempotent; <paramref name="currentDir"/> (Marleo's own folder) is never
    /// taken off PATH, in case Marleo was unzipped over the old install.
    /// </summary>
    public static void RemoveLegacyEntries(string? currentDir)
    {
        RemoveEntries(LegacyProgId, LegacyVerbKey, LegacyExeName, LegacyAppKey, LegacyRegisteredName);

        var legacyDirs = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "mdv"),
        };
        string legacyAppPath = $@"{AppPaths}\{LegacyExeName}";
        string? oldExe;
        using (var k = Registry.CurrentUser.OpenSubKey(legacyAppPath))
            oldExe = k?.GetValue("") as string;
        if (oldExe != null && !oldExe.Trim('"').EndsWith(@"\" + ExeName, StringComparison.OrdinalIgnoreCase))
        {
            if (Path.GetDirectoryName(oldExe.Trim('"')) is { Length: > 0 } d) legacyDirs.Add(d);
            Registry.CurrentUser.DeleteSubKeyTree(legacyAppPath, false);
        }

        foreach (var d in legacyDirs)
            if (currentDir is null || !PathEquals(d, currentDir)) RemoveFromUserPath(d);
    }

    private static void RemoveEntries(string progId, string verbKey, string exeName, string appKey, string registeredName)
    {
        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
        {
            if (classes != null)
            {
                classes.DeleteSubKeyTree(progId, false);
                classes.DeleteSubKeyTree($@"Applications\{exeName}", false);
                foreach (var ext in Extensions)
                {
                    using (var k = classes.OpenSubKey($@"{ext}\OpenWithProgids", writable: true))
                        k?.DeleteValue(progId, false);
                    classes.DeleteSubKeyTree($@"SystemFileAssociations\{ext}\shell\{verbKey}", false);
                }
                foreach (var target in FolderTargets)
                    classes.DeleteSubKeyTree($@"{target}\{verbKey}", false);
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(appKey, false);
        using (var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            reg?.DeleteValue(registeredName, false);
    }

    // ---- PATH helpers -------------------------------------------------------------------

    private static void AddToUserPath(string dir)
    {
        using var env = Registry.CurrentUser.CreateSubKey("Environment");
        var current = env.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Any(p => PathEquals(p, dir))) return;
        parts.Add(dir);
        env.SetValue("Path", string.Join(';', parts), RegistryValueKind.ExpandString);
        BroadcastEnvironmentChange();
    }

    private static void RemoveFromUserPath(string dir)
    {
        using var env = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
        if (env?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) is not string current) return;
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.RemoveAll(p => PathEquals(p, dir)) == 0) return;
        env.SetValue("Path", string.Join(';', parts), RegistryValueKind.ExpandString);
        BroadcastEnvironmentChange();
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Environment.ExpandEnvironmentVariables(a).TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // ---- Win32 --------------------------------------------------------------------------

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    private static void BroadcastEnvironmentChange() =>
        SendMessageTimeout((IntPtr)0xffff /* HWND_BROADCAST */, 0x001A /* WM_SETTINGCHANGE */, UIntPtr.Zero, "Environment", 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);
}
