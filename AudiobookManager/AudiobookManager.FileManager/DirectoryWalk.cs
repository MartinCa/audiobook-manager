namespace AudiobookManager.FileManager;

/// <summary>
/// Directory enumeration that does not follow symbolic links.
///
/// <c>SearchOption.AllDirectories</c> deliberately includes reparse points - mounted drives and
/// symbolic links - and the documentation is explicit that a link forming a loop puts the search
/// into an infinite loop. Libraries assembled from several shares are exactly where symlinked
/// media directories show up, which is the deployment this application is built for.
/// </summary>
public static class DirectoryWalk
{
    /// <summary>
    /// Whether this path is a symbolic link (or another reparse point, such as a junction or a
    /// mount point) rather than a real directory.
    /// </summary>
    public static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            // A path that cannot be inspected is not one to recurse into either.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Every directory beneath <paramref name="root"/>, links excluded and never recursed into.
    /// <paramref name="root"/> itself is not yielded, matching
    /// <c>Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)</c>, which this
    /// replaces.
    /// </summary>
    public static IEnumerable<string> EnumerateDirectoriesRecursively(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            foreach (var directory in Directory.EnumerateDirectories(pending.Pop()))
            {
                if (IsLink(directory))
                {
                    continue;
                }

                yield return directory;
                pending.Push(directory);
            }
        }
    }
}
