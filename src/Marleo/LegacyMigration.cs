namespace Marleo;

/// <summary>
/// One-time upgrade from v0.1.0, when the app was called mdv. Runs on the first start of Marleo and on --register.
/// Every step only fills in what is missing, so running it again is harmless.
///
///  • %APPDATA%\mdv\settings.json and %LOCALAPPDATA%\mdv\window.json are copied when Marleo has none yet.
///  • The old WebView2 profile's localStorage (theme, zoom, reading positions, highlight sets — stored under the
///    origin https://app.mdv) is copied into Marleo's new profile; <see cref="MainForm"/> then reads it on the old
///    origin and replays it under https://app.marleo (see MainForm.ImportLegacyStorageAsync and web/migrate.html).
///  • Explorer entries written by v0.1.0 are removed, but only if Marleo is registered itself, so starting a
///    portable or development copy never strips a working integration without replacing it.
/// </summary>
internal static class LegacyMigration
{
    /// <summary>The v0.1.0 origin of the web UI; mapped only while its localStorage is being imported.</summary>
    public const string AppHost = "app.mdv";

    private static readonly string LocalDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mdv");
    private static readonly string RoamingDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdv");

    private static string DoneMarker => Path.Combine(AppInfo.LocalDataDir, "migrated-from-mdv");
    private static string StoragePendingMarker => Path.Combine(AppInfo.LocalDataDir, "migrate-localstorage.pending");

    /// <summary>True until the copied localStorage has been imported by the first window.</summary>
    public static bool StorageImportPending => File.Exists(StoragePendingMarker);

    /// <summary>Called on every start: once done it costs a single File.Exists.</summary>
    public static void RunOnce()
    {
        if (File.Exists(DoneMarker)) return;

        Try(() => CopyIfMissing(Path.Combine(RoamingDir, "settings.json"), Settings.FilePath));
        Try(() => CopyIfMissing(Path.Combine(LocalDir, "window.json"), WindowPlacement.FilePath));
        Try(CopyLocalStorage);
        Try(() =>
        {
            if (ShellIntegration.IsRegistered && Environment.ProcessPath is { } exe)
                ShellIntegration.RemoveLegacyEntries(Path.GetDirectoryName(exe));
        });
        Try(() =>
        {
            Directory.CreateDirectory(AppInfo.LocalDataDir);
            File.WriteAllText(DoneMarker, $"{DateTime.Now:s}\n");
        });
    }

    public static void CompleteStorageImport() => Try(() => File.Delete(StoragePendingMarker));

    /// <summary>
    /// Copies only the old profile's "Local Storage" folder (a self-contained LevelDB), and only into a profile that
    /// does not exist yet — caches and the rest of the browser state are not worth carrying over.
    /// </summary>
    private static void CopyLocalStorage()
    {
        var source = Path.Combine(LocalDir, "WebView2", "EBWebView", "Default", "Local Storage");
        if (!Directory.Exists(source) || Directory.Exists(AppInfo.WebViewProfileDir)) return;

        CopyDirectory(source, Path.Combine(AppInfo.WebViewProfileDir, "EBWebView", "Default", "Local Storage"));
        File.WriteAllText(StoragePendingMarker, "");
    }

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (Path.GetFileName(file).Equals("LOCK", StringComparison.OrdinalIgnoreCase)) continue; // LevelDB lock
            try { File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false); }
            catch (IOException) { /* in use by a running mdv: skip, the rest is still useful */ }
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static void Try(Action step)
    {
        try { step(); }
        catch { /* migration is best effort and must never stop Marleo from opening */ }
    }
}
