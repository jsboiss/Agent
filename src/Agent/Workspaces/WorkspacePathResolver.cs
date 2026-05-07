namespace Agent.Workspaces;

public static class WorkspacePathResolver
{
    public static string GetRepositoryRootPath(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        if (directory.Parent is not null && directory.Parent.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            return directory.Parent.Parent?.FullName ?? directory.FullName;
        }

        return directory.FullName;
    }

    public static string GetDefaultAgentWorkspacePath(string contentRootPath)
    {
        return Path.Combine(GetRepositoryRootPath(contentRootPath), "App_Data", "CodexWorkspace");
    }

    public static string NormalizeRootPath(string rootPath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(rootPath));
        }

        var basePath = GetRepositoryRootPath(contentRootPath);
        var resolved = Path.IsPathRooted(rootPath)
            ? Path.GetFullPath(rootPath)
            : Path.GetFullPath(Path.Combine(basePath, rootPath));

        Directory.CreateDirectory(resolved);

        return resolved;
    }
}
