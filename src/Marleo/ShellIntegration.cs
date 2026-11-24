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
///  • App Paths entry → typing `marleo` in Explorer's address bar or Win+R works.
///  • The install folder on the user PATH → `marleo` works in any terminal.
///
/// Windows 11 does not let an app make itself the default handler. The user picks Marleo once
/// (Open with → Choose another app → Marleo → Always) and from then on double-click opens Marleo.
/// </summary>
internal static class ShellIntegration
{
    private const string ProgId = "marleo.md";
    private const string VerbKey = "marleo";
    private const string VerbLabel = "Open with Marleo";
    private static readonly string[] Extensions = [".md", ".markdown"];

    public static void Register(string exePath)
    {
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
        using (var k = classes.CreateSubKey(@"Applications\marleo.exe"))
        {
            k.SetValue("FriendlyAppName", "Marleo — Markdown viewer");
            k.CreateSubKey(@"shell\open\command").SetValue("", cmdFile);
            using var types = k.CreateSubKey("SupportedTypes");
            foreach (var ext in Extensions) types.SetValue(ext, "");
        }

        // Folder and folder-background context menus.
        foreach (var target in new[] { @"Directory\shell", @"Directory\Background\shell", @"Drive\shell" })
        {
            using var k = classes.CreateSubKey($@"{target}\{VerbKey}");
            k.SetValue("", VerbLabel);
            k.SetValue("Icon", icon);
            k.CreateSubKey("command").SetValue("", cmdDir);
        }

        // Default Apps (Settings) capabilities.
        using (var cap = Registry.CurrentUser.CreateSubKey(@"Software\Marleo\Capabilities"))
        {
            cap.SetValue("ApplicationName", "Marleo");
            cap.SetValue("ApplicationDescription", "Fast, clean Markdown viewer");
            cap.SetValue("ApplicationIcon", icon);
            using var fa = cap.CreateSubKey("FileAssociations");
            foreach (var ext in Extensions) fa.SetValue(ext, ProgId);
        }
        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue("Marleo", @"Software\Marleo\Capabilities");

        // App Paths: `marleo` from Win+R / Explorer address bar.
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\marleo.exe"))
        {
            k.SetValue("", exePath);
            k.SetValue("Path", Path.GetDirectoryName(exePath)!);
        }

        AddToUserPath(Path.GetDirectoryName(exePath)!);
        NotifyShell();
    }

    public static void Unregister()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
        if (classes != null)
        {
            classes.DeleteSubKeyTree(ProgId, false);
            classes.DeleteSubKeyTree(@"Applications\marleo.exe", false);
            foreach (var ext in Extensions)
            {
                using (var k = classes.OpenSubKey($@"{ext}\OpenWithProgids", writable: true))
                    k?.DeleteValue(ProgId, false);
                classes.DeleteSubKeyTree($@"SystemFileAssociations\{ext}\shell\{VerbKey}", false);
            }
            foreach (var target in new[] { @"Directory\shell", @"Directory\Background\shell", @"Drive\shell" })
                classes.DeleteSubKeyTree($@"{target}\{VerbKey}", false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Marleo", false);
        using (var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            reg?.DeleteValue("Marleo", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\marleo.exe", false);

        if (Environment.ProcessPath is { } exe) RemoveFromUserPath(Path.GetDirectoryName(exe)!);
        NotifyShell();
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
