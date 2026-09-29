namespace manage365.Configuration;

public static class LocalEnvFile
{
    public static string? ResolveEnvFilePath(params string?[] candidatePaths)
    {
        foreach (var path in candidatePaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        var searchDirs = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var baseDir in searchDirs)
        {
            try
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, ".env");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }
            catch
            {
                // Ignore invalid path issues during directory traversal
            }
        }

        return null;
    }

    public static void LoadIntoProcessEnvironment(string? path = null)
    {
        var resolvedPath = ResolveEnvFilePath(path);
        if (resolvedPath == null)
        {
            return;
        }

        foreach (var (key, value) in Parse(File.ReadLines(resolvedPath)))
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '\'' && value[^1] == '\'') ||
                 (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}
