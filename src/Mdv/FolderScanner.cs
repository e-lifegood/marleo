namespace Mdv;

/// <summary>A node of the sidebar tree. Serialized to JSON for the web UI.</summary>
internal sealed record TreeNode(string Name, string Path, bool Dir, List<TreeNode>? Children);

/// <summary>
/// Builds the tree of Markdown files under a folder.
/// Only folders that (recursively) contain Markdown are kept, so the sidebar stays short.
/// Hard limits keep it fast even if someone opens C:\ by accident.
/// </summary>
internal static class FolderScanner
{
    private const int MaxDepth = 10;
    private const int MaxEntries = 20000;

    public static TreeNode Scan(string root, Settings settings, out bool truncated)
    {
        int budget = MaxEntries;
        var ignored = new HashSet<string>(settings.IgnoredFolders, StringComparer.OrdinalIgnoreCase);
        var node = ScanDir(new DirectoryInfo(root), 0, settings, ignored, ref budget)
                   ?? new TreeNode(DisplayName(root), root, true, []);
        truncated = budget <= 0;
        return node with { Name = DisplayName(root) };
    }

    private static TreeNode? ScanDir(DirectoryInfo dir, int depth, Settings settings, HashSet<string> ignored, ref int budget)
    {
        var children = new List<TreeNode>();
        IEnumerable<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos(); }
        catch { return null; } // access denied, vanished, etc.

        var dirs = new List<DirectoryInfo>();
        var files = new List<FileInfo>();
        foreach (var e in entries)
        {
            if (--budget <= 0) break;
            if ((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            if (e is DirectoryInfo d)
            {
                if (!ignored.Contains(d.Name) && !d.Name.StartsWith('.')) dirs.Add(d);
            }
            else if (e is FileInfo f && settings.IsMarkdown(f.Name)) files.Add(f);
        }

        if (depth < MaxDepth)
        {
            foreach (var d in dirs.OrderBy(d => d.Name, NaturalComparer.Instance))
            {
                if (budget <= 0) break;
                // Skip junctions/symlinks to avoid cycles.
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var child = ScanDir(d, depth + 1, settings, ignored, ref budget);
                if (child is { Children.Count: > 0 }) children.Add(child);
            }
        }

        foreach (var f in files.OrderBy(f => f.Name, NaturalComparer.Instance))
            children.Add(new TreeNode(f.Name, f.FullName, false, null));

        return new TreeNode(dir.Name, dir.FullName, true, children);
    }

    /// <summary>Picks the document to show when a folder is opened: README, index, then the first file.</summary>
    public static string? PickDefaultDocument(string folder, TreeNode? tree, Settings settings)
    {
        try
        {
            var top = Directory.EnumerateFiles(folder).Where(settings.IsMarkdown).ToList();
            foreach (var preferred in new[] { "readme", "index", "_index", "home" })
            {
                var hit = top.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            if (top.Count > 0) return top.OrderBy(f => Path.GetFileName(f) ?? f, NaturalComparer.Instance).First();
        }
        catch { /* fall through */ }
        return tree is null ? null : FirstFile(tree);
    }

    private static string? FirstFile(TreeNode n)
    {
        if (!n.Dir) return n.Path;
        foreach (var c in n.Children ?? []) { var f = FirstFile(c); if (f != null) return f; }
        return null;
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}

/// <summary>"file2" before "file10", like Explorer.</summary>
internal sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
}
