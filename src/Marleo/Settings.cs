using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marleo;

/// <summary>
/// User-editable settings, stored in %APPDATA%\marleo\settings.json.
/// The file is created with defaults the first time Marleo runs so it is easy to discover.
/// </summary>
internal sealed class Settings
{
    /// <summary>Editor executable. Empty = auto-detect VS Code (PATH, then default install folders).</summary>
    public string EditorPath { get; set; } = "";

    /// <summary>
    /// Arguments passed to the editor. Placeholders: {file}, {line}, {folder}.
    /// Default opens a NEW VS Code window with the folder as workspace and jumps to the line you were reading.
    /// Examples: Notepad++ → "-n{line} \"{file}\""   Sublime → "\"{file}:{line}\""
    /// </summary>
    public string EditorArgs { get; set; } = "--new-window \"{folder}\" --goto \"{file}:{line}\"";

    /// <summary>Folders skipped when building the sidebar tree.</summary>
    public List<string> IgnoredFolders { get; set; } =
        [".git", "node_modules", ".vs", ".vscode", ".idea", "bin", "obj", "dist", "build", "__pycache__", ".venv", "venv", ".next", "target"];

    /// <summary>File extensions treated as Markdown.</summary>
    public List<string> MarkdownExtensions { get; set; } = [".md", ".markdown", ".mdown", ".mkd", ".mdx"];

    public static string FilePath => Path.Combine(AppInfo.RoamingDataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), MarleoJson.Default.Settings) ?? new Settings();

            var s = new Settings();
            Directory.CreateDirectory(AppInfo.RoamingDataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, MarleoJson.Default.Settings));
            return s;
        }
        catch
        {
            return new Settings(); // a broken settings file must never stop the viewer from opening
        }
    }

    public bool IsMarkdown(string path) =>
        MarkdownExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Window position/size, remembered between runs (%LOCALAPPDATA%\marleo\window.json).</summary>
internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1180;
    public int Height { get; set; } = 860;
    public bool Maximized { get; set; }

    private static string FilePath => Path.Combine(AppInfo.LocalDataDir, "window.json");

    public static WindowPlacement? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), MarleoJson.Default.WindowPlacement) : null; }
        catch { return null; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.LocalDataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, MarleoJson.Default.WindowPlacement));
        }
        catch { /* not critical */ }
    }
}

/// <summary>
/// Compile-time JSON serializers (no reflection): faster startup and trimming-friendly.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(WindowPlacement))]
[JsonSerializable(typeof(TreeNode))]
internal sealed partial class MarleoJson : JsonSerializerContext;
